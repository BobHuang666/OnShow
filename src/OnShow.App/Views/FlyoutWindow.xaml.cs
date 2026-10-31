using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using OnShow.App.Interop;
using OnShow.App.ViewModels;

namespace OnShow.App.Views;

/// <summary>托盘悬停快览:跟随托盘图标弹出,鼠标移开自动关闭;不抢焦点。</summary>
public partial class FlyoutWindow : Window
{
    readonly GlanceVm _vm = new();
    readonly DispatcherTimer _closeTimer;
    Rect _iconRectPhysical;
    Rect _anchorPhysical;
    int _outsideStreak;

    public FlyoutWindow()
    {
        InitializeComponent();
        Glance.DataContext = _vm;
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _closeTimer.Tick += (_, _) => PollClose();
    }

    internal void ShowNear(NativeMethods.RECT iconRect)
    {
        _iconRectPhysical = new Rect(iconRect.Left, iconRect.Top,
            iconRect.Right - iconRect.Left, iconRect.Bottom - iconRect.Top);

        // 光标锚点:Win11 的 Shell_NotifyIconGetRect 偶发返回垃圾矩形,
        // 收起判定与兜底定位都以「触发悬停时的光标位置」为准,不依赖图标矩形。
        NativeMethods.GetCursorPos(out var cp);
        _anchorPhysical = new Rect(cp.X - 14, cp.Y - 14, 28, 28);

        var anchorRect = _iconRectPhysical;
        if (anchorRect.Width < 6 || anchorRect.Height < 6 || anchorRect.Width > 120 || anchorRect.Height > 120)
            anchorRect = new Rect(cp.X - 11, cp.Y - 11, 22, 22);   // 图标矩形可疑,用光标兜底定位

        // 物理像素 → 逻辑坐标(DIP)
        var m = Matrix.Identity;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
            m = source.CompositionTarget.TransformFromDevice;
        var logical = Rect.Transform(anchorRect, m);

        var work = SystemParameters.WorkArea;
        var left = logical.Left + logical.Width / 2 - Width / 2;
        var top = logical.Top - ActualHeight - 8;

        left = Math.Min(Math.Max(left, work.Left + 8), work.Right - Width - 8);
        if (top < work.Top + 8) top = logical.Bottom + 8;   // 图标在顶部(任务栏上置)时向下弹

        _vm.Update(App.Instance.DdlCard, App.Instance.TokenCard);

        if (!IsVisible)
        {
            Left = left;
            Top = top;
            Show();
        }
        else if (Math.Abs(Left - left) > 4 || Math.Abs(Top - top) > 4)
        {
            // 图标矩形测量抖动会导致窗口跳动;仅在偏移明显时才跟随
            Left = left;
            Top = top;
        }
        _closeTimer.Start();
    }

    /// <summary>立即收起快览(打开托盘菜单前调用)。</summary>
    public void Dismiss()
    {
        _closeTimer.Stop();
        Hide();
    }

    void PollClose()
    {
        if (!NativeMethods.GetCursorPos(out var pt)) return;
        var cursor = new Point(pt.X, pt.Y);

        var m = Matrix.Identity;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
            m = source.CompositionTarget.TransformFromDevice;

        var flyout = new Rect(Left, Top, ActualWidth, ActualHeight);
        flyout.Transform(m);
        var anchor = Rect.Transform(_anchorPhysical, m);

        // 锚点 ∪ 快览 整体外扩 10 逻辑像素;不使用图标矩形,规避系统返回垃圾值导致的闪关
        var keep = Rect.Union(flyout, anchor);
        keep.Inflate(10, 10);
        if (keep.Contains(cursor))
        {
            _outsideStreak = 0;
            return;
        }

        // 连续两轮(约 400ms)都在外面才收起,过滤瞬时划出的抖动
        if (++_outsideStreak < 2) return;
        _outsideStreak = 0;
        Dismiss();
    }
}
