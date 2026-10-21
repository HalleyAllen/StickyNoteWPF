using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StickyNoteWPF;
using StickyNoteWPF.Models;
using StickyNoteWPF.Services;
using Application = System.Windows.Application;

internal static class Program
{
    private static string _phase = "initialization";

    // Application queues OnStartup on its dispatcher even when the test never calls Run().
    // Exercise real controls without loading user data, acquiring the single-instance mutex
    // or installing the production persistence/exit handlers in the test host.
    private sealed class SmokeApp : App
    {
        public Func<string, bool> SnapshotWriter { get; set; } = _ => true;
        public Func<bool> RetryDecision { get; set; } = () => throw new Exception("Unexpected save failure.");
        public bool ExitCompleted { get; private set; }
        public int SaveWarnings { get; private set; }
        protected override void ShowSaveError() => SaveWarnings++;
        public bool CancelSessionEnding()
        {
            var args = (SessionEndingCancelEventArgs)Activator.CreateInstance(
                typeof(SessionEndingCancelEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, args: new object[] { ReasonSessionEnding.Logoff }, culture: null)!;
            base.OnSessionEnding(args);
            return args.Cancel;
        }
        protected override bool WriteSnapshot(string json) => SnapshotWriter(json);
        protected override bool ConfirmSaveRetry() => RetryDecision();
        protected override void ShutdownAfterSave() => ExitCompleted = true;
        protected override void OnStartup(StartupEventArgs e) { }
        protected override void OnExit(ExitEventArgs e) { }
    }

    private sealed class DepthTestWindow(TaskListModel list) : TaskListWindow(list)
    {
        public int LimitWarnings { get; private set; }
        protected override void ShowTaskDepthLimit() => LimitWarnings++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        using var watchdog = new System.Threading.Timer(_ =>
        {
            Console.WriteLine("::error::Windows smoke timed out during " + Volatile.Read(ref _phase));
            Environment.Exit(1);
        }, null, TimeSpan.FromSeconds(120), Timeout.InfiniteTimeSpan);
        try
        {
            var app = new SmokeApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/StickyNote;component/Resources/TextBoxStyles.xaml", UriKind.Relative)
            });
            app.DispatcherUnhandledException += (_, e) => ReportError(e.Exception);
            RunCheck("templates and opacity", () => CheckTemplatesAndOpacity(app));
            RunCheck("hover timers", CheckHoverTimers);
            RunCheck("image cache", CheckImageCache);
            RunCheck("hotkey replacement", CheckHotKeyReplacement);
            RunCheck("manager virtualization", () => CheckManagerVirtualization(app));
            RunCheck("locked task editing", CheckLockedTaskEditing);
            RunCheck("task depth limit", CheckTaskDepthLimit);
            RunCheck("settings validation", () => CheckSettingsValidation(app));
            RunCheck("save failure exit protection", () => CheckExitProtection(app));
            if (args.Length == 1) RunCheck("published application", () => CheckPublishedApplication(args[0]));
            Console.WriteLine("PASS: Windows WPF smoke checks completed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            ReportError(ex);
            return 1;
        }
        finally { Application.Current?.Shutdown(); }
    }

    private static void ReportError(Exception ex)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            Console.WriteLine("::error::" + ex.ToString().Replace("%", "%25")
                .Replace("\r", "%0D").Replace("\n", "%0A"));
    }

    private static void RunCheck(string phase, Action check)
    {
        Volatile.Write(ref _phase, phase);
        Console.WriteLine("::notice::Starting " + phase);
        check();
        Console.WriteLine("::notice::Passed " + phase);
    }

    private static void CheckTemplatesAndOpacity(App app)
    {
        var note = new StickyNoteWindow(new StickyNoteModel { Opacity = 0.25 });
        var tasks = new TaskListWindow(new TaskListModel { Opacity = 0.25 });
        try
        {
            note.Show();
            tasks.Show();
            var textbox = (TextBox)note.FindName("NoteTextBox");
            var template = textbox.Template;
            textbox.Text = "first line\nsecond line";
            textbox.CaretIndex = 5;
            note.RefreshFromModel();
            Check(ReferenceEquals(template, textbox.Template), "The text template was recreated.");
            Check(textbox.CaretIndex == 5, "Refreshing the theme moved the caret.");
            app.Settings.ForceShowAll = true;
            note.ApplyForceShowAll();
            tasks.ApplyForceShowAll();
            Check(BackgroundAlpha(note) == 255 && BackgroundAlpha(tasks) == 255, "Force-show left a transparent background.");
            app.Settings.ForceShowAll = false;
            note.ApplyForceShowAll();
            tasks.ApplyForceShowAll();
            Check(BackgroundAlpha(note) == 63 && BackgroundAlpha(tasks) == 63, "Stored opacity was not restored.");
        }
        finally { note.Close(); tasks.Close(); }
        Console.WriteLine("PASS: shared template, caret preservation and force-show opacity.");
    }

    private static byte BackgroundAlpha(Window window)
        => ((SolidColorBrush)((Border)window.FindName("RootBorder")).Background).Color.A;

    private static void CheckHoverTimers()
    {
        foreach (Window window in new Window[]
        {
            new StickyNoteWindow(new StickyNoteModel { HoverToShow = true }),
            new TaskListWindow(new TaskListModel { HoverToShow = true })
        })
        {
            window.Show();
            Check(HoverTimer(window)?.IsEnabled == true, "Visible hover window has no active timer.");
            window.Hide();
            Check(HoverTimer(window) == null, "Hidden window retained a hover timer.");
            window.Show();
            Check(HoverTimer(window)?.IsEnabled == true, "Hover timer did not resume.");
            window.Close();
            Check(HoverTimer(window) == null, "Closed window retained a hover timer.");
        }
        Console.WriteLine("PASS: hover timer hide, resume and close lifecycle.");
    }

    private static DispatcherTimer? HoverTimer(Window window)
        => (DispatcherTimer?)window.GetType().GetField("_hoverTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);

    private static void CheckImageCache()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96,
                PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4)));
            using (var stream = System.IO.File.Create(path)) encoder.Save(stream);
            var loader = new BackgroundImageLoader();
            var first = loader.Load(path);
            Check(first.IsFrozen && ReferenceEquals(first, loader.Load(path)), "Unchanged image was decoded again.");
            System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(10));
            Check(!ReferenceEquals(first, loader.Load(path)), "Changed image was not reloaded.");
            System.IO.File.Delete(path);
            Check(!System.IO.File.Exists(path), "Image loading left the source file locked.");
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        Console.WriteLine("PASS: image reuse, invalidation and file handle release.");
    }

    private static void CheckHotKeyReplacement()
    {
        using var current = new HotKeyService();
        using var blocker = new HotKeyService();
        int callbacks = 0;
        Check(current.Register("Ctrl+Alt+Shift+F10", () => callbacks++), "Initial smoke-test hotkey is unavailable.");
        Check(blocker.Register("Ctrl+Alt+Shift+F11", () => { }), "Conflict-test hotkey is unavailable.");
        var field = typeof(HotKeyService).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var original = (HwndSource)field.GetValue(current)!;
        Check(!current.Register("Ctrl+Alt+Shift+F11", () => throw new Exception("replacement callback")),
            "Conflicting hotkey unexpectedly registered.");
        Check(ReferenceEquals(original, field.GetValue(current)), "Failed replacement disposed the original hotkey.");
        SendMessage(original.Handle, 0x0312, (IntPtr)0x5A11, IntPtr.Zero);
        Check(callbacks == 1, "Original hotkey callback no longer works.");
        Console.WriteLine("PASS: failed hotkey replacement preserves the original registration and callback.");
    }

    private static void CheckManagerVirtualization(App app)
    {
        var notes = (System.Collections.ObjectModel.ObservableCollection<StickyNoteModel>)app.Notes;
        for (int i = 0; i < 500; i++) notes.Add(new StickyNoteModel { Title = "Note " + i });
        var manager = new MainWindow();
        try
        {
            manager.Show();
            var list = (ListBox)manager.FindName("NoteList");
            var source = list.ItemsSource;
            var button = (Button)manager.FindName("RowViewButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            manager.UpdateLayout();
            manager.RefreshLists();
            manager.UpdateLayout();
            Check(ReferenceEquals(source, list.ItemsSource), "Manager replaced its collection view.");
            int realized = Enumerable.Range(0, list.Items.Count).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null);
            Check(realized > 0 && realized < 100, $"Row virtualization failed: {realized} of {list.Items.Count} containers realized.");

            // Search-only changes must refresh even when CustomSort stays null.
            var search = (TextBox)manager.FindName("SearchBox");
            search.Text = "Note 499";
            manager.RefreshLists();
            Check(list.Items.Count == 1 && ((StickyNoteModel)list.Items[0]).Title == "Note 499",
                "Search did not refresh an unsorted view.");
            search.Text = "no-matching-note";
            manager.RefreshLists();
            Check(list.Items.Count == 0 && ((TextBlock)manager.FindName("EmptyHint")).Visibility == Visibility.Visible,
                "Empty search results did not update the view and hint.");
            search.Clear();
            manager.RefreshLists();
            Check(list.Items.Count == 500, "Clearing search did not restore the collection.");
            var sortMenu = ((Button)manager.FindName("SortButton")).ContextMenu;
            var descending = sortMenu.Items.OfType<MenuItem>().Single(item => (string)item.Tag == "titleDesc");
            descending.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(((StickyNoteModel)list.Items[0]).Title == "Note 99", "Descending title sort did not refresh.");
            Check(ReferenceEquals(source, list.ItemsSource), "Filtering or sorting replaced the manager view.");
        }
        finally { manager.Close(); notes.Clear(); }
        Console.WriteLine("PASS: manager startup, repeated refresh, filtering, sorting and virtualized rows for 500 notes.");
    }

    private static void CheckSettingsValidation(App app)
    {
        var invalid = AppSettings.Parse("""
            {"DefaultFontSize":-20,"WindowOpacity":4,"TaskListFontSize":0,"TaskListOpacity":-1,
             "DefaultColor":null,"NoteTextColor":"bad color","TitleTextColor":"bad color",
             "ButtonColor":null,"TaskListColor":"bad color","TaskListTextColor":null,
             "ToggleWindowsHotKey":null,"RecentColors":[null,"bad color","#FF112233","#ff112233"],
             "NoteSettingsWidth":-1,"NoteSettingsHeight":1e300,"SettingsWindowWidth":0,"SettingsWindowHeight":-1}
            """);
        Check(invalid.DefaultFontSize == 14 && invalid.TaskListFontSize == 14 && invalid.WindowOpacity == 1
            && invalid.TaskListOpacity == 0, "Invalid font/opacity values survived validation.");
        Check(invalid.NoteSettingsWidth == 360 && invalid.NoteSettingsHeight == 4096
            && invalid.SettingsWindowWidth == 380 && invalid.SettingsWindowHeight == 470,
            "Invalid window dimensions survived validation.");
        Check(invalid.RecentColors.SequenceEqual(new[] { "#FF112233" }) && invalid.ToggleWindowsHotKey == "",
            "Null/invalid/duplicate palette or hotkey values survived validation.");
        foreach (var color in new[] { invalid.DefaultColor, invalid.NoteTextColor, invalid.TitleTextColor,
            invalid.ButtonColor, invalid.TaskListColor, invalid.TaskListTextColor })
            Check(AppearanceHelper.TryParseColor(color, out _), "An invalid color survived validation.");
        var legacy = AppSettings.Parse("""{"DefaultFontSize":24,"WindowOpacity":0.4,"DefaultColor":"#FF123456","RecentColors":null}""");
        Check(legacy.TaskListFontSize == 24 && legacy.TaskListOpacity == 0.4
            && legacy.TaskListColor == "#FF123456" && legacy.RecentColors.Count == AppearanceHelper.MaxRecentColors,
            "Legacy task defaults did not migrate from validated note defaults.");
        legacy.DefaultFontSize = 20;
        legacy.Normalize();
        Check(legacy.TaskListFontSize == 24, "Independent task defaults changed with note defaults.");
        invalid.DefaultFontSize = double.NaN;
        invalid.WindowOpacity = double.PositiveInfinity;
        invalid.NoteSettingsWidth = double.PositiveInfinity;
        invalid.Normalize();
        Check(invalid.DefaultFontSize == 14 && invalid.WindowOpacity == 1 && invalid.NoteSettingsWidth == 360,
            "Non-finite values survived validation.");
        var upper = AppSettings.Parse("""{"DefaultFontSize":1e300,"TaskListFontSize":0.1,"RecentColors":[]}""");
        Check(upper.DefaultFontSize == 48 && upper.TaskListFontSize == 8 && upper.RecentColors.Count > 0,
            "Extreme fonts or empty palettes survived validation.");

        // New notes use validated defaults directly. Exercise their real WPF font assignment.
        var note = new StickyNoteWindow(new StickyNoteModel { FontSize = invalid.DefaultFontSize,
            Color = invalid.DefaultColor, Opacity = invalid.WindowOpacity });
        try
        {
            note.Show();
            Check(((TextBox)note.FindName("NoteTextBox")).FontSize == 14, "Validated defaults could not create a note.");
        }
        finally { note.Close(); }
    }

    private static void CheckTaskDepthLimit()
    {
        var model = new TaskListModel();
        var task = new TaskItem { IsExpanded = false };
        model.Items.Add(task);
        for (int i = 1; i < TaskTreeLimits.MaximumDepth; i++)
        {
            var child = new TaskItem { IsExpanded = false };
            task.SubItems.Add(child);
            task = child;
        }
        var window = new DepthTestWindow(model);
        try
        {
            typeof(TaskListWindow).GetMethod("AddSubTask", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object[] { task });
            Check(task.SubItems.Count == 0 && window.LimitWarnings == 1,
                "Depth limit mutated the tree or failed to explain the limit.");
            var data = StoreDataCodec.Parse(StoreDataCodec.Serialize(new StoreData { TaskLists = [model] }));
            Check(data.TaskLists.Count == 1, "Depth-limited tree could not be saved and reopened.");
        }
        finally { window.Close(); }
    }

    private static void CheckLockedTaskEditing()
    {
        var root = new TaskItem { Text = "root", IsExpanded = true };
        var child = new TaskItem { Text = "child" };
        root.SubItems.Add(child);
        var model = new TaskListModel { IsLocked = true, ShowCompleted = true };
        model.Items.Add(root);
        var window = new TaskListWindow(model);
        try
        {
            window.Show();
            window.UpdateLayout();
            foreach (var item in new[] { root, child })
            {
                var textbox = (TextBox?)typeof(TaskListWindow)
                    .GetMethod("FindTextBoxForItem", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, new object[] { item });
                Check(textbox != null && textbox.IsReadOnly, "Locked task text was editable.");
                foreach (var key in new[] { Key.Enter, Key.Tab })
                {
                    var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(textbox!),
                        Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    textbox!.RaiseEvent(args);
                }
            }
            // Also guard alternate UI entry points, including stale/previously queued clicks.
            ((Button)window.FindName("AddTaskButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            typeof(TaskListWindow).GetMethod("AddSubTask", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object[] { root });
            VisualDescendants(window).OfType<Button>()
                .Single(button => button.DataContext == root && button.Content?.ToString() == "🗑")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(model.Items.Count == 1 && root.SubItems.Count == 1 && child.SubItems.Count == 0,
                "Enter, Tab or an alternate command mutated a locked task tree.");

            var checkbox = VisualDescendants(window).OfType<CheckBox>().First(box => box.DataContext == root);
            checkbox.IsChecked = true;
            Check(root.IsDone, "Locking prevented marking a task complete.");
            checkbox.IsChecked = false;
            ((Button)window.FindName("LockButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            var editable = VisualDescendants(window).OfType<TextBox>().First(box => box.DataContext == root);
            Check(!model.IsLocked && !editable.IsReadOnly, "Unlocking did not restore editing.");
            foreach (var key in new[] { Key.Enter, Key.Tab })
                editable.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editable),
                    Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Check(model.Items.Count == 2 && root.SubItems.Count == 2,
                "Unlocking did not restore Enter and Tab task creation.");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS: locked roots and children reject structural edits, completion remains available, and unlocking restores keyboard editing.");
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }

    private static void CheckExitProtection(SmokeApp app)
    {
        var notes = (System.Collections.ObjectModel.ObservableCollection<StickyNoteModel>)app.Notes;
        var lists = (System.Collections.ObjectModel.ObservableCollection<TaskListModel>)app.TaskLists;
        var note = new StickyNoteModel { Text = "unsaved text" };
        var list = new TaskListModel();
        notes.Add(note);
        lists.Add(list);
        app.OpenNote(note);
        app.OpenTaskList(list);
        var windows = app.Windows.Cast<Window>().ToArray();
        try
        {
            int prompts = 0;
            app.SnapshotWriter = _ => false;
            Check(app.CancelSessionEnding() && app.SaveWarnings == 1 && windows.All(window => window.IsVisible),
                "A failed save allowed Windows logoff or closed editing windows.");
            app.RetryDecision = () =>
            {
                prompts++;
                Check(windows.All(window => window.IsVisible), "Windows closed before the retry/cancel decision.");
                return false;
            };
            Check(!AwaitOnDispatcher(app.RequestExitAsync()), "Failed write allowed exit.");
            Check(!app.ExitCompleted && prompts == 1 && windows.All(window => window.IsVisible),
                "Canceling exit discarded the open editing session.");
            Check(notes.Contains(note) && lists.Contains(list) && note.Text == "unsaved text",
                "Canceling exit discarded in-memory models.");

            app.SnapshotWriter = _ => true;
            note.FontSize = double.NaN; // Serialization failure must also prevent shutdown.
            Check(!AwaitOnDispatcher(app.RequestExitAsync()) && !app.ExitCompleted && prompts == 2,
                "Serialization failure allowed exit.");
            note.FontSize = 14;

            int writes = 0;
            var snapshots = new System.Collections.Concurrent.ConcurrentQueue<string>();
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            app.SnapshotWriter = json =>
            {
                snapshots.Enqueue(json);
                int attempt = Interlocked.Increment(ref writes);
                if (attempt == 1)
                {
                    started.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                    return false;
                }
                return attempt > 2;
            };
            app.RetryDecision = () => { prompts++; return true; };
            var exit = app.RequestExitAsync();
            try
            {
                Check(started.Wait(TimeSpan.FromSeconds(10)), "Exit did not start saving.");
                Check(!AwaitOnDispatcher(app.RequestExitAsync()), "A second exit request was accepted while saving.");
                note.Text = "edited while saving";
                app.SaveAll();
            }
            finally { release.Set(); }
            Check(AwaitOnDispatcher(exit) && app.ExitCompleted, "Exit did not recover when saving became available.");
            var saved = StoreDataCodec.Parse(snapshots.Last());
            Check(AssertSingle(saved.Notes).Text == "edited while saving" && AssertSingle(saved.Notes).IsOpen,
                "Exit lost edits made during saving or the original open state.");
            Check(AssertSingle(saved.TaskLists).IsOpen && writes >= 3 && prompts == 3,
                "Retry did not save successfully or task list open state was lost.");
        }
        finally
        {
            foreach (var window in windows) window.Close();
            notes.Clear();
            lists.Clear();
        }
        Console.WriteLine("PASS: write/serialization failure cancels exit, preserves windows and models, and later saves include concurrent edits.");
    }

    private static T AssertSingle<T>(IEnumerable<T> items) => items.Single();

    private static T AwaitOnDispatcher<T>(Task<T> task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(
                new Action(() => frame.Continue = false)), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }

    private static void CheckPublishedApplication(string executable)
    {
        using var process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = System.IO.Path.GetDirectoryName(executable)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })
            ?? throw new Exception("Published application did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            // A tray/system-events thread can become idle before WPF's manager is ready.
            // Wait for the actual control instead of treating MainWindowHandle as readiness.
            AutomationElement? root = null;
            AutomationElement? create = null;
            var startup = Stopwatch.StartNew();
            while (startup.Elapsed < TimeSpan.FromSeconds(20))
            {
                Check(!process.HasExited, "Published application exited before displaying its manager.");
                var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));
                foreach (AutomationElement window in windows)
                {
                    try
                    {
                        var button = window.FindFirst(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.AutomationIdProperty, "AddNoteButton"));
                        if (button == null) continue;
                        root = window;
                        create = button;
                        break;
                    }
                    catch (ElementNotAvailableException) { }
                }
                if (create != null) break;
                Thread.Sleep(100);
            }
            Check(create != null, "Published manager's New button did not become available within 20 seconds.");
            var managerHandle = (IntPtr)root!.Current.NativeWindowHandle;
            ((InvokePattern)create!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            var deadline = Stopwatch.StartNew();
            bool noteOpened = false;
            while (deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));
                noteOpened = windows.Cast<AutomationElement>().Any(window => window.Current.Name == "便利贴");
                if (noteOpened) break;
                Thread.Sleep(100);
            }
            Check(noteOpened, "Published application did not create a note through its UI.");
            int shutdown = RegisterWindowMessage("StickyNoteWPF_Shutdown");
            PostMessage(managerHandle, shutdown, IntPtr.Zero, IntPtr.Zero);
            Check(process.WaitForExit(15000), "Published application did not exit and flush saves.");
            Check(process.ExitCode == 0, "Published application exited with an error.");
            string data = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StickyNoteWPF", "notes.json");
            using var saved = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(data));
            Check(saved.RootElement.GetProperty("Notes").GetArrayLength() > 0, "UI-created note was not persisted.");
        }
        catch (Exception ex)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            string stdout = output.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            string stderr = errors.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            string log = System.IO.File.Exists(Logger.LogPath)
                ? string.Join("\n", System.IO.File.ReadLines(Logger.LogPath).TakeLast(30)) : "No application log was written.";
            throw new InvalidOperationException($"Published smoke failed: {ex.Message}\nstdout: {stdout}\nstderr: {stderr}\nlog: {log}", ex);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        Console.WriteLine("PASS: published application starts, creates a note through UI automation and saves on exit.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
