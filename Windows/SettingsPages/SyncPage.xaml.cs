using System.Windows.Controls;
using System.Windows;
using StickyNoteWPF.Services;
using StickyNoteWPF.Sync;

namespace StickyNoteWPF;

public partial class SyncPage : System.Windows.Controls.UserControl
{
    private SyncPreferences _preferences = SyncPreferences.Load();
    private bool _busy;
    public bool IsBusy => _busy;
    public SyncPage()
    {
        InitializeComponent();
        ServerBox.Text = _preferences.ServerUrl;
        KeyBox.Password = _preferences.ServerKey;
        UsernameBox.Text = _preferences.Username;

        UpdateStatus();
    }

    private void UpdateStatus(string? message = null)
    {
        var loggedIn = !string.IsNullOrEmpty(_preferences.Token) && _preferences.ExpiresAt > DateTimeOffset.UtcNow;
        SyncButton.IsEnabled = loggedIn;
        LogoutButton.IsEnabled = !string.IsNullOrEmpty(_preferences.ProtectedToken);
        StatusText.Text = message ?? (loggedIn
            ? $"已登录：{_preferences.Username}\n服务器：{_preferences.ServerUrl}\n上次同步：{(_preferences.LastSyncAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "尚未同步") }"
            : "未登录或登录已过期。本地数据可继续使用。");
    }

    private async void Login_Click(object sender, RoutedEventArgs e) => await LoginAsync(false);
    private async void Register_Click(object sender, RoutedEventArgs e) => await LoginAsync(true);
    private async Task LoginAsync(bool register) => await RunAsync(async () =>
    {
        var url = SyncClient.NormalizeUrl(ServerBox.Text);
        using var client = new SyncClient(url, KeyBox.Password);
        var login = await client.LoginAsync(UsernameBox.Text.Trim(), PasswordInput.Password, register);
        var next = new SyncPreferences
        {
            ServerUrl = url, Username = login.Username, UserId = login.UserId,
            ServerKey = KeyBox.Password, Token = login.Token, ExpiresAt = login.ExpiresAt
        };
        if (_preferences.ServerUrl == url && _preferences.UserId == login.UserId)
        {
            next.Revision = _preferences.Revision;
            next.BaselineHash = _preferences.BaselineHash;
            next.LastSyncAt = _preferences.LastSyncAt;
        }
        next.Save();
        _preferences = next;
        PasswordInput.Clear();
        UpdateStatus();
    });

    private async void Sync_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (SyncClient.NormalizeUrl(ServerBox.Text) != _preferences.ServerUrl
            || UsernameBox.Text.Trim() != _preferences.Username || KeyBox.Password != _preferences.ServerKey)
            throw new InvalidOperationException("连接信息已修改，请先登录该服务器，再同步。");
        using var client = new SyncClient(_preferences.ServerUrl, _preferences.ServerKey, _preferences.Token);
        var local = SnapshotTools.ForSync(App.Current.CaptureSyncData());
        var localHash = SnapshotTools.Hash(local);
        ISyncTransport transport = client;
        var received = await transport.ReadAsync();
        var remote = new RemoteSnapshot(long.Parse(received.Version, System.Globalization.CultureInfo.InvariantCulture), received.Data);
        var remoteHash = SnapshotTools.Hash(remote.Data);
        long revision = remote.Revision;
        string syncedHash;
        if (localHash == remoteHash) syncedHash = localHash;
        else
        {
            var decision = SyncDecision.Choose(_preferences.Revision, _preferences.BaselineHash,
                localHash, remoteHash, remote.Revision, SnapshotTools.IsEmpty(local));
            if (decision == SyncDirection.Conflict)
            {
                var answer = System.Windows.MessageBox.Show(Window.GetWindow(this)!,
                    $"本机有 {local.Notes.Count} 张便签、{local.TaskLists.Count} 个清单；服务器有 {remote.Data.Notes.Count} 张便签、{remote.Data.TaskLists.Count} 个清单。\n\n首次关联或两端都已修改，需要选择保留哪份完整数据（不会合并）：\n\n“是”：用本机数据替换服务器。\n“否”：用服务器数据替换本机，替换前自动备份。\n“取消”：保持两端原样。",
                    "选择同步数据", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                if (answer == MessageBoxResult.Cancel) { UpdateStatus("已取消同步，两端数据未替换。"); return; }
                decision = answer == MessageBoxResult.Yes ? SyncDirection.Upload : SyncDirection.Download;
            }
            // 下载请求等待期间，本机其他窗口可能仍在编辑。
            if (SnapshotTools.Hash(App.Current.CaptureSyncData()) != localHash)
                throw new InvalidOperationException("同步期间本机数据已修改，请重新同步。");
            if (decision == SyncDirection.Upload)
            {
                revision = long.Parse(await transport.WriteAsync(local, received.Version), System.Globalization.CultureInfo.InvariantCulture);
                syncedHash = localHash;
            }
            else
            {
                App.Current.ApplySyncData(remote.Data);
                syncedHash = remoteHash;
            }
        }
        _preferences.Revision = revision;
        _preferences.BaselineHash = syncedHash;
        _preferences.LastSyncAt = DateTimeOffset.UtcNow;
        _preferences.Save();
        UpdateStatus();
    });

    private async void Logout_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        string? warning = null;
        try
        {
            using var client = new SyncClient(_preferences.ServerUrl, _preferences.ServerKey, _preferences.Token);
            await client.LogoutAsync();
        }
        catch { warning = "已清除本机登录。服务器暂不可达，远端会话将在到期后失效。"; }
        _preferences.Token = "";
        _preferences.ExpiresAt = default;
        _preferences.Save();
        PasswordInput.Clear();
        UpdateStatus(warning);
    });

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        ConnectionPanel.IsEnabled = ActionPanel.IsEnabled = false;
        StatusText.Text = "正在连接服务器…";
        try { await action(); }
        catch (Exception ex)
        {
            UpdateStatus(ex is TaskCanceledException ? "连接超时，请检查服务器地址与网络。" : ex.Message);
        }
        finally { _busy = false; ConnectionPanel.IsEnabled = ActionPanel.IsEnabled = true; }
    }
}
