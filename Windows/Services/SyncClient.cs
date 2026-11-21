using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using StickyNoteWPF.Sync;

namespace StickyNoteWPF.Services;

public sealed class SyncClient : ISyncTransport
{
    private readonly HttpClient _http;
    public static string NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("请输入 HTTPS 服务器地址；本机测试可使用 http://localhost:端口。地址不能包含账号、查询参数或片段。");
        return uri.AbsoluteUri.TrimEnd('/') + "/";
    }

    public SyncClient(string url, string key, string token = "")
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("请输入服务器连接密钥。");
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(NormalizeUrl(url)), Timeout = TimeSpan.FromSeconds(25),
            MaxResponseContentBufferSize = 2 * 1024 * 1024
        };
        _http.DefaultRequestHeaders.Add("X-Server-Key", key);
        if (!string.IsNullOrEmpty(token)) _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<LoginResult> LoginAsync(string username, string password, bool register)
    {
        using var response = await _http.PostAsJsonAsync(register ? "api/auth/register" : "api/auth/login", new Credentials(username, password), SnapshotTools.Json);
        await CheckAsync(response);
        return await response.Content.ReadFromJsonAsync<LoginResult>(SnapshotTools.Json) ?? throw new InvalidOperationException("服务器登录响应无效。");
    }

    public async Task<RemoteSnapshot> DownloadAsync()
    {
        using var response = await _http.GetAsync("api/sync");
        await CheckAsync(response);
        var snapshot = await response.Content.ReadFromJsonAsync<RemoteSnapshot>(SnapshotTools.Json);
        var error = SnapshotTools.Validate(snapshot?.Data);
        if (snapshot is null || snapshot.Revision < 0 || error is not null) throw new InvalidOperationException(error ?? "服务器同步数据无效。");
        return snapshot;
    }

    public async Task<long> UploadAsync(SyncData data, long revision)
    {
        var error = SnapshotTools.Validate(data);
        if (error is not null) throw new InvalidOperationException(error);
        using var response = await _http.PutAsJsonAsync("api/sync", new UploadSnapshot(revision, data), SnapshotTools.Json);
        await CheckAsync(response);
        return checked(revision + 1);
    }

    public async Task LogoutAsync()
    {
        using var response = await _http.PostAsync("api/auth/logout", null);
        await CheckAsync(response);
    }

    public async Task<TransportSnapshot> ReadAsync()
    {
        var snapshot = await DownloadAsync();
        return new(snapshot.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), snapshot.Data);
    }

    public async Task<string> WriteAsync(SyncData data, string expectedVersion)
    {
        if (!long.TryParse(expectedVersion, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var revision))
            throw new InvalidOperationException("同步版本无效。");
        return (await UploadAsync(data, revision)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task CheckAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string? message = null;
        try { message = (await response.Content.ReadFromJsonAsync<ApiError>(SnapshotTools.Json))?.Message; }
        catch (System.Text.Json.JsonException) { }
        throw new InvalidOperationException(message ?? (response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "服务器密钥错误或登录已失效，请重新登录。",
            HttpStatusCode.Conflict => "服务器数据已变化，请重新同步。",
            HttpStatusCode.TooManyRequests => "请求过于频繁，请一分钟后再试。",
            HttpStatusCode.RequestEntityTooLarge => "同步数据超过服务器大小限制（默认 2 MB）。",
            _ => $"服务器返回错误（{(int)response.StatusCode}）。"
        }));
    }

    public void Dispose() => _http.Dispose();
}
