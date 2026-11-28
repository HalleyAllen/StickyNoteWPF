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
  - 标题栏列表按钮支持正文有序 / 无序列表，回车自动继续，空项回车退出
  - 逐张独立设置外观，已创建的便签不受默认样式变更影响
  - 新建便签与任务清单默认采用白字、灰底、85% 背景不透明度，文字保持清晰
- **任务清单**
  - 桌面可勾选清单，实时显示完成进度
  - 「其他（⋯）」菜单可一次性应用一周任务模板，已有任务归入今天，按周一至周日填写子任务，并切换已完成任务的显示
  - 支持新建多张清单独立管理
  - 主页预览独立遵守「显示已完成任务」，首次加载即生效，完成状态变化实时更新，无需先打开清单窗口
- **管理界面**（主窗口）
  - 左侧导航切换「便利贴 / 任务清单 / 归档」
  - 支持关键字搜索、排序（默认 / 标题 A→Z / Z→A）
  - 卡片 / 列表两种视图切换
  - 选中时保留卡片原色；浅色使用阴影，暗色使用中性浅灰光晕；新建、排序的焦点和按压状态不会改变布局尺寸
  - 便利贴和待办支持归档、搜索与恢复；归档后从桌面及日常列表收起，归档待办始终显示已完成项和子任务，恢复后保留原有设置
  - 长列表和任务预览按可见区域加载，背景图按窗口所需尺寸解码，降低内存占用
- **默认外观**（🎨）
  - 在统一设置窗口的「便签外观 / 清单外观」页调整新建项的默认样式
- **全局设置**（⚙）
  - 全局、便签和清单设置均为非模态窗口，打开时可继续编辑便签、勾选待办；外观调整即时生效，重复打开会唤回已有窗口
  - 开机自启、窗口置顶、全局显示/隐藏快捷键（支持 F1、F2 等单键，只切换便签和清单，主页保持原状态）
  - 常规设置可切换深色模式，即时生效并记住选择；便签和清单保留自定义颜色
- **账号与数据同步**
  - 不登录即可离线使用，也可通过局域网设备直连同步
  - 设备直连使用临时配对码和加密连接，无需部署服务器或注册账号
  - 也可登录服务器手动同步便签与任务清单
  - 支持连接自己部署或他人提供的服务器，同时验证服务器密钥和账号
  - 冲突提示、并发版本保护、下载替换前本地备份
  - 设置 →「服务器同步 / 设备直连」；[设备直连说明](Docs/设备同步协议.md)、[服务端说明](Server/README.md)
- **其他**
  - 系统托盘常驻，关闭主窗口不退出
  - 「👁 显示便签与清单」：恢复各窗口原来的透明度和自动隐藏设置
  - 数据自动持久化，损坏文件自动备份防止覆盖

## 运行环境

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（仅开发构建需要；发布为自包含后可免环境运行）

## 归档与恢复

1. 在日常卡片或列表中点击「归档」，对应桌面窗口会关闭，内容保留在归档区。
2. 点击左侧「归档」，在右上角选择「便利贴 / 待办」，可继续搜索、排序和切换视图。
3. 双击归档项只读查看完整内容；归档待办的预览和详情均显示已完成项，详情包含子任务。
4. 点击「恢复」或详情中的「恢复到日常列表」，再从日常列表打开即可继续编辑。

归档不删除内容，也不会修改任务完成状态、样式或原来的「显示已完成任务」设置。归档状态参与本地保存和同步；归档项不会在启动或点击「显示便签与清单」时重新出现在桌面。

## 构建与运行

```bash
# 构建全部项目
dotnet build StickyNoteWPF.slnx -m:1

# 直接运行
dotnet run --project Windows/StickyNoteWPF.csproj

# 运行同步检查
dotnet run --project Tests/StickyNote.Sync.Tests.csproj
```

## 开发验证

```bash
# 跨平台核心回归测试（保存队列、备份、任务通知、快捷键解析、窗口位置和日志）
dotnet test Tests/StickyNoteWPF.Tests.csproj

# Linux 云环境：仅交叉编译 Windows 目标；WPF 界面仍须在 Windows 上运行
dotnet build Windows/StickyNoteWPF.csproj -p:EnableWindowsTargeting=true -r win-x64 --self-contained false

# Windows：实际加载控件，检查模板、透明度、定时器、快捷键冲突和列表虚拟化
dotnet run --project Tests/Windows/StickyNoteWPF.WindowsSmoke.csproj

# Windows：渲染界面并检查主题、选中效果、新建/排序布局、设置页和归档详情
dotnet run --project Tests/VisualPreview/StickyNote.VisualPreview.csproj
```

GitHub Actions 会执行核心测试、同步检查、Windows 构建、界面预览回归、自包含发布和界面冒烟检查。窗口检查包含非模态设置、归档恢复、主页完成项过滤、透明度和自动隐藏。
发布包的界面检查会通过管理器新建一张便签，退出后验证已写入数据文件；应在隔离的 Windows 测试账户中执行。

数据和设置使用原子替换写入，`.bak` 保留上一版；损坏文件会另存为 `.corrupt-*.json`。
日志位于 `%LOCALAPPDATA%\StickyNoteWPF\logs\app.log`，单文件最多 1 MiB，保留 3 份轮转日志。

## 发布

Windows 项目内置发布整理脚本（`Windows/scripts/publish-layout.ps1`），发布后清理历史 `lib\` 与 RID 中间目录，并复制图标资源；需要外置的运行时 DLL 保留在发布根目录：

```bash
dotnet publish Windows/StickyNoteWPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Visual Studio 中的 `FolderProfile` 使用 `win-x86` 自包含单文件发布；也可在仓库根目录执行 `dotnet publish Windows/StickyNoteWPF.csproj -p:PublishProfile=FolderProfile`。整理脚本兼容 Windows PowerShell 5.1，支持相对或绝对发布目录。

更换程序版本前先从托盘退出旧实例，再启动新文件；重复启动会激活当前运行的实例。用户数据不在程序目录中，无需随发布包移动。

## 数据存储

所有数据保存在当前用户的本地应用数据目录下：

```
%LOCALAPPDATA%\StickyNoteWPF\
├── notes.json      # 便利贴与任务清单数据
└── settings.json   # 应用设置（默认外观、全局设置、窗口记忆等）
```

卸载程序或重装系统前如需保留数据，可先备份该目录。

启用服务器同步后，此目录还会包含 `sync.json`（本机连接配置及 Windows 加密的凭据）。设备直连配对信息仅在内存中保存。两种方式下载替换前均会生成 `notes.before-sync.*.json` 本机备份。服务器独立保存每个账号的数据；退出账号保留本地便签，且不会自动切换本地数据集。背景图片文件和设备设置不参与首版同步。手机端后续开发，当前直连界面面向 Windows 电脑；尚无跨公网网络穿透和自动设备发现。
