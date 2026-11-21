# StickyNote 同步服务器

服务器是可选的同步方式。PC 也可免账号进行局域网设备直接同步，操作及协议见 [设备同步说明](../Docs/设备同步协议.md)。本文件中的登录、注册和服务器密钥要求仅适用于服务器模式。

独立的 ASP.NET Core / .NET 10 服务，SQLite 保存账号、登录会话和每个账号的最新数据快照。支持 Windows、Linux 和 Docker。PC 客户端不需要登录即可离线使用；联网同步需要同时提供服务器连接密钥和账号登录凭据。

## 本机运行

需要 .NET 10 SDK。在项目根目录的 PowerShell 中执行：

```powershell
$env:Sync__ServerKey = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
# 将上面的密钥安全保存，客户端连接时需要填写；不要每次重启都重新生成。
$env:Sync__DataDirectory = Join-Path $PWD 'Server/data'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5080'
dotnet run --project Server/StickyNote.Sync.Server.csproj
```

客户端填写 `http://localhost:5080` 和相同的密钥，注册或登录后点击“立即同步”。登录与注册本身不会上传本地数据。用户名为 3–40 位英文字母、数字、下划线或短横线，不区分大小写；密码为 10–128 位。

## Docker 部署

在服务器上安装 Docker 与 Compose，然后在仓库中执行：

```sh
cd Server
cp .env.example .env
# 使用 openssl rand -hex 32 生成密钥，写入 .env 的 SYNC_SERVER_KEY。
docker compose up -d --build
```

Compose 将服务绑定到宿主机 `127.0.0.1:5080`，数据存入命名卷 `sync-data`。不使用示例占位密钥，服务会拒绝启动。`.env` 不应提交到版本库。

远程访问需要域名和 HTTPS 反向代理。例如，宿主机安装 Caddy 后配置：

```caddyfile
sync.example.com {
    reverse_proxy 127.0.0.1:5080
}
```

将域名解析到服务器并允许 80/443 端口，PC 客户端填写 `https://sync.example.com`。也可以使用已有 Nginx 或其他 HTTPS 入口。客户端只对 localhost/回环地址允许 HTTP，不会忽略证书错误，也不会跟随重定向转发连接密钥。

服务可供自己或他人连接：提供服务器地址与连接密钥，用户在此服务器上注册自己的账号。不同服务器的账号独立；更换服务器需要重新注册或使用目标服务器上的账号。

## 配置

| 环境变量 | 说明 | 默认值 |
| --- | --- | --- |
| `Sync__ServerKey` | 至少 32 字节的随机连接密钥，必填 | 无 |
| `Sync__DataDirectory` | SQLite 数据目录 | 服务内容根目录下的 `data` |
| `Sync__AllowRegistration` | 是否允许新注册；关闭后已有账号仍可登录 | `true` |
| `ASPNETCORE_URLS` | 直接运行时的监听地址 | ASP.NET Core 默认值 |

Docker 中通过 `.env` 的 `ALLOW_REGISTRATION=false` 关闭注册，然后执行 `docker compose up -d`。更换连接密钥后重启，所有客户端都需要更新密钥。公开 `/health` 只返回存活状态；全部 `/api` 接口均要求密钥。

当前登录/注册共用每个直连来源 IP 每分钟 10 次的限流。放在反向代理后，默认按代理 IP 计数；首版不信任外部伪造的转发头。多人部署可在可信的 HTTPS 代理上增加按真实来源的限流策略，并按实际规模调整应用额度。

## 同步规则

- 同步整个账号的便利贴、任务清单、多层子任务、完成状态与每张卡片的样式。
- 窗口位置、尺寸、打开状态、全局设置、快捷键、服务器连接配置与背景图片文件不上传。已有卡片下载后保留本机位置和背景图片，新下载的卡片可从管理界面双击打开。
- 新账号服务器为空时上传本地数据；首次连接已有数据的账号时，本地为空则下载，两边都有内容则让用户选择整份数据。
- 后续只有本地改动则上传，只有远端改动则下载；两端都改动则提示用户选择本机或服务器版本，或取消。首版不逐条合并。
- 每次上传携带预期版本号，SQLite 用条件更新保证并发上传只有一个成功。收到冲突后必须重新同步，不能盲目覆盖。
- 下载覆盖前，在本机 `%LOCALAPPDATA%\StickyNoteWPF\` 下生成 `notes.before-sync.*.json` 备份；写入失败时不替换内存数据。同步等待期间本机内容有改动也会取消替换。
- 删除通过完整快照传播；选择某一端时，另一端独有的记录也会被该快照替换。冲突窗口会明确提示。
- 请求体默认限制 2 MiB；每账号最多 2000 张卡片、10000 个任务、16 层子任务。SQLite 中只保留最新快照，不提供服务器历史恢复。

## 账号与数据保护

密码使用 ASP.NET Core Identity 的带盐密码哈希；会话使用 256 位随机 token，数据库只保存 token 的 SHA-256 摘要，30 天到期。退出登录会撤销当前会话；离线退出只能清除客户端登录，远端会话等待到期。

PC 端服务器密钥和会话 token 使用 Windows DPAPI 当前用户保护，保存在 `%LOCALAPPDATA%\StickyNoteWPF\sync.json`，不保存账号密码。请勿把这个文件复制到其他用户或设备上作为登录迁移。

这是服务器可读的同步存储，未实现端到端加密。服务器运营者可以读取所托管的便签和清单，所以请连接信任的服务。首版提供注册、登录、退出；尚无邮箱、找回密码、账号删除或管理后台。

## 备份、恢复与升级

SQLite 使用 WAL。直接复制在线运行的 `sync.db` 可能遗漏尚未合并的事务，建议停止服务后备份整个数据目录或卷，再启动。Docker 不要使用 `docker compose down -v`，该命令会删除数据卷。

升级前备份数据，再执行 `docker compose up -d --build`。迁移到另一台服务器时复制完整数据目录并保留连接密钥；更换域名后客户端需重新登录，首次同步可能要求重新选择版本。

## 接口

所有 `/api` 请求带 `X-Server-Key`。同步及退出另需 `Authorization: Bearer <token>`。JSON 字段使用 camelCase。

| 方法与路径 | 请求 | 响应 |
| --- | --- | --- |
| `GET /health` | 无 | 存活状态 |
| `POST /api/auth/register` | `{username,password}` | `{userId,username,token,expiresAt}` |
| `POST /api/auth/login` | `{username,password}` | 同上 |
| `POST /api/auth/logout` | 无 | 204 |
| `GET /api/sync` | 无 | `{revision,data:{notes,taskLists}}` |
| `PUT /api/sync` | `{expectedRevision,data:{notes,taskLists}}` | `{revision}`；版本冲突为 409 |

## 验证

在仓库根目录执行：

```sh
dotnet build StickyNoteWPF.slnx -m:1
dotnet run --project Tests/StickyNote.Sync.Tests.csproj --no-build
```

测试启动临时 localhost 服务器，使用随机密钥、独立账号和 `Tests/TestResults` 下的临时 SQLite 数据库，不读写日常使用的便签。覆盖客户端同步往返、账号隔离、并发冲突、注销、重启持久化与认证限流。

实现参考：[ASP.NET Core Minimal API 安全](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/security?view=aspnetcore-10.0)、[ASP.NET Core 限流](https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)、[Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.13)。
