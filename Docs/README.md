# 项目文档

- [设备同步协议](设备同步协议.md)：设备直连操作、传输协议及手机端接入约定。
- [功能介绍讲解稿](功能介绍讲解稿.md)：产品功能演示与介绍。
- [热插拔方案设计](热插拔方案设计.md)：尚未实施的模块化方案。
- [服务端部署与接口](../Server/README.md)：服务器运行、Docker 部署与账号同步。

Windows 界面预览可运行 `dotnet run --project Tests/VisualPreview/StickyNote.VisualPreview.csproj`。它使用独立样例数据，在屏幕外渲染主界面、桌面便签、设置和同步窗口，PNG 输出到 `Tests/TestResults/ui-preview/`。预览会屏蔽真实应用启动及保存流程，不连接同步服务。正式启动客户端仍使用 `dotnet run --project Windows/StickyNoteWPF.csproj`。

统一设置窗口提供「常规、便签外观、清单外观、服务器同步、设备直连」五个页签，子页面位于 `Windows/SettingsPages/`。默认外观入口直接定位对应页签。页签内容按需创建并保留输入；关闭设置时停止设备接收，实际同步中的数据冲突确认仍会提示。

在「常规 → 深色模式」切换界面主题，无需重启。主题保存在本机 `settings.json` 的 `DarkMode` 字段中，旧配置默认使用浅色。管理界面、设置及同步页面跟随主题；便签、任务清单的自定义颜色保持原样。调色板与公共样式位于 `Windows/Themes/`。

便签标题栏的列表图标可将光标所在段落或选中的多行改为有序列表（`1. `）或无序列表（`• `）。再次选择同一种列表或选择「取消列表」会移除标记。回车继续下一项，在有序列表中间插入时顺延后面的编号；空列表项再次回车退出，Shift+Enter 保留普通换行。快捷键为 Ctrl+Shift+7 / Ctrl+Shift+8。列表标记直接保存在正文中，兼容现有存储、搜索和同步；支持撤销/重做，锁定便签时禁止修改。

任务清单标题栏的「其他（⋯）」菜单提供「应用一周任务模板」：每张清单只能应用一次，已有未分组任务连同子任务及完成状态归入今天（按电脑本地日期）的分组；空白的其他天提供一条可编辑任务。顶部加号新增的任务也进入今天，各天旁边的加号可添加指定日期的子任务。模板通过稳定分组 ID 保存和同步，重开清单或修改星期标题不会重复生成；旧版完整的星期分组会自动识别。菜单中的「显示已完成任务」与齿轮设置共用每张清单自己的开关，对根任务与子任务生效；隐藏不会删除任务。锁定后不能应用模板。

目录整理后，Windows 桌面应用代码位于 `Windows/`，共用模型位于 `Shared/Models/`。早期设计稿中的 `App.xaml.cs`、窗口和 `Services/` 路径对应 `Windows/` 内的相对路径；其中 `Models/` 对应 `Shared/Models/`。

## 内存优化验证（2026-11-24）

Windows 客户端已将卡片、列表行、任务清单和卡片内的任务预览改为按可见区域创建控件。管理卡片尺寸不变，滚动、标题编辑和排序仍保留原有行为。正文预览最多格式化 512 个字符，完整正文仍保存在模型和桌面编辑器中。

背景图片按窗口尺寸和 DPI 解码，放大窗口后延迟 200 毫秒更新分辨率，避免拖动时连续解码。解码结果冻结后使用有容量限制的弱引用缓存，窗口关闭时释放背景和计时器；居中、平铺模式保留原始像素尺寸。滚动条模板改为共享的编译 XAML，刷新样式不再解析模板或重建编辑器。JSON 存储改为流式读写，继续使用临时文件原子替换、旧格式兼容和损坏文件保护。

同一台 Windows 机器、.NET 10 Debug、相同测试器，在独立进程中得到以下结果。图片样例包含 3 张 3000×2000 PNG，分别用于 3 个便签和 3 个任务窗口；管理界面样例包含 600 张便签，每张正文 4000 字。数据均为独立样例，不读取或写入用户便签。

| 场景 | 优化前专用内存 | 优化后专用内存 | 降幅 |
| --- | ---: | ---: | ---: |
| 打开 6 个图片窗口 | 426.6 MiB | 241.6 MiB | 43.4% |
| 图片窗口重复刷新外观后 | 454.6 MiB | 325.8 MiB | 28.3% |
| 600 张便签，卡片视图 | 317.1 MiB | 223.8 MiB | 29.4% |
| 600 张便签，列表视图 | 354.0 MiB | 228.7 MiB | 35.4% |

相应工作集分别由 501.4→314.9、529.6→399.0、386.2→280.9、423.0→287.1 MiB。管理界面生成的卡片/行容器由 600/600 降到 8/6；72 次外观刷新产生的托管分配约由 30.6 MiB 降到 0.34 MiB。测试在两版中均使用相同的等待和 GC 取样步骤，产品代码未加入强制 GC 或工作集裁剪。以上为样例结果，实际占用随窗口数量、图片模式、分辨率和 DPI 变化。

验证包含大文本和嵌套任务的存储往返、带 BOM 的旧数组格式、损坏 JSON、保存失败不覆盖原文件、背景图片替换/损坏/放大、关闭窗口及图片可回收、长清单新建聚焦与完成项筛选，以及深浅色、排序和卡片选中回归。原始 JSON 输出到 `Tests/TestResults/memory-checks/`，该目录不提交。

```powershell
# 完整隔离检查；仅使用样例数据
dotnet run --project Tests/VisualPreview/StickyNote.VisualPreview.csproj -- --memory current --verify
# 分场景测量时分别启动进程，排除上一场景的影响
dotnet run --project Tests/VisualPreview/StickyNote.VisualPreview.csproj -- --memory images --scenario images
dotnet run --project Tests/VisualPreview/StickyNote.VisualPreview.csproj -- --memory manager --scenario manager
```

参考实现依据：[WPF 控件虚拟化](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls)、[按像素尺寸解码图片](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.bitmapimage.decodepixelwidth?view=windowsdesktop-10.0)。
