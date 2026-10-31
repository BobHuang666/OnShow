using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OnShow.App.Services;

namespace OnShow.App.Views;

/// <summary>文档窗口:更新日志、用户协议与隐私声明(内嵌文档离线可读)。项目信息在 设置 → 关于。</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        ((FlowDocumentScrollViewer)((Border)FindName("PageChangelog")).Child).Document = Docs.ChangeLog();
        ((FlowDocumentScrollViewer)((Border)FindName("PageAgreement")).Child).Document = Docs.UserAgreement();
        ((FlowDocumentScrollViewer)((Border)FindName("PagePrivacy")).Child).Document = Docs.PrivacyStatement();
    }

    void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (NavList.SelectedItem is not ListBoxItem item) return;
        var idx = NavList.ItemContainerGenerator.IndexFromContainer(item);
        PageChangelog.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageAgreement.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PagePrivacy.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
    }
}
