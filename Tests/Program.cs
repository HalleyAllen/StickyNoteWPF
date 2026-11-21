using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;
using StickyNoteWPF.Sync;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}

Check(SyncDecision.Choose(null, "", "L", "R", 0, false) == SyncDirection.Upload, "首次连接空服务器上传");
Check(SyncDecision.Choose(null, "", "L", "R", 1, true) == SyncDirection.Download, "空本机下载已有云端数据");
Check(SyncDecision.Choose(null, "", "L", "R", 1, false) == SyncDirection.Conflict, "首次两端都有数据要求选择");
Check(SyncDecision.Choose(1, "B", "L", "B", 1, false) == SyncDirection.Upload, "仅本地修改上传");
Check(SyncDecision.Choose(1, "B", "B", "R", 2, false) == SyncDirection.Download, "仅远端修改下载");
Check(SyncDecision.Choose(1, "B", "L", "R", 2, false) == SyncDirection.Conflict, "双端修改保护");
Check(SyncDecision.Choose(2, "B", "B", "R", 1, false) == SyncDirection.Conflict, "服务器版本回退保护");
Check(SyncDecision.Choose(1, "B", "L", "L", 2, false) == SyncDirection.None, "内容相同无需替换");
Check(SyncDecision.Choose(1, "B", "B", "EMPTY", 2, false) == SyncDirection.Download, "远端删除传播");
Check(SyncDecision.Choose(1, "B", "EMPTY", "B", 1, true) == SyncDirection.Upload, "本地删除传播");

var local = new SyncData { Notes = { new StickyNoteModel { Text = "离线记录", IsOpen = true, BackgroundImagePath = @"C:\private\photo.png" } },
    TaskLists = { new TaskListModel { Items = { new TaskItem { Text = "父任务", SubItems = { new TaskItem { Text = "子任务", IsDone = true } } } } } } };
var normalized = SnapshotTools.ForSync(local);
Check(normalized.Notes[0].BackgroundImagePath == "" && !normalized.Notes[0].IsOpen
    && local.Notes[0].IsOpen && local.Notes[0].BackgroundImagePath != "", "同步副本移除设备信息且不修改本地数据");
var hash = SnapshotTools.Hash(local);
local.Notes[0].Left += 50;
local.Notes[0].IsOpen = false;
Check(SnapshotTools.Hash(local) == hash, "窗口位置和开关不会触发冲突");
var utcCopy = SnapshotTools.ForSync(local);
utcCopy.Notes[0].CreatedAt = utcCopy.Notes[0].CreatedAt.ToUniversalTime();
Check(SnapshotTools.Hash(utcCopy) == hash, "创建时间在不同时区表示下摘要一致");
local.TaskLists[0].Items[0].SubItems[0].Text += "修改";
Check(SnapshotTools.Hash(local) != hash, "深层子任务修改触发内容变化");
Check(SnapshotTools.Validate(normalized) is null, "有效便签和多层任务通过验证");
normalized.Notes.Add(normalized.Notes[0]);
Check(SnapshotTools.Validate(normalized) is not null, "拒绝重复标识");
try { SyncClient.NormalizeUrl("http://example.com"); Check(false, "远程明文连接应被拒绝"); }
catch (InvalidOperationException) { Check(true, "拒绝远程明文连接"); }
Check(SyncClient.NormalizeUrl("https://example.com/sync") == "https://example.com/sync/", "支持服务器子路径");

await PeerTests.RunAsync(Check);

var root = Path.GetFullPath("../../../../", AppContext.BaseDirectory);
var configuration = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
var serverDll = Path.Combine(root, "Server", "bin", configuration, "net10.0", "StickyNote.Sync.Server.dll");
var dataPath = Path.Combine(root, "Tests", "TestResults", Guid.NewGuid().ToString("N"));
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();
var url = $"http://127.0.0.1:{port}/";
var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var logs = new ConcurrentQueue<string>();
Process? process = null;
using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(10) };

async Task Start(bool registration = true)
{
    var info = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = Path.Combine(root, "Server"), UseShellExecute = false,
        CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
    };
    info.ArgumentList.Add(serverDll);
    info.Environment["ASPNETCORE_URLS"] = url.TrimEnd('/');
    info.Environment["Sync__ServerKey"] = key;
    info.Environment["Sync__DataDirectory"] = dataPath;
    info.Environment["Sync__AllowRegistration"] = registration.ToString();
    info.Environment["Logging__LogLevel__Default"] = "Warning";
    process = Process.Start(info)!;
    process.OutputDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); };
    process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); };
    process.BeginOutputReadLine(); process.BeginErrorReadLine();
    for (var i = 0; i < 80; i++)
    {
        if (process.HasExited) throw new Exception("服务端启动失败：" + string.Join('\n', logs));
        try { using var response = await http.GetAsync("health"); if (response.IsSuccessStatusCode) return; }
        catch (HttpRequestException) { }
        await Task.Delay(100);
    }
    throw new Exception("服务端启动超时。");
}

async Task Stop()
{
    if (process is null) return;
    if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
    process.Dispose(); process = null;
}

async Task<HttpResponseMessage> Request(HttpMethod method, string path, object? body = null, string? token = null, string? serverKey = null)
{
    using var request = new HttpRequestMessage(method, path);
    request.Headers.Add("X-Server-Key", serverKey ?? key);
    if (token is not null) request.Headers.Authorization = new("Bearer", token);
    if (body is not null) request.Content = JsonContent.Create(body, options: SnapshotTools.Json);
    return await http.SendAsync(request);
}

try
{
    await Start();
    using (var r = await Request(HttpMethod.Post, "api/auth/register", new Credentials("alice", "correct-horse-123"), serverKey: "wrong"))
        Check(r.StatusCode == HttpStatusCode.Unauthorized, "错误服务器密钥不能注册");
    using var aliceClient = new SyncClient(url, key);
    var alice = await aliceClient.LoginAsync("alice", "correct-horse-123", true);
    using var bobClient = new SyncClient(url, key);
    var bob = await bobClient.LoginAsync("bob", "another-horse-123", true);
    Check(alice.UserId != bob.UserId, "不同账号具有独立身份");
    using (var r = await Request(HttpMethod.Post, "api/auth/register", new Credentials("ALICE", "correct-horse-123")))
        Check(r.StatusCode == HttpStatusCode.Conflict, "注册用户名大小写不重复");
    using (var r = await Request(HttpMethod.Post, "api/auth/login", new Credentials("alice", "wrong-password")))
        Check(r.StatusCode == HttpStatusCode.Unauthorized, "错误密码不能登录");
    using (var r = await Request(HttpMethod.Get, "api/sync"))
        Check(r.StatusCode == HttpStatusCode.Unauthorized, "只有服务器密钥不能访问用户数据");
    using var clientA = new SyncClient(url, key, alice.Token);
    using var clientB = new SyncClient(url, key, bob.Token);
    var empty = await clientA.DownloadAsync();
    Check(empty.Revision == 0 && SnapshotTools.IsEmpty(empty.Data), "新账号初始为空");
    var payload = SnapshotTools.ForSync(local);
    Check(await clientA.UploadAsync(payload, 0) == 1, "客户端上传便签与子任务");
    var downloaded = await clientA.DownloadAsync();
    Check(downloaded.Revision == 1 && SnapshotTools.Hash(downloaded.Data) == SnapshotTools.Hash(payload), "客户端完整往返内容一致");
    Check(SnapshotTools.IsEmpty((await clientB.DownloadAsync()).Data), "其他用户不能读取该快照");
    using (var r = await Request(HttpMethod.Put, "api/sync", new UploadSnapshot(0, new()), alice.Token))
        Check(r.StatusCode == HttpStatusCode.Conflict, "拒绝过期版本上传");
    var writes = await Task.WhenAll(
        Request(HttpMethod.Put, "api/sync", new UploadSnapshot(1, payload), alice.Token),
        Request(HttpMethod.Put, "api/sync", new UploadSnapshot(1, payload), alice.Token));
    Check(writes.Count(r => r.StatusCode == HttpStatusCode.OK) == 1 && writes.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 1,
        "并发写入仅一个成功");
    foreach (var r in writes) r.Dispose();
    var malicious = SnapshotTools.ForSync(payload);
    malicious.Notes[0].BackgroundImagePath = @"C:\sensitive.txt";
    using (var r = await Request(HttpMethod.Put, "api/sync", new UploadSnapshot(2, malicious), alice.Token))
        Check(r.StatusCode == HttpStatusCode.BadRequest, "拒绝远端注入本地文件路径");
    Check((await clientA.DownloadAsync()).Revision == 2, "无效写入不改变版本");
    await Stop();
    // 检查数据库没有保存原始密码或原始会话 token。
    using (var db = new SqliteConnection($"Data Source={Path.Combine(dataPath, "sync.db")}"))
    {
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT PasswordHash FROM Users WHERE Username='alice'";
        Check((string)cmd.ExecuteScalar()! != "correct-horse-123", "数据库使用密码哈希");
        cmd.CommandText = "SELECT COUNT(*) FROM Sessions WHERE TokenHash=$token";
        cmd.Parameters.AddWithValue("$token", alice.Token);
        Check((long)cmd.ExecuteScalar()! == 0, "数据库不保存原始会话密钥");
    }
    await Start(false);
    Check((await clientA.DownloadAsync()).Revision == 2, "服务器重启后数据与会话仍有效");
    using (var r = await Request(HttpMethod.Post, "api/auth/register", new Credentials("charlie", "third-horse-123")))
        Check(r.StatusCode == HttpStatusCode.Forbidden, "管理员可以关闭注册");
    var relogin = await aliceClient.LoginAsync("ALICE", "correct-horse-123", false);
    Check(relogin.UserId == alice.UserId, "重启后仍可登录原账号");
    Check(await clientA.UploadAsync(new(), 2) == 3 && SnapshotTools.IsEmpty((await clientA.DownloadAsync()).Data), "删除全部内容可以同步");
    await clientA.LogoutAsync();
    using (var r = await Request(HttpMethod.Get, "api/sync", token: alice.Token))
        Check(r.StatusCode == HttpStatusCode.Unauthorized, "退出后会话立即失效");
    using (var r = await Request(HttpMethod.Get, "api/sync", token: bob.Token, serverKey: "wrong"))
        Check(r.StatusCode == HttpStatusCode.Unauthorized, "有效登录仍需正确服务器密钥");
    // 关闭注册同样消耗认证限流额度；使用同一 IP 验证上限。
    HttpStatusCode last = 0;
    for (var i = 0; i < 12; i++)
    {
        using var r = await Request(HttpMethod.Post, "api/auth/login", new Credentials("invalid", "invalid-password"));
        last = r.StatusCode;
        if (last == HttpStatusCode.TooManyRequests) break;
    }
    Check(last == HttpStatusCode.TooManyRequests, "登录请求限流");
    Console.WriteLine($"全部 {checks} 项检查通过。");
}
finally { await Stop(); }
