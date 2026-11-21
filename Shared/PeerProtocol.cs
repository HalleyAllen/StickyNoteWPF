using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace StickyNoteWPF.Sync;

public record PeerInvitation(int Protocol, string Address, int Port, string Key, string Fingerprint,
    DateTimeOffset ExpiresAt)
{
    public string Encode() => "sn1:" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this, SnapshotTools.Json));

    public static PeerInvitation Parse(string code)
    {
        try
        {
            if (code.Length > 4096 || !code.Trim().StartsWith("sn1:", StringComparison.Ordinal)) throw new FormatException();
            var invitation = JsonSerializer.Deserialize<PeerInvitation>(Convert.FromBase64String(code.Trim()[4..]), SnapshotTools.Json);
            if (invitation is null || invitation.Protocol != 1 || invitation.Port is < 1 or > 65535
                || !IPAddress.TryParse(invitation.Address, out var address) || !IsLocalAddress(address)
                || !HexSecret(invitation.Key) || !HexSecret(invitation.Fingerprint)) throw new FormatException();
            if (invitation.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidOperationException("配对码已过期，请让对方重新开启设备同步。");
            return invitation;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        { throw new InvalidOperationException("配对码无效，请复制完整的 sn1: 配对码。", ex); }
    }

    private static bool HexSecret(string? value) => value is { Length: 64 }
        && !value.AsSpan().ContainsAnyExcept("0123456789ABCDEFabcdef");

    public static bool IsLocalAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    public static IReadOnlyList<string> LocalAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address).Where(a => IsLocalAddress(a) && !IPAddress.IsLoopback(a))
        .Select(a => a.ToString()).Distinct().Append("127.0.0.1").ToList();
}

public record PeerRequest(int Protocol, string Key, string Operation, string? ExpectedVersion = null, SyncData? Data = null);
public record PeerResponse(string Status, string? Message = null, TransportSnapshot? Snapshot = null);

public sealed class PeerSyncException(string message) : InvalidOperationException(message);

public static class PeerWire
{
    public const int MaxBytes = 2 * 1024 * 1024;

    // TLS 中传输：四字节大端长度 + UTF-8 JSON，一次连接一个请求和响应。
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, cancellationToken);
        int size = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (size is <= 0 or > MaxBytes) throw new PeerSyncException("设备同步数据大小无效（上限 2 MB）。");
        var bytes = new byte[size];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return JsonSerializer.Deserialize<T>(bytes, SnapshotTools.Json) ?? throw new PeerSyncException("设备同步消息无效。");
    }

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SnapshotTools.Json);
        if (bytes.Length > MaxBytes) throw new PeerSyncException("设备同步数据超过 2 MB。");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static TransportSnapshot Snapshot(SyncData data)
    {
        var copy = SnapshotTools.ForSync(data);
        var error = SnapshotTools.Validate(copy);
        if (error is not null) throw new PeerSyncException(error);
        return new(SnapshotTools.Hash(copy), copy);
    }

    public static bool SecretEquals(string expected, string? actual) => actual is { Length: <= 4096 }
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(actual)));
}
