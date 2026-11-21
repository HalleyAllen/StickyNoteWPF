# 🗒️ StickyNoteWPF

桌面便利贴与任务清单工具，基于 **.NET 10 + WPF**。支持透明置顶便签、可勾选任务清单、集中管理界面与全局快捷键。

## 项目结构

```text
StickyNoteWPF/
├── Windows/               # Windows 桌面客户端（Win10 / Win11）
│   ├── StickyNoteWPF.csproj
│   ├── App.xaml           # 应用入口及窗口代码
│   ├── Services/          # 客户端存储、同步、托盘与快捷键
│   ├── Converters/        # WPF 绑定转换器
│   ├── Resources/         # 图标资源
│   ├── Properties/        # 发布配置
│   └── scripts/           # 发布整理脚本
├── Server/                # ASP.NET Core 同步服务器及 Docker 配置
├── Shared/                # 跨端数据模型、同步协议及直连实现
│   └── Models/            # 便签、清单和任务模型
├── Tests/                 # 同步自动检查；产物在 TestResults/ 下
├── Docs/                  # 使用说明、演示稿及方案设计
├── StickyNoteWPF.slnx     # 完整解决方案
└── README.md
```

打开根目录解决方案可同时查看各端项目。Windows 客户端与服务端各自生成 `bin/`、`obj/`，运行数据仍按下文所述位置保存。

## 功能特性

- **便利贴**
  - 无边框半透明便签，支持置顶、自定义颜色与字体大小
  - 逐张独立设置外观，已创建的便签不受默认样式变更影响
- **任务清单**
  - 桌面可勾选清单，实时显示完成进度
  - 支持新建多张清单独立管理
- **管理界面**（主窗口）
  - 左侧导航切换「便利贴 / 任务清单」
  - 支持关键字搜索、排序（默认 / 标题 A→Z / Z→A）
  - 卡片 / 列表两种视图切换
- **默认外观**（🎨）
  - 独立窗口设置新建便利贴与任务清单的默认样式
- **全局设置**（⚙）
  - 开机自启、窗口置顶、全局显示/隐藏快捷键
- **账号与数据同步**
  - 不登录即可离线使用，也可通过局域网设备直连同步
  - 设备直连使用临时配对码和加密连接，无需部署服务器或注册账号
  - 也可登录服务器手动同步便签与任务清单
  - 支持连接自己部署或他人提供的服务器，同时验证服务器密钥和账号
  - 冲突提示、并发版本保护、下载替换前本地备份
  - 设置 →「设备、账号与数据同步」；[设备直连说明](Docs/设备同步协议.md)、[服务端说明](Server/README.md)
- **其他**
  - 系统托盘常驻，关闭主窗口不退出
  - 「👁 全部显示」：一键停用所有便签的隐藏与透明效果
  - 数据自动持久化，损坏文件自动备份防止覆盖

## 运行环境

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（仅开发构建需要；发布为自包含后可免环境运行）

## 构建与运行

```bash
# 构建全部项目
dotnet build StickyNoteWPF.slnx -m:1

# 直接运行
dotnet run --project Windows/StickyNoteWPF.csproj

# 运行同步检查
dotnet run --project Tests/StickyNote.Sync.Tests.csproj
```

## 发布

Windows 项目内置发布整理脚本（`Windows/scripts/publish-layout.ps1`），发布后清理历史 `lib\` 与 RID 中间目录，并复制图标资源；需要外置的运行时 DLL 保留在发布根目录：

```bash
dotnet publish Windows/StickyNoteWPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 数据存储

所有数据保存在当前用户的本地应用数据目录下：

```
%LOCALAPPDATA%\StickyNoteWPF\
├── notes.json      # 便利贴与任务清单数据
└── settings.json   # 应用设置（默认外观、全局设置、窗口记忆等）
```

卸载程序或重装系统前如需保留数据，可先备份该目录。

启用服务器同步后，此目录还会包含 `sync.json`（本机连接配置及 Windows 加密的凭据）。设备直连配对信息仅在内存中保存。两种方式下载替换前均会生成 `notes.before-sync.*.json` 本机备份。服务器独立保存每个账号的数据；退出账号保留本地便签，且不会自动切换本地数据集。背景图片文件和设备设置不参与首版同步。手机端后续开发，当前直连界面面向 Windows 电脑；尚无跨公网网络穿透和自动设备发现。
