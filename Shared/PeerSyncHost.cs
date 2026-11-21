using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace StickyNoteWPF.Sync;

// 临时设备端点：没有账号数据库，也不依赖单独安装的 ASP.NET Core 服务。
public sealed class PeerSyncHost : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly X509Certificate2 _certificate;
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<CancellationToken, Task<TransportSnapshot>> _read;
    private readonly Func<SyncData, string, CancellationToken, Task<TransportSnapshot>> _write;
    private readonly ConcurrentDictionary<int, Task> _connections = new();
    private readonly SemaphoreSlim _slots = new(4);
    private readonly Task _acceptLoop;
    private int _nextConnection;
    public PeerInvitation Invitation { get; }
    public bool IsActive => !_stop.IsCancellationRequested && !_acceptLoop.IsCompleted;

    public PeerSyncHost(IPAddress address, int port,
        Func<CancellationToken, Task<TransportSnapshot>> read,
        Func<SyncData, string, CancellationToken, Task<TransportSnapshot>> write,
        TimeSpan? lifetime = null)
    {
        if (!PeerInvitation.IsLocalAddress(address)) throw new PeerSyncException("设备直连仅支持局域网 IPv4 地址。");
        var duration = lifetime ?? TimeSpan.FromMinutes(10);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(10)) throw new ArgumentOutOfRangeException(nameof(lifetime));
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=StickyNote-Peer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        // 导入临时私钥，兼容 Windows 的 TLS 实现；关闭后释放，不安装信任证书。
        _certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        _read = read; _write = write;
        _listener = new TcpListener(address, port);
        try { _listener.Start(8); }
        catch { _certificate.Dispose(); _stop.Dispose(); _slots.Dispose(); throw; }
        Invitation = new(1, address.ToString(), ((IPEndPoint)_listener.LocalEndpoint).Port,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            _certificate.GetCertHashString(HashAlgorithmName.SHA256), DateTimeOffset.UtcNow.Add(duration));
        _stop.CancelAfter(duration);
        _acceptLoop = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var tcp = await _listener.AcceptTcpClientAsync(_stop.Token);
                if (!_slots.Wait(0)) { tcp.Dispose(); continue; }
                var id = Interlocked.Increment(ref _nextConnection);
                var job = HandleAsync(tcp);
                _connections[id] = job;
                _ = job.ContinueWith(_ => _connections.TryRemove(id, out var ignored), TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        finally { _listener.Stop(); }
    }

    private async Task HandleAsync(TcpClient tcp)
    {
        using (tcp)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(55));
            var ct = timeout.Token;
            try
            {
                using var tls = new SslStream(tcp.GetStream(), false);
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, ct);
                var request = await PeerWire.ReadAsync<PeerRequest>(tls, ct);
                PeerResponse response;
                if (request.Protocol != 1 || !PeerWire.SecretEquals(Invitation.Key, request.Key))
                    response = new("unauthorized", "配对密钥无效。");
                else
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        if (request.Operation == "read") response = new("ok", Snapshot: await _read(ct));
                        else if (request.Operation == "write" && request.ExpectedVersion is { Length: > 0 and <= 128 })
                        {
                            var error = SnapshotTools.Validate(request.Data);
                            response = error is not null ? new("invalid", error)
                                : new("ok", Snapshot: await _write(request.Data!, request.ExpectedVersion, ct));
                        }
                        else response = new("invalid", "设备同步请求无效。");
                    }
                    catch (PeerSyncException ex) { response = new("rejected", ex.Message); }
                    catch (OperationCanceledException) { return; }
                    catch { response = new("error", "对方设备无法保存或读取数据，本次同步未确认。"); }
                }
                await PeerWire.WriteAsync(tls, response, ct);
            }
            catch (Exception ex) when (ex is System.IO.IOException or SocketException or AuthenticationException
                or OperationCanceledException or System.Text.Json.JsonException or PeerSyncException) { }
            finally { _slots.Release(); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await _acceptLoop;
        await Task.WhenAll(_connections.Values);
        _certificate.Dispose(); _stop.Dispose(); _slots.Dispose();
    }
}
