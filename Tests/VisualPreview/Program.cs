using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StickyNoteWPF;
using StickyNoteWPF.Models;

internal static class Program
{
    private static readonly string Output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../TestResults/ui-preview"));

    [STAThread]
    private static void Main(string[] args)
    {
        using var bindingChecks = new BindingChecks();
        // 不执行 App.OnStartup，不读取或保存日常便签，也不创建托盘、注册热键。
        var app = new PreviewApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
            new Uri("/StickyNote;component/Themes/Controls.xaml", UriKind.Relative)));
        BindingChecks.CheckAutomationNames();
        typeof(App).GetField("_applyingSync", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, true);
        if (args.FirstOrDefault() == "--memory")
        {
            var scenarioIndex = Array.IndexOf(args, "--scenario");
            MemoryChecks.Run(args.Skip(1).FirstOrDefault() ?? "memory", args.Contains("--verify"),
                scenarioIndex >= 0 ? args[scenarioIndex + 1] : "all");
            bindingChecks.AssertClean();
            return;
        }
        var notes = new List<StickyNoteModel>
        {
            new() { Title = "项目灵感", Text = "记下突然想到的好点子。\n先把重要的事情留在桌面上。", Color = "#FFE7C765", FontSize = 14 },
            new() { Title = "会议记录", Text = "本周重点：整理界面展示。\n保留熟悉的操作方式。", Color = "#FF83BEAF" },
            new() { Title = "周末备忘", Text = "挑一本想读的书。\n给自己留一些空闲时间。", Color = "#FF29334A" }
        };
        var lists = new List<TaskListModel> { new() { Title = "本周安排", Color = "#FFB8D9CF", Items = { new TaskItem { Text = "整理主窗口展示", IsDone = true }, new TaskItem { Text = "检查任务清单样式", SubItems = { new TaskItem { Text = "确认子任务缩进" } } }, new TaskItem { Text = "保留现有操作方式" } } } };
        lists.Add(new TaskListModel { Title = "采购清单", Color = "#FFE7D2B7", Items = { new TaskItem { Text = "补充常用物品" } } });
        typeof(App).GetField("_notes", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, new System.Collections.ObjectModel.ObservableCollection<StickyNoteModel>(notes));
        typeof(App).GetField("_taskLists", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, new System.Collections.ObjectModel.ObservableCollection<TaskListModel>(lists));
        Directory.CreateDirectory(Output);
        var manager = new MainWindow();
        PlaceOffscreen(manager);
        try
        {
            manager.Show();
            Render(manager, "manager-cards");
            CheckSortLayout(manager);
            CheckCreateButtonLayout(manager);
            var noteList = (ListBox)manager.FindName("NoteList");
            var originalCards = Enumerable.Range(0, notes.Count).Select(i => CardSnapshot(noteList, i)).ToArray();
            noteList.SelectedIndex = 0;
            Render(manager, "selection-yellow");
            CheckSelection(noteList, 0, originalCards[0]);
            noteList.SelectedIndex = 1;
            Render(manager, "selection-green");
            CheckSelection(noteList, 1, originalCards[1]);
            noteList.SelectedIndex = 2;
            Render(manager, "selection-dark");
            CheckSelection(noteList, 2, originalCards[2]);
            Console.WriteLine("PASS: selection adds a shadow without changing card colors or dimensions.");
            CheckTitleSelection(noteList);
            Click(manager, "RowViewButton"); Render(manager, "manager-rows");
            CheckTitleSelection(noteList);
            Click(manager, "NavTasks"); Render(manager, "manager-tasks");
            var taskList = (ListBox)manager.FindName("TaskListBox");
            CheckTitleSelection(taskList);
            Click(manager, "RowViewButton"); Render(manager, "task-rows");
            CheckTitleSelection(taskList);
            Console.WriteLine("PASS: title clicks and keyboard focus select one card in both views and categories.");
            manager.Width = manager.MinWidth; manager.Height = manager.MinHeight;
            Render(manager, "manager-small");
            var search = (TextBox)manager.FindName("SearchBox");
            search.Text = "不会匹配任何内容";
            Render(manager, "manager-empty");
            search.Clear();
            RenderWindow(new StickyNoteWindow(notes[0]) { Width = 300, Height = 250 }, "desktop-note");
            RenderWindow(new TaskListWindow(lists[0]), "desktop-tasks");
            CheckDefaultAppearance();
            RenderWindow(new StickyNoteWindow(new StickyNoteModel { Title = "默认便签", Text = "白色文字，灰色背景。\n背景略带透明，文字保持清晰。", Color = App.Current.Settings.DefaultColor, TextColor = App.Current.Settings.NoteTextColor, Opacity = App.Current.Settings.WindowOpacity }) { Width = 300, Height = 250 }, "gray-default-note");
            RenderWindow(new TaskListWindow(new TaskListModel { Title = "默认任务清单", Color = StickyNoteWPF.Services.AppSettings.TaskListDefaultColorHex, TextColor = StickyNoteWPF.Services.AppSettings.TaskListDefaultTextColorHex, Opacity = StickyNoteWPF.Services.AppSettings.TaskListDefaultOpacity, Items = { new TaskItem { Text = "整理今天的安排" }, new TaskItem { Text = "完成一件重要的事" } } }), "gray-default-tasks");
            NoteListChecks.Run(Render);
            WeeklyTemplateChecks.Run(Render);
            HotKeyChecks.Run(manager);
            Click(manager, "NavNotes");
            app.SetArchived(notes[0], true);
            Click(manager, "NavArchive");
            Render(manager, "archive-notes");
            RenderWindow(new ArchiveViewerWindow(notes[0], () => { }), "archive-note-details");
            var originalShowCompleted = lists[0].ShowCompleted;
            lists[0].ShowCompleted = false;
            app.SetArchived(lists[0], true);
            ((ComboBox)manager.FindName("ArchiveKindBox")).SelectedIndex = 1;
            Render(manager, "archive-tasks");
            RenderWindow(new ArchiveViewerWindow(lists[0], () => { }), "archive-task-details");
            app.SetArchived(notes[0], false);
            app.SetArchived(lists[0], false);
            lists[0].ShowCompleted = originalShowCompleted;
            Click(manager, "NavNotes");
            var settings = new SettingsWindow(app.Settings);
            PlaceOffscreen(settings);
            settings.Show();
            try
            {
                Render(settings, "settings");
                settings.SelectSection(SettingsSection.NoteAppearance);
                Render(settings, "default-appearance");
                settings.SelectSection(SettingsSection.TaskAppearance);
                Render(settings, "task-appearance");
                settings.SelectSection(SettingsSection.ServerSync);
                var sync = (SyncPage)((ContentControl)settings.FindName("ServerSyncHost")).Content;
                ((TextBox)sync.FindName("ServerBox")).Text = "https://sync.example.com";
                ((TextBox)sync.FindName("UsernameBox")).Text = "";
                ((PasswordBox)sync.FindName("KeyBox")).Clear();
                ((TextBlock)sync.FindName("StatusText")).Text = "未登录。本地数据可继续使用。";
                Render(settings, "sync");
                settings.SelectSection(SettingsSection.PeerSync);
                var peer = (PeerSyncPage)((ContentControl)settings.FindName("PeerSyncHost")).Content;
                ((ComboBox)peer.FindName("AddressBox")).ItemsSource = new[] { "192.168.1.10" };
                ((ComboBox)peer.FindName("AddressBox")).SelectedIndex = 0;
                Render(settings, "peer-sync");
                settings.Width = settings.MinWidth;
                settings.Height = settings.MinHeight;
                Render(settings, "settings-small");
                settings.Width = 760;
                settings.Height = 700;
                settings.SelectSection(SettingsSection.ServerSync);
                if (!ReferenceEquals(sync, ((ContentControl)settings.FindName("ServerSyncHost")).Content)
                    || ((TextBox)sync.FindName("ServerBox")).Text != "https://sync.example.com")
                    throw new InvalidOperationException("切换页签不能丢失输入或重建同步页。");
                if (app.Windows.Count != 2) throw new InvalidOperationException("设置页签不应创建额外窗口。");
                Console.WriteLine("PASS: settings tabs reuse pages and keep a single window.");
                CheckThemes(manager, settings, sync, peer);
                // 同步中不能关闭宿主；切页也不能销毁后台操作所在控件。
                var busyField = typeof(SyncPage).GetField("_busy", BindingFlags.Instance | BindingFlags.NonPublic)!;
                busyField.SetValue(sync, true);
                settings.Close();
                if (!settings.IsVisible) throw new InvalidOperationException("同步处理中设置窗口不应关闭。");
                busyField.SetValue(sync, false);
                settings.SelectSection(SettingsSection.PeerSync);
                ((ComboBox)peer.FindName("AddressBox")).ItemsSource = new[] { "127.0.0.1" };
                ((ComboBox)peer.FindName("AddressBox")).SelectedIndex = 0;
                var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                listener.Start();
                var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                ((TextBox)peer.FindName("PortBox")).Text = port.ToString();
                ((Button)peer.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var host = (StickyNoteWPF.Sync.PeerSyncHost?)typeof(PeerSyncPage).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peer);
                if (host?.IsActive != true) throw new InvalidOperationException("本机设备接收未启动。");
                settings.SelectSection(SettingsSection.General);
                if (!host.IsActive) throw new InvalidOperationException("切换页面不应停止设备接收。");
                settings.Close();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (settings.IsVisible && DateTime.UtcNow < deadline)
                {
                    settings.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Thread.Sleep(10);
                }
                if (settings.IsVisible) throw new InvalidOperationException("设备接收停止后设置窗口未关闭。");
                var released = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                released.Start(); released.Stop();
                Console.WriteLine("PASS: busy close protection, page switching and receiver cleanup.");
            }
            finally
            {
                settings.Close();
                settings.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }
            Console.WriteLine("Rendered preview windows: " + Output);
        }
        finally { manager.Close(); }
        bindingChecks.AssertClean();
    }

    private static void Click(Window window, string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void CheckDefaultAppearance()
    {
        var normalize = typeof(StickyNoteWPF.Services.AppSettings).GetMethod("EnsureTaskListDefaults", BindingFlags.NonPublic | BindingFlags.Static)!;
        var fresh = new StickyNoteWPF.Services.AppSettings();
        normalize.Invoke(null, new object[] { fresh });
        if (fresh.NoteTextColor != "#FFFFFFFF" || fresh.DefaultColor != "#FF4B5058" || fresh.WindowOpacity != 0.85
            || fresh.TaskListColor != fresh.DefaultColor || fresh.TaskListTextColor != fresh.NoteTextColor || fresh.TaskListOpacity != fresh.WindowOpacity)
            throw new InvalidOperationException("新建便签和清单应为白字灰底、85% 不透明度。");
        var old = System.Text.Json.JsonSerializer.Deserialize<StickyNoteWPF.Services.AppSettings>("{\"DefaultColor\":\"#FFF7A900\",\"NoteTextColor\":\"#FF222222\",\"WindowOpacity\":1,\"TitleTextColor\":\"#FF333333\",\"ButtonColor\":\"#FF333333\"}")!;
        normalize.Invoke(null, new object[] { old });
        if (old.DefaultColor != fresh.DefaultColor || old.TaskListColor != fresh.TaskListColor || old.TitleTextColor != fresh.TitleTextColor || old.WindowOpacity != 0.85)
            throw new InvalidOperationException("旧出厂默认值应迁移到新配色。");
        var custom = new StickyNoteWPF.Services.AppSettings { DefaultColor = "#FF123456", NoteTextColor = "#FFEEEEEE", WindowOpacity = 0.62, TaskListColor = "#FFABCDEF", TaskListTextColor = "#FF112233", TaskListOpacity = 0.7 };
        normalize.Invoke(null, new object[] { custom });
        if (custom.DefaultColor != "#FF123456" || custom.WindowOpacity != 0.62 || custom.TaskListColor != "#FFABCDEF" || custom.TaskListOpacity != 0.7)
            throw new InvalidOperationException("自定义默认配色不应被迁移覆盖。");
        old.DefaultColor = "#FFF7A900"; old.NoteTextColor = "#FF222222"; old.WindowOpacity = 1;
        normalize.Invoke(null, new object[] { old });
        if (old.DefaultColor != "#FFF7A900") throw new InvalidOperationException("完成迁移后允许用户重新选择原配色。");
        Console.WriteLine("PASS: gray/white translucent defaults, legacy migration and custom appearance preservation.");
    }
    private static void CheckThemes(MainWindow manager, SettingsWindow settings, SyncPage sync, PeerSyncPage peer)
    {
        var app = App.Current;
        var noteColors = app.Notes.Select(n => n.Color).ToArray();
        var taskColors = app.TaskLists.Select(n => n.Color).ToArray();
        manager.Width = 860; manager.Height = 600;
        Click(manager, "NavNotes"); Click(manager, "CardViewButton");
        var list = (ListBox)manager.FindName("NoteList");
        manager.UpdateLayout();
        var cards = Enumerable.Range(0, list.Items.Count).Select(i => CardSnapshot(list, i)).ToArray();
        var originalCanvas = ((SolidColorBrush)manager.Background).Color;
        try
        {
            // 使用独立样例配置，只验证序列化和主题应用，不写用户 settings.json。
            app.Settings.DarkMode = true;
            var json = System.Text.Json.JsonSerializer.Serialize(app.Settings);
            if (System.Text.Json.JsonSerializer.Deserialize<StickyNoteWPF.Services.AppSettings>(json)?.DarkMode != true
                || System.Text.Json.JsonSerializer.Deserialize<StickyNoteWPF.Services.AppSettings>("{}")?.DarkMode != false)
                throw new InvalidOperationException("主题设置必须可保存，旧配置默认使用浅色。");
            StickyNoteWPF.Services.ThemeManager.Apply(true);
            Render(manager, "dark-manager-cards");
            CheckDarkSelectionContrast(manager, list);
            CheckCreateButtonLayout(manager);
            if (((SolidColorBrush)manager.Background).Color == originalCanvas)
                throw new InvalidOperationException("主题切换必须更新已打开的窗口。");
            AssertBrush(((Button)manager.FindName("NavNotes")).Foreground, "UiText");
            AssertBrush(((Button)manager.FindName("CardViewButton")).Foreground, "UiText");
            AssertBrush(((TextBox)sync.FindName("ServerBox")).Foreground, "UiText");
            AssertBrush(((TextBox)sync.FindName("ServerBox")).Background, "UiSurface");
            AssertBrush(((PasswordBox)sync.FindName("KeyBox")).Foreground, "UiText");
            AssertBrush(((ComboBox)peer.FindName("AddressBox")).Foreground, "UiText");
            for (var i = 0; i < cards.Length; i++)
                if (CardSnapshot(list, i) != cards[i]) throw new InvalidOperationException("主题不能改变自定义卡片的颜色或尺寸。");
            CheckSortLayout(manager);
            CheckTitleSelection(list);
            Click(manager, "RowViewButton"); Render(manager, "dark-manager-rows"); CheckTitleSelection(list);
            Click(manager, "NavTasks"); Click(manager, "CardViewButton"); Render(manager, "dark-manager-tasks");
            foreach (var section in Enum.GetValues<SettingsSection>())
            {
                if (section == SettingsSection.General) continue;
                settings.SelectSection(section);
                Render(settings, "dark-settings-" + section);
            }
            // 新窗口读取当前的主题与已加载配置。
            var reopened = new SettingsWindow(app.Settings);
            if (((CheckBox)reopened.FindName("DarkModeCheck")).IsChecked != true)
                throw new InvalidOperationException("设置窗口必须展示当前主题状态。");
            RenderWindow(reopened, "dark-settings-General");
            for (var i = 0; i < 3; i++)
            {
                StickyNoteWPF.Services.ThemeManager.Apply(false);
                StickyNoteWPF.Services.ThemeManager.Apply(true);
            }
            if (app.Resources.MergedDictionaries.Count != 2) throw new InvalidOperationException("切换主题不能累积资源字典。");
        }
        finally
        {
            app.Settings.DarkMode = false;
            StickyNoteWPF.Services.ThemeManager.Apply(false);
        }
        Render(manager, "light-restored");
        if (((SolidColorBrush)manager.Background).Color != originalCanvas
            || ((TextBox)sync.FindName("ServerBox")).Text != "https://sync.example.com"
            || !noteColors.SequenceEqual(app.Notes.Select(n => n.Color))
            || !taskColors.SequenceEqual(app.TaskLists.Select(n => n.Color)))
            throw new InvalidOperationException("主题切换必须保留页面输入与便签、清单颜色。");
        Console.WriteLine("PASS: live dark/light switching, theme persistence, existing/new pages and card selection.");
        static void AssertBrush(System.Windows.Media.Brush brush, string key)
        {
            if (((SolidColorBrush)brush).Color != ((SolidColorBrush)App.Current.FindResource(key)).Color)
                throw new InvalidOperationException("控件没有跟随主题资源：" + key);
        }
    }
    private static void CheckDarkSelectionContrast(MainWindow manager, ListBox list)
    {
        for (var index = 0; index < list.Items.Count; index++)
        {
            list.SelectedIndex = -1;
            manager.UpdateLayout();
            var original = CardSnapshot(list, index);
            var before = MarginPixel(index);
            list.SelectedIndex = index;
            Render(manager, "dark-selection-" + index);
            CheckSelection(list, index, original);
            var after = MarginPixel(index);
            if (after.R + after.G + after.B < before.R + before.G + before.B + 24)
                throw new InvalidOperationException("暗色模式的选中光晕必须在卡片边缘外有可见的亮度差。");
        }
        Console.WriteLine("PASS: rendered dark selection is visible on each card color without changing card size or fill.");

        Color MarginPixel(int index)
        {
            manager.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            manager.UpdateLayout();
            var content = (FrameworkElement)manager.Content;
            var card = Card(list, index);
            var point = card.TranslatePoint(new Point(-3, card.ActualHeight / 2), content);
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight,
                96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), pixel, 4, 0);
            return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        }
    }

    private static void CheckCreateButtonLayout(MainWindow manager)
    {
        var button = (Button)manager.FindName("AddNoteButton");
        var search = (TextBox)manager.FindName("SearchBox");
        var pressedKey = (DependencyPropertyKey)typeof(System.Windows.Controls.Primitives.ButtonBase)
            .GetField("IsPressedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var width = manager.Width;
        var height = manager.Height;
        try
        {
            foreach (var size in new[] { (860d, 600d), (manager.MinWidth, manager.MinHeight) })
            {
                manager.Width = size.Item1; manager.Height = size.Item2;
                foreach (var tasks in new[] { false, true })
                {
                    Click(manager, tasks ? "NavTasks" : "NavNotes");
                    search.Focus();
                    var original = Snapshot();
                    button.Focus(); AssertStable("获得焦点");
                    if (!button.IsKeyboardFocused)
                        throw new InvalidOperationException("新建按钮的布局检查未获得键盘焦点。");
                    button.SetValue(pressedKey, true); AssertStable("按下");
                    button.SetValue(pressedKey, false); AssertStable("松开");
                    search.Focus(); AssertStable("失去焦点");

                    void AssertStable(string phase)
                    {
                        if (!original.SequenceEqual(Snapshot()))
                            throw new InvalidOperationException("新建按钮" + phase + "时不能改变标题、工具栏或列表的位置和尺寸。");
                    }
                }
            }
        }
        finally
        {
            button.SetValue(pressedKey, false);
            manager.Width = width; manager.Height = height;
            Click(manager, "NavNotes");
            manager.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            manager.UpdateLayout();
        }
        Console.WriteLine("PASS: New button focus, press and release keep layout stable in both categories and window sizes.");

        Rect[] Snapshot()
        {
            manager.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            manager.UpdateLayout();
            return new[] { "AddNoteButton", "PageTitle", "SearchBox", "SortButton", "NoteList", "TaskListBox" }
                .Select(name => (FrameworkElement)manager.FindName(name))
                .Where(element => element.Visibility == Visibility.Visible)
                .Select(element => element.TransformToAncestor(manager).TransformBounds(new Rect(element.RenderSize))).ToArray();
        }
    }

    private static void CheckSortLayout(MainWindow manager)
    {
        var sort = (Button)manager.FindName("SortButton");
        var menu = sort.ContextMenu;
        var previousStaysOpen = menu.StaysOpen;
        menu.StaysOpen = true; // 屏幕外宿主的焦点切换不应干扰布局检查。
        var width = manager.Width;
        var height = manager.Height;
        try
        {
            foreach (var size in new[] { (860d, 600d), (manager.MinWidth, manager.MinHeight) })
            {
                manager.Width = size.Item1; manager.Height = size.Item2;
                foreach (var tasks in new[] { false, true })
                {
                    Click(manager, tasks ? "NavTasks" : "NavNotes");
                    var list = (ListBox)manager.FindName(tasks ? "TaskListBox" : "NoteList");
                    var source = tasks ? App.Current.TaskLists.Select(n => n.Title).ToArray() : App.Current.Notes.Select(n => n.Title).ToArray();
                    foreach (var tag in new[] { "none", "titleAsc", "titleDesc" })
                    {
                        ((TextBox)manager.FindName("SearchBox")).Focus();
                        Pump();
                        var original = Snapshot(list);
                        sort.Focus();
                        Pump();
                        Check(original, list, "获得焦点");
                        Click(manager, "SortButton");
                        Pump();
                        if (!menu.IsOpen) throw new InvalidOperationException("排序按钮必须打开菜单。");
                        Check(original, list, "展开菜单");
                        var selected = menu.Items.Cast<MenuItem>().Single(item => (string)item.Tag == tag);
                        selected.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        menu.IsOpen = false;
                        Pump();
                        Check(original, list, "选定排序");
                        var expected = tag switch
                        {
                            "titleAsc" => source.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(),
                            "titleDesc" => source.OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase).ToArray(),
                            _ => source
                        };
                        var actual = list.Items.Cast<object>().Select(item => item is StickyNoteModel note ? note.Title : ((TaskListModel)item).Title);
                        if (!actual.SequenceEqual(expected) || menu.Items.Cast<MenuItem>().Count(item => item.IsChecked) != 1 || !selected.IsChecked)
                            throw new InvalidOperationException("排序结果或菜单勾选状态不正确。");
                    }
                    menu.Items.Cast<MenuItem>().Single(item => (string)item.Tag == "none").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                }
            }
            Console.WriteLine("PASS: sort focus/menu/choices keep toolbar layout stable at normal/minimum sizes in both categories.");
        }
        finally
        {
            menu.IsOpen = false;
            menu.StaysOpen = previousStaysOpen;
            manager.Width = width; manager.Height = height;
            Click(manager, "NavNotes");
            Pump();
        }
        void Pump() { manager.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); manager.UpdateLayout(); }
        Rect[] Snapshot(ListBox list) => new[] { (FrameworkElement)manager.FindName("SearchBox"), sort,
            (FrameworkElement)manager.FindName("CardViewButton"), (FrameworkElement)manager.FindName("RowViewButton"),
            (FrameworkElement)manager.FindName("DefaultAppearanceButton"), (FrameworkElement)manager.FindName("ShowWindowsButton"), list }
            .Select(element => element.TransformToAncestor(manager).TransformBounds(new Rect(element.RenderSize))).ToArray();
        void Check(Rect[] original, ListBox list, string phase)
        {
            if (!original.SequenceEqual(Snapshot(list))) throw new InvalidOperationException("排序按钮" + phase + "时不应挤压搜索框或移动其他区域。");
        }
    }
    private static Border Card(ListBox list, int index)
    {
        var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(index);
        return FindBorder(container) ?? throw new InvalidOperationException("未找到卡片模板。");
        static Border? FindBorder(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Border border) return border;
                if (FindBorder(child) is { } nested) return nested;
            }
            return null;
        }
    }
    private static (Color Color, double Width, double Height) CardSnapshot(ListBox list, int index)
    {
        var card = Card(list, index);
        return (((SolidColorBrush)card.Background).Color, card.ActualWidth, card.ActualHeight);
    }
    private static void CheckSelection(ListBox list, int index, (Color Color, double Width, double Height) original)
    {
        var card = Card(list, index);
        if (CardSnapshot(list, index) != original || card.Effect is not null || !HasShadow(card))
            throw new InvalidOperationException("选中时应仅增加阴影，保持卡片颜色和尺寸。");
    }
    private static bool HasShadow(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Border { Effect: System.Windows.Media.Effects.DropShadowEffect }) return true;
            if (HasShadow(child)) return true;
        }
        return false;
    }
    private static void CheckTitleSelection(ListBox list)
    {
        list.SelectedIndex = 0;
        var first = FindTitle(Card(list, 0))!;
        var second = FindTitle(Card(list, 1))!;
        first.Focus();
        second.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
        { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent });
        Check(1);
        first.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice, Environment.TickCount, second, first)
        { RoutedEvent = System.Windows.Input.Keyboard.GotKeyboardFocusEvent });
        Check(0);
        void Check(int selected)
        {
            list.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            list.UpdateLayout();
            if (list.SelectedIndex != selected || list.SelectedItems.Count != 1)
                throw new InvalidOperationException("标题和主体必须使用同一个单选状态。");
            for (var i = 0; i < list.Items.Count; i++)
                if (HasShadow(Card(list, i)) != (i == selected))
                    throw new InvalidOperationException("上一张卡片的阴影必须随选中项切换而取消。");
        }
        static TextBox? FindTitle(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBox title) return title;
                if (FindTitle(child) is { } nested) return nested;
            }
            return null;
        }
    }
    private static void RenderWindow(Window window, string name)
    {
        PlaceOffscreen(window);
        try { window.Show(); Render(window, name); }
        finally { window.Close(); }
    }
    private static void PlaceOffscreen(Window window)
    {
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
    }
    private static void Render(Window window, string name)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (App.Current.Notes.Count != 3 || App.Current.TaskLists[0].Title != "本周安排")
            throw new InvalidOperationException("预览必须始终使用独立样例数据。");
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Output, name + ".png"));
        encoder.Save(file);
        Console.WriteLine("Rendered " + name);
    }
}

// Application 构造时会把启动回调放入 Dispatcher 队列，必须显式拦截，
// 即使不调用 Application.Run，也不能执行真实的本地数据加载及系统服务初始化。
internal sealed class PreviewApp : App
{
    private sealed class PreviewSettings : StickyNoteWPF.Services.AppSettings
    {
        public override bool Save() => true;
    }
    public PreviewApp() => typeof(App).GetProperty(nameof(Settings))!.SetValue(this, new PreviewSettings());
    protected override void OnStartup(StartupEventArgs e) { }
    protected override void OnExit(ExitEventArgs e) { }
}
