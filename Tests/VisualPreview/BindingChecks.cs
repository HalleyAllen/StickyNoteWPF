using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using StickyNoteWPF;

internal sealed class BindingChecks : TraceListener
{
    private readonly SourceLevels _previousLevel;
    private readonly StringBuilder _line = new();
    private readonly List<string> _errors = new();

    public BindingChecks()
    {
        _previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(this);
    }

    public override void Write(string? message) => _line.Append(message);
    public override void WriteLine(string? message)
    {
        _line.Append(message);
        _errors.Add(_line.ToString());
        _line.Clear();
    }

    public static void CheckAutomationNames()
    {
        foreach (var key in new[] { "UiIconButton", "UiToolbarButton", "UiDeleteButton" })
        {
            // 复现 XAML 先设置 Style、稍后才赋 ToolTip 的初始化顺序。
            var button = new Button { Style = (System.Windows.Style)App.Current.FindResource(key) };
            Check(string.Empty);
            button.ToolTip = "排序方式：默认排序";
            Check("排序方式：默认排序");
            button.ToolTip = "排序方式：按标题 A→Z";
            Check("排序方式：按标题 A→Z");
            button.ToolTip = null;
            Check(string.Empty);
            AutomationProperties.SetName(button, "明确指定的名称");
            Check("明确指定的名称");

            void Check(string expected)
            {
                button.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                if (AutomationProperties.GetName(button) != expected)
                    throw new InvalidOperationException("图标按钮的辅助功能名称未正确跟随提示或显式名称：" + key);
            }
        }
        Console.WriteLine("PASS: automation names handle unset/null tooltips, dynamic changes and explicit names.");
    }

    public void AssertClean()
    {
        App.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (_errors.Count != 0)
            throw new InvalidOperationException($"检测到 {_errors.Count} 个 WPF 绑定错误：\n" + string.Join("\n", _errors.Take(8)));
        Console.WriteLine("PASS: no WPF binding errors during window and page lifecycle checks.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
            PresentationTraceSources.DataBindingSource.Switch.Level = _previousLevel;
        }
        base.Dispose(disposing);
    }
}
