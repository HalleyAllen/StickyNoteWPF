using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using StickyNoteWPF.Models;
using StickyNoteWPF.Sync;

internal static class PeerTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var gate = new object();
        var data = new SyncData { Notes = { new StickyNoteModel { Text = "另一台电脑的离线内容" } } };
        bool allowWrite = true;
        var host = new PeerSyncHost(IPAddress.Loopback, 0,
            _ => { lock (gate) return Task.FromResult(PeerWire.Snapshot(data)); },
            (incoming, expected, ct) =>
            {
                lock (gate)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!allowWrite) throw new PeerSyncException("接收设备拒绝替换数据。");
                    if (SnapshotTools.Hash(data) != expected) throw new PeerSyncException("设备数据已修改。");
                    data = incoming;
                    return Task.FromResult(PeerWire.Snapshot(data));
                }
            });
        int port = host.Invitation.Port;
        try
        {
            var code = host.Invitation.Encode();
            check(PeerInvitation.Parse(code) == host.Invitation, "设备配对码往返解析");
            check(PeerInvitation.IsLocalAddress(IPAddress.Parse("192.168.1.2"))
                && PeerInvitation.IsLocalAddress(IPAddress.Parse("172.16.1.2"))
                && !PeerInvitation.IsLocalAddress(IPAddress.Parse("172.32.1.2"))
                && !PeerInvitation.IsLocalAddress(IPAddress.Parse("8.8.8.8")), "直连地址限制在私有网络");
            await ExpectFailure(async () =>
            {
                using var invalid = new PeerSyncClient((host.Invitation with { Address = "8.8.8.8" }).Encode());
                await invalid.ReadAsync();
            }, check, "拒绝公网地址配对码");
            using ISyncTransport client = new PeerSyncClient(code);
            var first = await client.ReadAsync();
            check(first.Data.Notes[0].Text == "另一台电脑的离线内容", "无服务器、无账号 TLS 直连读取");
            var outgoing = new SyncData { TaskLists = { new TaskListModel
                { Items = { new TaskItem { Text = "设备任务", SubItems = { new TaskItem { Text = "手机预留子任务" } } } } } } };
            var result = await client.WriteAsync(outgoing, first.Version);
            check(result == SnapshotTools.Hash(outgoing) && (await client.ReadAsync()).Data.TaskLists[0].Items[0].SubItems.Count == 1,
                "设备发送、接收、子任务完整往返");
            await ExpectFailure(() => client.WriteAsync(first.Data, first.Version), check, "设备直连拒绝过期内容摘要");
            lock (gate) data.Notes.Add(new StickyNoteModel { Text = "接收期间本机修改" });
            await ExpectFailure(() => client.WriteAsync(first.Data, result), check, "接收端编辑后阻止覆盖");
            var current = await client.ReadAsync();
            allowWrite = false;
            await ExpectFailure(() => client.WriteAsync(first.Data, current.Version), check, "接收方可以拒绝写入");
            check((await client.ReadAsync()).Version == current.Version, "拒绝接收后数据保持不变");
            allowWrite = true;
            using (var wrongKey = new PeerSyncClient((host.Invitation with { Key = new string('0', 64) }).Encode()))
                await ExpectFailure(() => wrongKey.ReadAsync(), check, "设备直连拒绝错误配对密钥");
            using (var wrongCertificate = new PeerSyncClient((host.Invitation with { Fingerprint = new string('0', 64) }).Encode()))
                await ExpectFailure(() => wrongCertificate.ReadAsync(), check, "设备直连校验证书指纹");
            var concurrent = await Task.WhenAll(TryWrite(), TryWrite());
            check(concurrent.Count(success => success) == 1, "设备直连并发覆盖只有一个成功");

            async Task<bool> TryWrite()
            {
                using var writer = new PeerSyncClient(code);
                try { await writer.WriteAsync(first.Data, current.Version); return true; }
                catch (PeerSyncException) { return false; }
            }
        }
        finally { await host.DisposeAsync(); }
        var released = new TcpListener(IPAddress.Loopback, port);
        released.Start(); released.Stop();
        check(true, "停止设备接收后释放监听端口");

        await using (var mobileStyleHost = new PeerSyncHost(IPAddress.Loopback, 0,
            _ => Task.FromResult(new TransportSnapshot("mobile-version-1", new())),
            (incoming, expected, _) => expected == "mobile-version-1"
                ? Task.FromResult(new TransportSnapshot("mobile-version-2", incoming))
                : throw new PeerSyncException("版本错误")))
        {
            using ISyncTransport mobileStyleClient = new PeerSyncClient(mobileStyleHost.Invitation.Encode());
            var snapshot = await mobileStyleClient.ReadAsync();
            check(snapshot.Version == "mobile-version-1", "设备版本凭据无需重算远端摘要");
            check(await mobileStyleClient.WriteAsync(new(), snapshot.Version) == "mobile-version-2",
                "非 Windows 风格版本可以通过通用协议往返");
        }

        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(prefix, PeerWire.MaxBytes + 1);
        await ExpectFailure(async () => await PeerWire.ReadAsync<PeerRequest>(new MemoryStream(prefix), default),
            check, "设备协议拒绝超大消息帧");
        await using var expiredHost = new PeerSyncHost(IPAddress.Loopback, 0,
            _ => Task.FromResult(PeerWire.Snapshot(new())),
            (_, _, _) => throw new Exception("不应调用"), TimeSpan.FromMilliseconds(200));
        var expiredCode = expiredHost.Invitation.Encode();
        await Task.Delay(350);
        check(!expiredHost.IsActive, "配对窗口到期后自动停止接收");
        await ExpectFailure(async () =>
        {
            using var expired = new PeerSyncClient(expiredCode);
            await expired.ReadAsync();
        }, check, "过期配对码不能继续连接");
    }

    private static async Task ExpectFailure(Func<Task> action, Action<bool, string> check, string description)
    {
        try { await action(); }
        catch (Exception ex) when (ex is InvalidOperationException or AuthenticationException)
        { check(true, description); return; }
        check(false, description);
    }
}
