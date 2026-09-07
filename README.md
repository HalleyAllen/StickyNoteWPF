# 🗒️ StickyNoteWPF

桌面便利贴与任务清单工具，基于 **.NET 10 + WPF**。支持透明置顶便签、可勾选任务清单、集中管理界面与全局快捷键。

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
- **其他**
  - 系统托盘常驻，关闭主窗口不退出
  - 「👁 全部显示」：一键停用所有便签的隐藏与透明效果
  - 数据自动持久化，损坏文件自动备份防止覆盖

## 运行环境

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（仅开发构建需要；发布为自包含后可免环境运行）

## 构建与运行

```bash
# 构建
dotnet build StickyNoteWPF.csproj

# 直接运行
dotnet run --project StickyNoteWPF.csproj
```

## 发布

项目内置发布整理脚本（`publish-layout.ps1`），发布后自动将运行时 DLL 收进 `lib\` 并清理中间产物，保持输出目录整洁：

```bash
dotnet publish StickyNoteWPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 数据存储

所有数据保存在当前用户的本地应用数据目录下：

```
%LOCALAPPDATA%\StickyNoteWPF\
├── notes.json      # 便利贴与任务清单数据
└── settings.json   # 应用设置（默认外观、全局设置、窗口记忆等）
```

卸载程序或重装系统前如需保留数据，可先备份该目录。
