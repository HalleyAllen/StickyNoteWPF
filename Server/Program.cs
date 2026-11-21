using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using StickyNote.Sync.Server;
using StickyNoteWPF.Sync;

var builder = WebApplication.CreateBuilder(args);
var serverKey = builder.Configuration["Sync:ServerKey"];
if (string.IsNullOrWhiteSpace(serverKey) || Encoding.UTF8.GetByteCount(serverKey) < 32
    || serverKey.StartsWith("CHANGE_", StringComparison.Ordinal))
    throw new InvalidOperationException("Set Sync__ServerKey to a random secret of at least 32 bytes.");
var keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(serverKey));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.MaxDepth = 96;
    options.SerializerOptions.NumberHandling = SnapshotTools.Json.NumberHandling;
});
builder.Services.AddSingleton<SyncDatabase>();
builder.Services.AddSingleton<IPasswordHasher<string>, PasswordHasher<string>>();
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210000);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
// 初始化数据库及权限错误在启动时暴露。
_ = app.Services.GetRequiredService<SyncDatabase>();
app.UseExceptionHandler(error => error.Run(async context =>
{
    context.Response.StatusCode = 500;
    await context.Response.WriteAsJsonAsync(new ApiError("服务器处理失败，请稍后重试。"));
}));
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var supplied = context.Request.Headers["X-Server-Key"].ToString();
        if (supplied.Length > 4096 || !CryptographicOperations.FixedTimeEquals(
            keyHash, SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new ApiError("服务器连接密钥无效。"));
            return;
        }
        if (!context.Request.Path.StartsWithSegments("/api/auth/login")
            && !context.Request.Path.StartsWithSegments("/api/auth/register"))
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            var token = authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization[7..] : "";
            var userId = context.RequestServices.GetRequiredService<SyncDatabase>().Authenticate(token);
            if (userId is null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new ApiError("登录已失效，请重新登录。"));
                return;
            }
            context.Items["UserId"] = userId;
            context.Items["Token"] = token;
        }
    }
    await next();
});
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/auth/register", (Credentials input, SyncDatabase db, IPasswordHasher<string> hasher, IConfiguration config) =>
{
    if (!config.GetValue("Sync:AllowRegistration", true))
        return Results.Json(new ApiError("服务器已关闭注册。"), statusCode: 403);
    if (!ValidCredentials(input)) return Results.BadRequest(new ApiError("用户名需为 3–40 位字母、数字、下划线或短横线；密码需为 10–128 位。"));
    var user = new SyncDatabase.User(Guid.NewGuid().ToString("N"), input.Username, hasher.HashPassword(input.Username, input.Password));
    return db.CreateUser(user) ? Results.Ok(db.CreateSession(user)) : Results.Conflict(new ApiError("用户名已存在。"));
}).RequireRateLimiting("auth");

// 为不存在的用户也执行密码校验，避免快速返回暴露用户名。
var dummyHash = app.Services.GetRequiredService<IPasswordHasher<string>>().HashPassword("dummy", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
app.MapPost("/api/auth/login", (Credentials input, SyncDatabase db, IPasswordHasher<string> hasher) =>
{
    if (!ValidCredentials(input)) return Results.Json(new ApiError("用户名或密码错误。"), statusCode: 401);
    var user = db.FindUser(input.Username);
    var result = hasher.VerifyHashedPassword(input.Username, user?.PasswordHash ?? dummyHash, input.Password);
    return user is not null && result != PasswordVerificationResult.Failed
        ? Results.Ok(db.CreateSession(user)) : Results.Json(new ApiError("用户名或密码错误。"), statusCode: 401);
}).RequireRateLimiting("auth");
app.MapPost("/api/auth/logout", (HttpContext context, SyncDatabase db) =>
{
    db.Revoke((string)context.Items["Token"]!);
    return Results.NoContent();
});
app.MapGet("/api/sync", (HttpContext context, SyncDatabase db) => Results.Ok(db.Read((string)context.Items["UserId"]!)));
app.MapPut("/api/sync", (UploadSnapshot input, HttpContext context, SyncDatabase db) =>
{
    var error = SnapshotTools.Validate(input.Data);
    if (input.ExpectedRevision < 0 || error is not null) return Results.BadRequest(new ApiError(error ?? "版本号无效。"));
    return db.Write((string)context.Items["UserId"]!, input)
        ? Results.Ok(new { revision = input.ExpectedRevision + 1 })
        : Results.Conflict(new ApiError("服务器数据已被其他设备修改，请重新同步。"));
});
app.Run();

static bool ValidCredentials(Credentials input) => input.Username is not null && input.Password is { Length: >= 10 and <= 128 }
    && Regex.IsMatch(input.Username, "\\A[a-zA-Z0-9_-]{3,40}\\z", RegexOptions.CultureInvariant);
