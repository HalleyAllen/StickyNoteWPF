namespace StickyNoteWPF.Sync;

// 版本凭据由连接方式定义：服务器使用递增版本，设备直连使用内容摘要。
public record TransportSnapshot(string Version, SyncData Data);

public interface ISyncTransport : IDisposable
{
    Task<TransportSnapshot> ReadAsync();
    Task<string> WriteAsync(SyncData data, string expectedVersion);
}
