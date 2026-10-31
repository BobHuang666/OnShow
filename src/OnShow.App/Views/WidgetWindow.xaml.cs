using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using OnShow.App.Interop;
using OnShow.App.Services;
using OnShow.Cards.Ddl;
using OnShow.Cards.Token;
using OnShow.Core;

namespace OnShow.App.Views;

/// <summary>
/// 桌面 Widget 主窗口:
/// 无边框 + 不抢焦点;DesktopTop 模式 z 序贴底(WM_WINDOWPOSCHANGING 事件钳制)
/// 并在 Win+D 后自动还原;整块空白区可拖动(可锁定)。
/// </summary>
public partial class WidgetWindow : Window
{
    readonly DdlCard _ddl;
    readonly TokenCard _token;
    readonly AppConfig _config;
    readonly Action<string?> _openSettings;

    HwndSource? _src;
    bool _dragging;
    bool _userHidden;
    bool _allowClose;

    readonly DispatcherTimer _saveTimer;

    /// <summary>SourceInitialized 完成后直接进托盘(--minimized / 上次退出时隐藏)。</summary>
    public bool StartHidden { get; set; }

    public WidgetWindow(DdlCard ddl, TokenCard token, AppConfig config, Action<string?> openSettings)
    {
        InitializeComponent();
        _ddl = ddl;
        _token = token;
        _config = config;
        _openSettings = openSettings;

        WidgetTheme.Apply(this, _config.WidgetBackground);
        RootBorder.Opacity = Math.Clamp(config.Window.Opacity, 0.5, 1);

        _ddl.ViewModelChanged += RebuildDdl;
        _token.ViewModelChanged += RebuildToken;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SavePosition();
        };
        LocationChanged += (_, _) =>
        {
            if (IsLoaded)
            {
                _saveTimer.Stop();
                _saveTimer.Start();
            }
        };
    }

    // ---------- 生命周期 ----------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _src = HwndSource.FromHwnd(PinService.Handle(this));
        _src?.AddHook(WndProc);

        PinService.ApplyMode(this, _config.Window.Mode);

        RestorePosition();
        RebuildDdl();
        RebuildToken();

        if (StartHidden)
        {
            _userHidden = true;
            _config.Window.Visible = false;
            Hide();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideToTray();
        }
        base.OnClosing(e);
    }

    // ---------- 展示 / 隐藏(托盘调用) ----------

    public bool UserHidden => _userHidden;

    public void ShowWidget()
    {
        _userHidden = false;
        _config.Window.Visible = true;
        Show();
        PinService.ApplyMode(this, _config.Window.Mode);
        if (_config.Window.Mode != "AlwaysTop") PinService.PushToBottom(this);
    }

    public void HideToTray()
    {
        _userHidden = true;
        _config.Window.Visible = false;
        Hide();
        App.Instance.SaveConfig();
    }

    public void ApplyModeChange() => PinService.ApplyMode(this, _config.Window.Mode);

    public void ApplyOpacity() => RootBorder.Opacity = Math.Clamp(_config.Window.Opacity, 0.5, 1);

    public void ApplyBackground() => WidgetTheme.Apply(this, _config.WidgetBackground);

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    // ---------- 列表重建 ----------

    void RebuildDdl()
    {
        DdlList.ItemsSource = _ddl.Items;
        DdlSectionTitle.Text = $"⏰ DDL 倒计时 · {_ddl.ActiveCount} 项";
        DdlEmpty.Visibility = _ddl.ActiveCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void RebuildToken()
    {
        TokenList.ItemsSource = _token.Rows;
        TokenEmpty.Visibility = _token.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- WndProc:z 序钳制 / Win+D 还原 ----------

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            switch (msg)
            {
                case NativeMethods.WM_WINDOWPOSCHANGING:
                    // DesktopTop 模式:任何试图抬升本窗口的操作都被钳回最底层,平时零调用、不触发重绘
                    if (_config.Window.Mode == "DesktopTop" && !_dragging && !_userHidden && IsVisible)
                    {
                        var wp = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
                        if (wp.hwndInsertAfter != NativeMethods.HWND_BOTTOM
                            && (wp.flags & NativeMethods.SWP_NOZORDER) == 0)
                        {
                            wp.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
                            Marshal.StructureToPtr(wp, lParam, false);
                        }
                    }
                    break;

                case NativeMethods.WM_SIZE:
                    if ((int)wParam == NativeMethods.SIZE_MINIMIZED
                        && _config.Window.Mode != "AlwaysTop"
                        && !_userHidden)
                        ScheduleRestoreAfterShowDesktop();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Widget WndProc 异常", ex);
        }
        return IntPtr.Zero;
    }

    void ScheduleRestoreAfterShowDesktop()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (_userHidden || _config.Window.Mode == "AlwaysTop") return;
            var h = PinService.Handle(this);
            if (NativeMethods.IsIconic(h))
            {
                NativeMethods.ShowWindow(h, NativeMethods.SW_SHOWNOACTIVATE);
                PinService.PushToBottom(this);
            }
        };
        t.Start();
    }

    // ---------- 位置记忆 ----------

    void RestorePosition()
    {
        var cfg = _config.Window;
        if (double.IsNaN(cfg.X) || double.IsNaN(cfg.Y))
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - Width - 16;
            Top = work.Bottom - Height - 16;
        }
        else
        {
            Left = cfg.X;
            Top = cfg.Y;
        }
        ClampIntoVirtualScreen();
    }

    void ClampIntoVirtualScreen()
    {
        var vx = SystemParameters.VirtualScreenLeft;
        var vy = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        Left = Math.Min(Math.Max(Left, vx - Width + 80), vx + vw - 80);
        Top = Math.Min(Math.Max(Top, vy), vy + vh - 40);
    }

    void SavePosition()
    {
        _config.Window.X = Left;
        _config.Window.Y = Top;
        App.Instance.SaveConfig();
    }

    // ---------- 交互 ----------

    void OnBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        App.Instance.CloseTrayMenu();
        if (_config.Window.Locked) return;
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is Control) return;               // 控件交给自身
        if (e.OriginalSource is FrameworkElement fe && fe.Tag as string == "btn") return;

        _dragging = true;
        try
        {
            PinService.DragWithoutActivate(this);              // 模态拖动,返回即结束
        }
        finally
        {
            _dragging = false;
            if (_config.Window.Mode != "AlwaysTop") PinService.PushToBottom(this);
        }
        e.Handled = true;
    }

    void OnAddDdlClick(object sender, MouseButtonEventArgs e)
    {
        App.Instance.CloseTrayMenu();
        new DdlEditWindow(_ddl, null) { Owner = this }.ShowDialog();
    }

    void OnRefreshClick(object sender, MouseButtonEventArgs e)
    {
        App.Instance.CloseTrayMenu();
        _ = _token.RefreshAsync();
    }

    void OnSettingsClick(object sender, MouseButtonEventArgs e)
    {
        App.Instance.CloseTrayMenu();
        _openSettings(null);
    }

    void OnHideClick(object sender, MouseButtonEventArgs e)
    {
        App.Instance.CloseTrayMenu();
        HideToTray();
    }

    void OnDdlRowClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        App.Instance.CloseTrayMenu();
        if ((sender as FrameworkElement)?.Tag is not string id) return;
        var item = _ddl.GetItem(id);
        if (item is null) return;
        new DdlEditWindow(_ddl, item) { Owner = this }.ShowDialog();
    }

    void OnTokenRowClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        App.Instance.CloseTrayMenu();
        if ((sender as FrameworkElement)?.Tag is not string id) return;
        var row = _token.Rows.FirstOrDefault(r => r.Account.Id == id);
        if (row is null) return;

        // 跳转该服务商的控制台网页(可看额度/费用);无网页的账户回退设置页
        if (!string.IsNullOrWhiteSpace(row.ConsoleUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = row.ConsoleUrl, UseShellExecute = true });
                return;
            }
            catch (Exception ex)
            {
                Log.Error("打开控制台网页失败", ex);
            }
        }
        _openSettings("token");
    }
}
