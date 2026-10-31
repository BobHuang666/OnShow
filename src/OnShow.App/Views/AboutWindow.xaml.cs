using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OnShow.App.Services;
using OnShow.Core;

namespace OnShow.App.Views;

/// <summary>关于窗口:版本信息、更新日志、用户协议与隐私声明(内嵌文档离线可读)。</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = "v" + AppMeta.CurrentVersionString;
        ((FlowDocumentScrollViewer)((Border)FindName("PageChangelog")).Child).Document = Docs.ChangeLog();
        ((FlowDocumentScrollViewer)((Border)FindName("PageAgreement")).Child).Document = Docs.UserAgreement();
        ((FlowDocumentScrollViewer)((Border)FindName("PagePrivacy")).Child).Document = Docs.PrivacyStatement();
    }

    void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (NavList.SelectedItem is not ListBoxItem item) return;
        var idx = NavList.ItemContainerGenerator.IndexFromContainer(item);
        PageAbout.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageChangelog.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageAgreement.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        PagePrivacy.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnCheckUpdate(object sender, RoutedEventArgs e) => App.Instance.CheckForUpdateInteractive(this);

    void OnFeedback(object sender, RoutedEventArgs e) => App.Instance.OpenFeedback(this);

    void OnOpenRepo(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppMeta.RepoUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("打开项目主页失败", ex);
        }
    }
}
