using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;

namespace StickyNoteWPF.Sync;

public sealed class PeerSyncClient : ISyncTransport
{
    private readonly PeerInvitation _invitation;
    private readonly CancellationTokenSource _stop = new();

    public PeerSyncClient(string pairingCode) => _invitation = PeerInvitation.Parse(pairingCode);

    private async Task<TransportSnapshot> SendAsync(PeerRequest request)
    {
        if (_invitation.ExpiresAt <= DateTimeOffset.UtcNow) throw new PeerSyncException("配对码已过期。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(_invitation.Address, _invitation.Port, ct);
        using var tls = new SslStream(tcp.GetStream(), false, (_, certificate, _, _) => certificate is not null
            && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(_invitation.Fingerprint),
                certificate.GetCertHash(HashAlgorithmName.SHA256)));
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "StickyNote-Peer", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, ct);
        await PeerWire.WriteAsync(tls, request, ct);
        var response = await PeerWire.ReadAsync<PeerResponse>(tls, ct);
        if (response.Status != "ok") throw new PeerSyncException(response.Message ?? "对方拒绝了此次同步。");
        var snapshot = response.Snapshot;
        if (snapshot is null || snapshot.Version is not { Length: > 0 and <= 128 }
            || SnapshotTools.Validate(snapshot.Data) is not null)
            throw new PeerSyncException("对方返回了无效同步数据。");
        return snapshot;
    }

    public Task<TransportSnapshot> ReadAsync() => SendAsync(new(1, _invitation.Key, "read"));
    public async Task<string> WriteAsync(SyncData data, string expectedVersion)
    {
        var copy = PeerWire.Snapshot(data);
        var result = await SendAsync(new(1, _invitation.Key, "write", expectedVersion, copy.Data));
        if (SnapshotTools.Hash(result.Data) != copy.Version) throw new PeerSyncException("对方确认的内容与本次发送内容不同，请重新同步。");
        return result.Version;
    }

    public void Dispose() { _stop.Cancel(); _stop.Dispose(); }
}
