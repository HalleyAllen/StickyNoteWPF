using System.Windows.Controls;
using System.Net;
using System.Windows;
using System.Windows.Threading;
using StickyNoteWPF.Sync;

namespace StickyNoteWPF;

public partial class PeerSyncPage : System.Windows.Controls.UserControl
{
    private PeerSyncHost? _host;
    private bool _busy, _receiving, _stopping;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public PeerSyncPage()
    {
        InitializeComponent();
        AddressBox.ItemsSource = PeerInvitation.LocalAddresses();
        AddressBox.SelectedIndex = 0;
        _timer.Tick += async (_, _) =>
        {
            if (_host is null || _stopping) return;
            if (!_host.IsActive) { await StopHostAsync(); HostStatus.Text = "配对已过期，接收已停止。"; }
            else HostStatus.Text = $"正在接收，配对码有效至 {_host.Invitation.ExpiresAt.ToLocalTime():HH:mm:ss}";
        };
    }

    public bool IsBusy => _busy || _receiving || _stopping;
    public Task ClosePageAsync() => StopHostAsync();

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(PortBox.Text, out var port) || port is < 1024 or > 65535)
                throw new InvalidOperationException("请输入 1024–65535 之间的端口。");
            if (AddressBox.SelectedItem is not string address) throw new InvalidOperationException("请选择本机网络地址。");
            _host = new PeerSyncHost(IPAddress.Parse(address), port,
                ct => Dispatcher.InvokeAsync(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    return PeerWire.Snapshot(App.Current.CaptureSyncData());
                }, DispatcherPriority.Normal, ct).Task,
                (data, expected, ct) => Dispatcher.InvokeAsync(() => Receive(data, expected, ct), DispatcherPriority.Normal, ct).Task);
            InvitationBox.Text = _host.Invitation.Encode();
            HostSetup.IsEnabled = ConnectPanel.IsEnabled = false;
            CopyButton.IsEnabled = StopButton.IsEnabled = true;
            HostStatus.Text = "正在接收，请把配对码传给另一台设备。";
            _timer.Start();
        }
        catch (Exception ex) { StatusText.Text = "无法开启接收：" + ex.Message; }
    }

    private TransportSnapshot Receive(SyncData data, string expected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_receiving || _stopping) throw new PeerSyncException("本机正在处理另一次同步，请稍后再试。");
        _receiving = true;
        try
        {
            if (SnapshotTools.Hash(App.Current.CaptureSyncData()) != expected)
                throw new PeerSyncException("接收设备的数据已变化，请重新读取后再同步。");
            var answer = System.Windows.MessageBox.Show(Window.GetWindow(this)!,
                $"已配对设备请求用 {data.Notes.Count} 张便签、{data.TaskLists.Count} 个清单替换本机完整同步数据。\n\n替换前会备份本机数据。是否接收？",
                "接收设备数据", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            ct.ThrowIfCancellationRequested();
            if (answer != MessageBoxResult.Yes) throw new PeerSyncException("对方取消了接收，本次同步未执行。");
            if (SnapshotTools.Hash(App.Current.CaptureSyncData()) != expected)
                throw new PeerSyncException("确认期间本机数据已变化，请重新同步。");
            App.Current.ApplySyncData(data);
            StatusText.Text = "已接收另一台设备的数据，本机原数据已备份。";
            return PeerWire.Snapshot(App.Current.CaptureSyncData());
        }
        finally { _receiving = false; }
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _host is not null) return;
        _busy = true;
        HostSetup.IsEnabled = ConnectPanel.IsEnabled = false;
        StatusText.Text = "正在连接对方设备…";
        try
        {
            using ISyncTransport transport = new PeerSyncClient(PairingBox.Text);
            var local = PeerWire.Snapshot(App.Current.CaptureSyncData());
            var remote = await transport.ReadAsync();
            if (SnapshotTools.Hash(App.Current.CaptureSyncData()) != local.Version)
                throw new PeerSyncException("连接期间本机内容已修改，请重新同步。");
            if (local.Version == remote.Version) { StatusText.Text = "两台设备的数据已经一致。"; return; }
            var answer = System.Windows.MessageBox.Show(Window.GetWindow(this)!,
                $"本机：{local.Data.Notes.Count} 张便签、{local.Data.TaskLists.Count} 个清单。\n对方：{remote.Data.Notes.Count} 张便签、{remote.Data.TaskLists.Count} 个清单。\n\n请选择保留哪一端的完整同步数据（不会合并）：\n\n“是”：发送本机数据，对方确认后替换。\n“否”：接收对方数据，备份后替换本机。\n“取消”：不做修改。",
                "选择设备同步方向", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (answer == MessageBoxResult.Cancel) { StatusText.Text = "已取消，两台设备的数据未替换。"; return; }
            if (SnapshotTools.Hash(App.Current.CaptureSyncData()) != local.Version)
                throw new PeerSyncException("选择期间本机数据已变化，请重新同步。");
            if (answer == MessageBoxResult.Yes)
            {
                StatusText.Text = "等待对方确认接收，请在另一台电脑上确认…";
                await transport.WriteAsync(local.Data, remote.Version);
                StatusText.Text = "对方已确认接收本次发送的数据。";
            }
            else
            {
                App.Current.ApplySyncData(remote.Data);
                StatusText.Text = "已接收对方的数据，本机原数据已备份。";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex is OperationCanceledException
                ? "连接或等待确认超时，请重新连接并检查对方数据。"
                : "同步未确认完成：" + ex.Message;
        }
        finally { _busy = false; HostSetup.IsEnabled = ConnectPanel.IsEnabled = true; }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { System.Windows.Clipboard.SetText(InvitationBox.Text); StatusText.Text = "已复制配对码。"; }
        catch (Exception ex) { StatusText.Text = "复制失败，请手动选择配对码复制：" + ex.Message; }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopHostAsync();
    private async Task StopHostAsync()
    {
        if (_stopping) return;
        _stopping = true;
        _timer.Stop();
        StopButton.IsEnabled = CopyButton.IsEnabled = false;
        try
        {
            if (_host is not null) await _host.DisposeAsync();
        }
        finally
        {
            _host = null; _stopping = false;
            InvitationBox.Clear();
            HostSetup.IsEnabled = ConnectPanel.IsEnabled = true;
            HostStatus.Text = "接收已停止，旧配对码已失效。";
        }
    }
}
