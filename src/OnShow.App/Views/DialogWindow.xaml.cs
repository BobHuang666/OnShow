using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OnShow.App.Views;

/// <summary>主题化提示/确认对话框,替代系统 MessageBox(深色、圆角、与全局风格一致)。</summary>
public partial class DialogWindow : Window
{
    public bool Confirmed { get; private set; }

    public DialogWindow()
    {
        InitializeComponent();
    }

    /// <summary>kind:info / success / warn / error / question</summary>
    public void Configure(string kind, string title, string message, bool showCancel)
    {
        var (glyph, color) = kind switch
        {
            "success" => ("✓", Color.FromRgb(0x7B, 0xC6, 0x7E)),
            "warn" => ("!", Color.FromRgb(0xFD, 0xD6, 0x63)),
            "error" => ("✕", Color.FromRgb(0xFF, 0x6B, 0x6B)),
            "question" => ("?", Color.FromRgb(0x8A, 0xB4, 0xF8)),
            _ => ("ℹ", Color.FromRgb(0x8A, 0xB4, 0xF8)),
        };

        IconGlyph.Text = glyph;
        IconGlyph.Foreground = new SolidColorBrush(color);
        IconBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B));
        TitleText.Text = title;
        MessageText.Text = message;
        CancelBtn.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        OkBtn.Content = showCancel ? "确定" : "知道了";
    }

    void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    void OnOk(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    void OnCancel(object sender, RoutedEventArgs e) => Close();
}

/// <summary>对话框便捷入口。kind:info / success / warn / error / question。</summary>
public static class Dialog
{
    public static void Info(Window? owner, string title, string message, string kind = "info")
        => Show(owner, kind, title, message, showCancel: false);

    public static void Warn(Window? owner, string title, string message)
        => Show(owner, "warn", title, message, showCancel: false);

    public static bool Confirm(Window? owner, string title, string message, string kind = "question")
        => Show(owner, kind, title, message, showCancel: true);

    static bool Show(Window? owner, string kind, string title, string message, bool showCancel)
    {
        var dialog = new DialogWindow
        {
            Owner = owner!,
        };
        if (owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        dialog.Configure(kind, title, message, showCancel);
        dialog.ShowDialog();
        return dialog.Confirmed;
    }
}
