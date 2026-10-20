using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
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
        protected override void OnStartup(StartupEventArgs e) { }
        protected override void OnExit(ExitEventArgs e) { }
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

    private static void CheckPublishedApplication(string executable)
    {
        using var process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false })
            ?? throw new Exception("Published application did not start.");
        try
        {
            Check(process.WaitForInputIdle(20000), "Published application did not finish initialization.");
            process.Refresh();
            Check(!process.HasExited && process.MainWindowHandle != IntPtr.Zero, "Published manager window is missing.");
            var root = AutomationElement.FromHandle(process.MainWindowHandle);
            var create = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "AddNoteButton"));
            Check(create != null, "Published manager's New button is missing.");
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
            PostMessage(process.MainWindowHandle, shutdown, IntPtr.Zero, IntPtr.Zero);
            Check(process.WaitForExit(15000), "Published application did not exit and flush saves.");
            Check(process.ExitCode == 0, "Published application exited with an error.");
            string data = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StickyNoteWPF", "notes.json");
            using var saved = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(data));
            Check(saved.RootElement.GetProperty("Notes").GetArrayLength() > 0, "UI-created note was not persisted.");
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
