using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using OnShow.App.Services;
using OnShow.Core;

namespace OnShow.App.Views;

/// <summary>
/// 反馈窗口:文字 + 截图 → GitHub Issue 预填(首图进剪贴板,Ctrl+V 粘贴);
/// 无 GitHub 账号走「导出反馈包」。上传范围严格限定为用户填写/选择的内容。
/// </summary>
public partial class FeedbackWindow : Window
{
    const int MaxImages = 3;

    public sealed record ShotItem(string SourcePath, BitmapSource Thumb);

    readonly ObservableCollection<ShotItem> _shots = new();

    public FeedbackWindow()
    {
        InitializeComponent();
        ShotList.ItemsSource = _shots;
        Loaded += (_, _) => DescBox.Focus();
    }

    void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    void OnCancel(object sender, RoutedEventArgs e) => Close();

    // ---------- 截图 ----------

    void OnAddShots(object sender, RoutedEventArgs e)
    {
        if (_shots.Count >= MaxImages)
        {
            Dialog.Warn(this, "添加截图", $"最多添加 {MaxImages} 张截图。");
            return;
        }
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择截图",
            Filter = "图片文件|*.png；*.jpg；*.jpeg；*.bmp；*.gif；*.webp",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        foreach (var path in dlg.FileNames)
        {
            if (_shots.Count >= MaxImages) { Dialog.Info(this, "添加截图", $"已达到 {MaxImages} 张上限,多余的未添加。"); break; }
            if (_shots.Any(s => string.Equals(s.SourcePath, path, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                _shots.Add(new ShotItem(path, LoadThumb(path)));
            }
            catch (Exception ex)
            {
                Log.Warn($"无法读取图片 {path}:{ex.Message}");
            }
        }
    }

    void OnRemoveShot(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ShotItem shot)
            _shots.Remove(shot);
    }

    static BitmapSource LoadThumb(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = 180;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // ---------- 提交 ----------

    async void OnSubmit(object sender, RoutedEventArgs e)
    {
        var text = DescBox.Text.Trim();
        if (text.Length == 0 && _shots.Count == 0)
        {
            Dialog.Warn(this, "反馈", "请先填写描述，或添加一张截图。");
            return;
        }

        var title = FeedbackComposer.ComposeTitle(text);
        var body = FeedbackComposer.ComposeBody(text, EnvBox.IsChecked == true ? BuildEnvBlock() : null);

        AddShotBtn.IsEnabled = false;
        try
        {
            // 截图复制到固定临时目录:命名稳定,便于在 GitHub 页面拖拽/粘贴
            var tempDir = Path.Combine(Path.GetTempPath(), "OnShowFeedback");
            try { Directory.Delete(tempDir, true); } catch { /* 尽力而为 */ }
            Directory.CreateDirectory(tempDir);
            var copied = new List<string>();
            for (var i = 0; i < _shots.Count; i++)
            {
                var dst = Path.Combine(tempDir, $"screenshot-{i + 1}{Path.GetExtension(_shots[i].SourcePath)}");
                File.Copy(_shots[i].SourcePath, dst, overwrite: true);
                copied.Add(dst);
            }

            // 剪贴板:完整文字(备用)+ 首张截图(PNG 流 + DIB 位图双格式,
            // 模拟系统截图工具的标准组合,浏览器/GitHub 粘贴最可靠)
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, title + "\n\n" + body);
            if (copied.Count > 0)
            {
                var pngStream = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(LoadBitmap(copied[0])));
                encoder.Save(pngStream);
                pngStream.Position = 0;
                data.SetData("PNG", pngStream);
                data.SetImage(LoadBitmap(copied[0]));
            }
            Clipboard.SetDataObject(data, true);

            var url = FeedbackComposer.BuildIssueUrl(AppMeta.IssuesNewUrl, title, body);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            var hint = "已复制反馈内容" + (copied.Count > 0 ? "，首张截图已放入剪贴板" : "") + ",浏览器将打开 GitHub。\n\n" +
                       "提交步骤：\n" +
                       "1. 确认标题与描述（不完整可按 Ctrl+V 粘贴完整文字）\n" +
                       (copied.Count > 0 ? "2. 在页面下方评论框内点一下，按 Ctrl+V 粘贴截图；其余截图可从 " + tempDir + " 拖入\n" : "") +
                       "3. 点击 Submit new issue 提交\n\n" +
                       "注意：截图不会自动上传，请在 issue 页面手动粘贴或拖拽上传。没有 GitHub 账号？改用「导出反馈包」，邮件发送时附上截图即可。";
            Dialog.Info(this, "反馈", hint);
            Log.Info("用户提交反馈（GitHub）");
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("提交反馈失败", ex);
            Dialog.Warn(this, "反馈", "提交失败：" + ex.Message);
        }
        finally
        {
            AddShotBtn.IsEnabled = true;
        }
    }

    // ---------- 导出反馈包(无 GitHub 账号的备用通道) ----------

    void OnExportPack(object sender, RoutedEventArgs e)
    {
        var text = DescBox.Text.Trim();
        if (text.Length == 0 && _shots.Count == 0)
        {
            Dialog.Warn(this, "导出反馈包", "请先填写描述，或添加一张截图。");
            return;
        }

        var title = FeedbackComposer.ComposeTitle(text);
        var body = FeedbackComposer.ComposeBody(text, EnvBox.IsChecked == true ? BuildEnvBlock() : null);

        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var zipPath = Path.Combine(desktop, $"OnShow反馈_{DateTime.Now:yyyyMMdd-HHmmss}.zip");

            var stage = Path.Combine(Path.GetTempPath(), "OnShowFeedbackPack");
            try { Directory.Delete(stage, true); } catch { /* 尽力而为 */ }
            Directory.CreateDirectory(stage);

            File.WriteAllText(Path.Combine(stage, "描述.txt"),
                title + "\n\n" + body, new System.Text.UTF8Encoding(true));
            for (var i = 0; i < _shots.Count; i++)
            {
                File.Copy(_shots[i].SourcePath,
                    Path.Combine(stage, $"screenshot-{i + 1}{Path.GetExtension(_shots[i].SourcePath)}"));
            }

            ZipFile.CreateFromDirectory(stage, zipPath);
            Directory.Delete(stage, true);

            Dialog.Info(this, "导出反馈包",
                $"反馈包已导出到:\n{zipPath}\n\n请通过邮件发送至 {AppMeta.ContactEmail},并在邮件中说明问题。");
            try { Process.Start("explorer.exe", $"/select,\"{zipPath}\""); } catch { /* 尽力而为 */ }
            Log.Info($"导出反馈包:{zipPath}");
        }
        catch (Exception ex)
        {
            Log.Error("导出反馈包失败", ex);
            Dialog.Warn(this, "导出反馈包", "导出失败：" + ex.Message);
        }
    }

    // ---------- 环境信息 ----------

    static string BuildEnvBlock()
    {
        var os = Environment.OSVersion;
        var arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
        return $"- OnShow 版本:{AppMeta.CurrentVersionString}\n" +
               $"- 操作系统:{os.VersionString}({arch})\n" +
               $"- 运行时:.NET {Environment.Version}";
    }

    static BitmapSource LoadBitmap(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
