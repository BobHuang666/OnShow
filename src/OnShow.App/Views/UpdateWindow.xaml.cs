using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using OnShow.App.Services;
using OnShow.Core;

namespace OnShow.App.Views;

/// <summary>发现新版本的更新窗口:展示更新日志,一键下载(SHA256 校验)并自动重启安装。</summary>
public partial class UpdateWindow : Window
{
    readonly GitHubReleaseInfo _release;
    readonly CancellationTokenSource _cts = new();
    bool _busy;

    public UpdateWindow(GitHubReleaseInfo release)
    {
        InitializeComponent();
        _release = release;
        VersionLine.Text = $"当前 v{AppMeta.CurrentVersionString}  →  新版本 {release.TagName.TrimStart('v')}";
        NotesViewer.Document = MdLite.Render(string.IsNullOrWhiteSpace(release.Body)
            ? "该版本未提供更新说明。"
            : release.Body);
        Closed += (_, _) => _cts.Cancel();
    }

    void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !_busy) DragMove();
    }

    void OnLater(object sender, RoutedEventArgs e) => Close();

    void OnSkip(object sender, RoutedEventArgs e)
    {
        var config = App.Instance.Config;
        config.SkippedVersion = _release.TagName.TrimStart('v', 'V');
        App.Instance.SaveConfig();
        Log.Info($"跳过更新版本 {config.SkippedVersion}");
        Close();
    }

    async void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        UpdateBtn.IsEnabled = false;
        SkipBtn.IsEnabled = false;
        CloseBtn.IsEnabled = false;

        var progress = new Progress<UpdateProgress>(p =>
        {
            Progress.Value = p.Percent;
            StatusText.Text = p.Status;
        });

        try
        {
            var mirror = App.Instance.Config.DownloadMirrorPrefix;
            var newExe = await Task.Run(() =>
                UpdateService.DownloadAndPrepareAsync(_release, progress, _cts.Token, mirror));

            StatusText.Text = "校验通过,即将重启并安装新版本…";
            await Task.Delay(500);
            UpdateService.LaunchApply(newExe);
            App.Instance.ExitApp();
        }
        catch (OperationCanceledException)
        {
            // 窗口已关闭
        }
        catch (Exception ex)
        {
            Log.Error("应用内更新失败", ex);
            StatusText.Text = "";
            Progress.Value = 0;
            Dialog.Warn(this, "更新失败", ex.Message);
            UpdateBtn.IsEnabled = true;
            SkipBtn.IsEnabled = true;
            CloseBtn.IsEnabled = true;
        }
        finally
        {
            _busy = false;
        }
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_busy) e.Cancel = true;   // 下载进行中禁止关闭,避免半途文件残留引发困惑
    }
}
