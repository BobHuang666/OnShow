using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using OnShow.Core;
using SD = System.Drawing;

namespace OnShow.App.Interop;

/// <summary>
/// 托盘图标(手写 Shell_NotifyIcon):
/// 左键切换 Widget,双击开设置,右键菜单,悬停 350ms 触发快览,气泡通知承载 DDL/Token 提醒。
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    public event Action? LeftClicked;
    public event Action? DoubleClicked;
    public event Action? MenuRequested;
    public event Action? HoverStarted;

    const uint Uid = 1;

    readonly HwndSource _src;
    readonly DispatcherTimer _hoverTimer;
    readonly Dispatcher _uiDispatcher;
    DispatcherTimer? _balloonTimer;
    IntPtr _icon;

    public TrayIconService()
    {
        _uiDispatcher = Dispatcher.CurrentDispatcher;
        var p = new HwndSourceParameters("OnShowTrayHidden")
        {
            Width = 1,
            Height = 1,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = 0,
        };
        _src = new HwndSource(p);
        NativeMethods.SetParent(_src.Handle, NativeMethods.HWND_MESSAGE);   // 消息专用窗口
        _src.AddHook(WndProc);

        _icon = CreateAppIcon();
        var nid = NewData();
        nid.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
        nid.uCallbackMessage = NativeMethods.WM_APP_TRAY;
        nid.hIcon = _icon;
        nid.szTip = "OnShow";
        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref nid))
            Log.Error("托盘图标注册失败");

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            HoverStarted?.Invoke();
        };
    }

    public IntPtr TrayHwnd => _src.Handle;

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != (int)NativeMethods.WM_APP_TRAY) return IntPtr.Zero;
        handled = true;
        switch (lParam.ToInt32())
        {
            case NativeMethods.WM_LBUTTONUP: LeftClicked?.Invoke(); break;
            case NativeMethods.WM_LBUTTONDBLCLK: DoubleClicked?.Invoke(); break;
            case NativeMethods.WM_RBUTTONUP: MenuRequested?.Invoke(); break;
            case NativeMethods.WM_MOUSEMOVE:
                _hoverTimer.Stop();
                _hoverTimer.Start();
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>气泡通知(Windows 10/11 上呈现为系统通知)。卡片可能从后台线程调用,统一编组。</summary>
    public void ShowInfo(string title, string message)
        => _uiDispatcher.BeginInvoke(() =>
        {
            var nid = NewData();
            nid.uFlags = NativeMethods.NIF_INFO;
            nid.szInfoTitle = title;
            nid.szInfo = message;
            nid.dwInfoFlags = NativeMethods.NIIF_INFO;
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref nid);

            _balloonTimer?.Stop();
            _balloonTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _balloonTimer.Tick += (_, _) =>
            {
                _balloonTimer?.Stop();
                ClearBalloon();
            };
            _balloonTimer.Start();
        });

    void ClearBalloon()
    {
        var nid = NewData();
        nid.uFlags = NativeMethods.NIF_INFO;
        nid.szInfo = "";
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref nid);
    }

    public NativeMethods.RECT IconRectOrCursor()
    {
        var id = new NativeMethods.NOTIFYICONIDENTIFIER
        {
            cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
            hWnd = _src.Handle,
            uID = Uid,
        };
        if (NativeMethods.Shell_NotifyIconGetRect(ref id, out var rect) == 0) return rect;

        NativeMethods.GetCursorPos(out var pt);
        return new NativeMethods.RECT { Left = pt.X - 8, Top = pt.Y - 8, Right = pt.X + 8, Bottom = pt.Y + 8 };
    }

    NativeMethods.NOTIFYICONDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _src.Handle,
        uID = Uid,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    static IntPtr CreateAppIcon()
    {
        using var bmp = new SD.Bitmap(32, 32);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            using var path = new SD.Drawing2D.GraphicsPath();
            const int r = 9;
            path.AddArc(2, 2, r * 2, r * 2, 180, 90);
            path.AddArc(30 - r * 2, 2, r * 2, r * 2, 270, 90);
            path.AddArc(30 - r * 2, 30 - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(2, 30 - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using var fill = new SD.SolidBrush(SD.Color.FromArgb(0x8A, 0xB4, 0xF8));
            g.FillPath(fill, path);
            using var f = new SD.Font("Segoe UI", 15f, SD.FontStyle.Bold, SD.GraphicsUnit.Pixel);
            using var fmt = new SD.StringFormat
            {
                Alignment = SD.StringAlignment.Center,
                LineAlignment = SD.StringAlignment.Center,
            };
            g.DrawString("O", f, SD.Brushes.White, new SD.RectangleF(2, 3, 28, 28), fmt);
        }
        return bmp.GetHicon();
    }

    public void Dispose()
    {
        var nid = NewData();
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref nid);
        if (_icon != IntPtr.Zero) NativeMethods.DestroyIcon(_icon);
        _icon = IntPtr.Zero;
        _src.Dispose();
    }
}
