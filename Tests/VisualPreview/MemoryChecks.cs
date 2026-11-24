using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StickyNoteWPF;
using StickyNoteWPF.Models;

internal static class MemoryChecks
{
    private static readonly string Output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../TestResults/memory-checks"));
    private static readonly Dictionary<string, object> Results = new();

    public static void Run(string name, bool verify, string scenario)
    {
        Directory.CreateDirectory(Output);
        CreateImage(Path.Combine(Output, "background-0.png"));
        for (var i = 1; i < 3; i++) File.Copy(Path.Combine(Output, "background-0.png"), Path.Combine(Output, $"background-{i}.png"), true);
        Sample("idle");
        if (scenario is "all" or "images") { CheckImages(verify); Sample("images-closed"); }
        if (scenario is "all" or "manager") { CheckManager(verify); Sample("manager-closed"); }
        if (verify) { CheckStorage(); CheckImageModes(); CheckReleasedWindows(); CheckLongTaskList(); }
        var path = Path.Combine(Output, name + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Memory results: " + path);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckImages(bool verify)
    {
        var windows = new List<Window>();
        var notes = new List<StickyNoteModel>();
        var lists = new List<TaskListModel>();
        SetData(notes, lists);
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var path = Path.Combine(Output, $"background-{i}.png");
                var note = new StickyNoteModel { Title = "图片便签", BackgroundImagePath = path, Width = 320, Height = 280, Text = "独立样例，不读写用户便签。" };
                var list = new TaskListModel { Title = "图片清单", BackgroundImagePath = path, Items = { new TaskItem { Text = "检查内存" } } };
                notes.Add(note); lists.Add(list);
                windows.Add(new StickyNoteWindow(note));
                windows.Add(new TaskListWindow(list));
            }
            foreach (var window in windows) Show(window);
            Sample("images-open");
            var textBox = (TextBox)windows[0].FindName("NoteTextBox");
            var template = textBox.Template;
            var source = ((ImageBrush)((Border)windows[0].FindName("RootBorder")).Background).ImageSource;
            var allocated = GC.GetTotalAllocatedBytes(true);
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < 12; i++)
                foreach (var window in windows)
                    if (window is StickyNoteWindow note) note.RefreshFromModel();
                    else ((TaskListWindow)window).RefreshFromModel();
            Pump();
            Results["refresh-allocated-bytes"] = GC.GetTotalAllocatedBytes(true) - allocated;
            Results["refresh-milliseconds"] = stopwatch.Elapsed.TotalMilliseconds;
            var images = windows.Select(window => ((ImageBrush)((Border)window.FindName("RootBorder")).Background).ImageSource).Cast<BitmapSource>().ToArray();
            Results["image-sizes"] = images.Select(image => $"{image.PixelWidth}x{image.PixelHeight}").ToArray();
            Sample("images-refreshed");
            if (verify)
            {
                if (!ReferenceEquals(template, textBox.Template) || !ReferenceEquals(source, images[0]))
                    throw new InvalidOperationException("刷新外观不能重建文本模板或重新解码同一张图片。");
                if (images.Any(image => (long)image.PixelWidth * image.PixelHeight >= 3000L * 2000))
                    throw new InvalidOperationException("缩放背景应按窗口所需尺寸解码。");
                foreach (var path in notes.Select(note => note.BackgroundImagePath))
                    using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
        }
        finally { foreach (var window in windows) window.Close(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckManager(bool verify)
    {
        var notes = Enumerable.Range(0, 600).Select(i => new StickyNoteModel { Title = $"便签 {i:D4}", Text = new string('文', 4000), Color = "#FFE7C765" }).ToList();
        SetData(notes, new());
        var manager = new MainWindow();
        try
        {
            Show(manager);
            var list = (ListBox)manager.FindName("NoteList");
            var cards = Realized(list);
            Results["card-containers"] = cards;
            Sample("manager-cards");
            ((Button)manager.FindName("RowViewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Results["row-containers"] = Realized(list);
            Sample("manager-rows");
            if (verify && (cards > 60 || Realized(list) > 60))
                throw new InvalidOperationException("管理器应只创建可见区域附近的卡片和列表行。");
            if (verify)
            {
                CheckScrolling(list, notes);
                ((Button)manager.FindName("CardViewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(); manager.UpdateLayout();
                CheckScrolling(list, notes);
                notes.RemoveAt(0);
                manager.RefreshLists(); Pump(); manager.UpdateLayout();
                list.ScrollIntoView(notes[^1]); Pump(); manager.UpdateLayout();
                if (list.ItemContainerGenerator.ContainerFromItem(notes[^1]) is null)
                    throw new InvalidOperationException("数据变化后仍应能滚动到末尾。");
                ((TextBox)manager.FindName("SearchBox")).Text = "不存在的内容";
                Pump(); manager.UpdateLayout();
                if (list.Items.Count != 0) throw new InvalidOperationException("虚拟化后搜索结果必须正常更新。");
                ((TextBox)manager.FindName("SearchBox")).Clear(); Pump(); manager.UpdateLayout();
                list.ScrollIntoView(notes[0]); Pump(); manager.UpdateLayout();
                if (list.ItemContainerGenerator.ContainerFromItem(notes[0]) is null)
                    throw new InvalidOperationException("恢复搜索后必须正常显示内容。");
                Console.WriteLine("PASS: bounded card/row containers, scrolling, title editing, deletion and filtering.");
            }
        }
        finally { manager.Close(); }
    }

    private static int Realized(ListBox list) => Enumerable.Range(0, list.Items.Count).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null);
    private static void CheckScrolling(ListBox list, List<StickyNoteModel> notes)
    {
        foreach (var index in new[] { 599, 300, 0 })
        {
            list.ScrollIntoView(notes[index]); Pump(); list.UpdateLayout(); Pump();
            if (list.ItemContainerGenerator.ContainerFromItem(notes[index]) is not ListBoxItem container)
                throw new InvalidOperationException("虚拟化后必须能滚动到任意便签。");
            if (Realized(list) > 60) throw new InvalidOperationException("滚动后不能累积离屏容器。");
            var title = Find<TextBox>(container)!;
            var original = notes[index].Title;
            title.Text = "标题编辑测试"; Pump();
            if (notes[index].Title != "标题编辑测试") throw new InvalidOperationException("滚动后的标题必须写回正确便签。");
            title.Text = original;
            list.SelectedItem = notes[index]; Pump();
            if (list.SelectedItem != notes[index]) throw new InvalidOperationException("滚动后选中状态不正确。");
        }
    }

    private static void CheckStorage()
    {
        var type = typeof(StickyNoteWPF.Services.NoteStore);
        var write = type.GetMethod("WriteFile", BindingFlags.NonPublic | BindingFlags.Static)!;
        var read = type.GetMethod("ReadFile", BindingFlags.NonPublic | BindingFlags.Static)!;
        var path = Path.Combine(Output, "storage-roundtrip.json");
        var data = new StickyNoteWPF.Services.StoreData
        {
            Notes = new() { new() { Title = "中文与 emoji 😀", Text = new string('文', 600000) } },
            TaskLists = new() { new() { Left = 100, Top = 100, Items = { new TaskItem { Text = "父任务", SubItems = { new TaskItem { Text = "子任务", IsDone = true } } } } } }
        };
        write.Invoke(null, new object[] { path, data });
        var loaded = (StickyNoteWPF.Services.StoreData)read.Invoke(null, new object[] { path })!;
        if (loaded.Notes[0].Text != data.Notes[0].Text || loaded.Notes[0].Id != data.Notes[0].Id
            || !loaded.TaskLists[0].Items[0].SubItems[0].IsDone || File.Exists(path + ".tmp"))
            throw new InvalidOperationException("流式存储必须完整保留大文本、ID、子任务与样式。");
        var before = File.ReadAllBytes(path);
        data.Notes[0].Width = double.NaN;
        try { write.Invoke(null, new object[] { path, data }); throw new InvalidOperationException("无效值必须拒绝序列化。"); }
        catch (TargetInvocationException) { }
        if (!before.SequenceEqual(File.ReadAllBytes(path))) throw new InvalidOperationException("保存失败不得覆盖已有数据。");
        File.WriteAllText(path, " \r\n" + JsonSerializer.Serialize(loaded.Notes), new System.Text.UTF8Encoding(true));
        loaded = (StickyNoteWPF.Services.StoreData)read.Invoke(null, new object[] { path })!;
        if (loaded.Notes.Count != 1 || loaded.TaskLists.Count != 0) throw new InvalidOperationException("必须兼容带 BOM 的旧版数组格式。");
        File.WriteAllText(path, "{invalid");
        try { read.Invoke(null, new object[] { path }); throw new InvalidOperationException("损坏 JSON 必须拒绝加载。"); }
        catch (TargetInvocationException ex) when (ex.InnerException is JsonException) { }
        Console.WriteLine("PASS: streaming storage roundtrip, legacy/BOM, corrupt input and atomic failure protection.");
    }

    private static void CheckImageModes()
    {
        var path = Path.Combine(Output, "background-0.png");
        var model = new TaskListModel { BackgroundImagePath = path, Color = "#FF83BEAF", Opacity = 0.45 };
        var window = new TaskListWindow(model);
        Show(window);
        try
        {
            foreach (var mode in new[] { "Center", "Tile", "Stretch", "Fill" })
            {
                model.BackgroundImageMode = mode; window.RefreshFromModel();
                var brush = (ImageBrush)((Border)window.FindName("RootBorder")).Background;
                if (brush.Opacity != model.Opacity) throw new InvalidOperationException("背景透明度必须保留。");
                if (mode is "Center" or "Tile")
                {
                    var bitmap = (BitmapSource)brush.ImageSource;
                    if (bitmap.PixelWidth != 3000 || bitmap.PixelHeight != 2000 || brush.Stretch != Stretch.None)
                        throw new InvalidOperationException("居中和平铺必须保留原图尺寸与显示方式。");
                }
            }
            model.BackgroundImagePath = Path.Combine(Output, "missing-image.png"); window.RefreshFromModel();
            var solid = ((Border)window.FindName("RootBorder")).Background as SolidColorBrush;
            if (solid?.Color.G != 0xBE) throw new InvalidOperationException("图片缺失必须回退到便签颜色。");
            var damaged = Path.Combine(Output, "damaged-image.png");
            File.WriteAllText(damaged, "invalid image");
            model.BackgroundImagePath = damaged; window.RefreshFromModel();
            if (((Border)window.FindName("RootBorder")).Background is not SolidColorBrush)
                throw new InvalidOperationException("损坏图片必须回退到便签颜色。");
            var changed = Path.Combine(Output, "changed-image.png");
            File.Copy(path, changed, true);
            model.BackgroundImagePath = changed; window.RefreshFromModel();
            var previous = ((ImageBrush)((Border)window.FindName("RootBorder")).Background).ImageSource;
            var replacement = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, new byte[64 * 64 * 4], 64 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(replacement));
            using (var file = File.Create(changed)) encoder.Save(file);
            window.RefreshFromModel();
            if (ReferenceEquals(previous, ((ImageBrush)((Border)window.FindName("RootBorder")).Background).ImageSource))
                throw new InvalidOperationException("同一路径的图片被替换后必须更新。");
            model.BackgroundImagePath = path; window.RefreshFromModel();
            var old = ((ImageBrush)((Border)window.FindName("RootBorder")).Background).ImageSource;
            window.Width = 1100; window.Height = 950;
            var wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 350) { Pump(); Thread.Sleep(10); }
            var resized = ((ImageBrush)((Border)window.FindName("RootBorder")).Background).ImageSource;
            if (((BitmapSource)resized).PixelWidth <= ((BitmapSource)old).PixelWidth)
                throw new InvalidOperationException("放大窗口后必须补充足够清晰的背景图。");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS: image modes, opacity, missing/corrupt files, replacement and resize decoding.");
    }

    private static void CheckReleasedWindows()
    {
        var references = OpenAndClose();
        var wait = Stopwatch.StartNew();
        while (references.Any(reference => reference.IsAlive) && wait.ElapsedMilliseconds < 2000)
        {
            Pump(); GC.Collect(); GC.WaitForPendingFinalizers(); Thread.Sleep(30);
        }
        if (references.Any(reference => reference.IsAlive)) throw new InvalidOperationException("关闭的窗口和背景图不应被计时器或缓存强引用。");
        Console.WriteLine("PASS: closed hover windows and decoded images are collectible.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] OpenAndClose()
    {
        var path = Path.Combine(Output, "released-image.png");
        File.Copy(Path.Combine(Output, "background-0.png"), path, true);
        var note = new StickyNoteWindow(new StickyNoteModel { BackgroundImagePath = path, HoverToShow = true });
        var task = new TaskListWindow(new TaskListModel { BackgroundImagePath = path, HoverToShow = true });
        Show(note); Show(task);
        var references = new[] { new WeakReference(note), new WeakReference(task),
            new WeakReference(((ImageBrush)((Border)note.FindName("RootBorder")).Background).ImageSource),
            new WeakReference(((ImageBrush)((Border)task.FindName("RootBorder")).Background).ImageSource) };
        note.Close(); task.Close();
        return references;
    }

    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (Find<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static void CheckLongTaskList()
    {
        var model = new TaskListModel();
        for (var i = 0; i < 600; i++) model.Items.Add(new TaskItem { Text = $"任务 {i}", Order = i });
        SetData(new(), new() { model });
        var manager = new MainWindow(); Show(manager);
        var window = new TaskListWindow(model); Show(window);
        try
        {
            var list = (ListBox)window.FindName("TaskList");
            list.UpdateLayout();
            if (Realized(list) > 60) throw new InvalidOperationException("长任务清单必须按可见区域创建控件。");
            list.ScrollIntoView(model.Items[0]); Pump(); list.UpdateLayout();
            typeof(TaskListWindow).GetMethod("AddTask", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
            Pump(); list.UpdateLayout(); Pump();
            var added = model.Items[^1];
            if (list.Items[0] != added || list.ItemContainerGenerator.ContainerFromItem(added) is null)
                throw new InvalidOperationException("新建任务必须保持置顶并能聚焦编辑。");
            typeof(TaskListWindow).GetMethod("AddSubTask", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { added });
            Pump(); list.UpdateLayout(); Pump();
            if (Find<TextBox>((DependencyObject)list.ItemContainerGenerator.ContainerFromItem(added)) is null)
                throw new InvalidOperationException("新建子任务后必须能编辑。");
            added.IsDone = true; model.ShowCompleted = false; window.ApplyShowCompleted(); Pump(); list.UpdateLayout();
            if (list.Items.Contains(added)) throw new InvalidOperationException("隐藏已完成任务必须保持有效。");
            ((Button)manager.FindName("NavTasks")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            manager.UpdateLayout(); Pump();
            var taskCards = (ListBox)manager.FindName("TaskListBox");
            var container = (DependencyObject)taskCards.ItemContainerGenerator.ContainerFromIndex(0);
            var preview = Find<ItemsControl>(container)!;
            var count = Enumerable.Range(0, preview.Items.Count).Count(i => preview.ItemContainerGenerator.ContainerFromIndex(i) != null);
            if (count > 15) throw new InvalidOperationException("卡片中的任务预览不能生成整份长清单。");
            Console.WriteLine($"PASS: long task list, add/focus, subtasks, completed filter and {count} preview containers.");
        }
        finally { window.Close(); manager.Close(); }
    }
    private static void SetData(List<StickyNoteModel> notes, List<TaskListModel> lists)
    {
        typeof(App).GetField("_notes", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(App.Current, notes);
        typeof(App).GetField("_taskLists", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(App.Current, lists);
    }
    private static void Show(Window window)
    {
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000; window.Top = -10000;
        window.Show(); Pump();
    }
    private static void Pump() => App.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Sample(string name)
    {
        var settle = Stopwatch.StartNew();
        while (settle.ElapsedMilliseconds < 200) { Pump(); Thread.Sleep(10); }
        Pump(); GC.Collect(); GC.WaitForPendingFinalizers(); Pump(); GC.Collect();
        using var process = Process.GetCurrentProcess();
        var sample = new { Managed = GC.GetTotalMemory(false), Private = process.PrivateMemorySize64, WorkingSet = process.WorkingSet64 };
        Results[name] = sample;
        Console.WriteLine($"{name}: managed={sample.Managed / 1048576d:F1} MiB, private={sample.Private / 1048576d:F1} MiB, workingSet={sample.WorkingSet / 1048576d:F1} MiB");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateImage(string path)
    {
        if (File.Exists(path)) return;
        const int width = 3000, height = 2000;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 120; pixels[i + 1] = (byte)(i / 4 % 200); pixels[i + 2] = 220; pixels[i + 3] = 255; }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }
}
