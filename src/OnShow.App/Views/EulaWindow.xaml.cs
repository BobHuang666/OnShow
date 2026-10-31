using System.Windows;
using System.Windows.Input;
using OnShow.App.Services;

namespace OnShow.App.Views;

/// <summary>首次启动的用户协议窗口:必须勾选同意才能进入主界面;关闭窗口视为拒绝。</summary>
public partial class EulaWindow : Window
{
    public EulaWindow()
    {
        InitializeComponent();
        DocViewer.Document = Docs.AgreementCombined();
    }

    void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    void OnAgreeChanged(object sender, RoutedEventArgs e) => AcceptBtn.IsEnabled = AgreeBox.IsChecked == true;

    void OnAccept(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    void OnDecline(object sender, RoutedEventArgs e) => Close();
}
