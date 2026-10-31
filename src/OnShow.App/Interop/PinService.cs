using System.Windows;
using System.Windows.Interop;

namespace OnShow.App.Interop;

/// <summary>
/// 桌面固定:
/// DesktopTop = 无激活 + z 序贴底(图标之上、应用之下,Win+D 后自动还原);
/// AlwaysTop  = WS_EX_TOPMOST 置顶。
/// </summary>
internal static class PinService
{
    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).EnsureHandle();

    public static void ApplyMode(Window w, string mode)
    {
        w.Topmost = mode == "AlwaysTop";
        SetNoActivate(w, true);
        if (mode != "AlwaysTop") PushToBottom(w);
    }

    public static void SetNoActivate(Window w, bool on)
    {
        var h = Handle(w);
        var ex = NativeMethods.GetExStyle(h);
        var target = on ? ex | NativeMethods.WS_EX_NOACTIVATE : ex & ~NativeMethods.WS_EX_NOACTIVATE;
        if (target != ex) NativeMethods.SetExStyle(h, target);
    }

    public static void PushToBottom(Window w)
        => NativeMethods.SetWindowPos(Handle(w), NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);

    /// <summary>不激活窗口的拖动(HTCAPTION 模拟)。调用返回即拖动结束。</summary>
    public static void DragWithoutActivate(Window w)
    {
        var h = Handle(w);
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(h, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
    }

    /// <summary>
    /// 强制把窗口置为前台:托盘菜单不取得前台焦点时,点击其他位置不会使其关闭。
    /// 通过 AttachThreadInput 临时并入前台线程输入队列,绕过 SetForegroundWindow 的前台锁。
    /// </summary>
    public static void ForceForeground(Window w)
    {
        var h = Handle(w);
        var fg = NativeMethods.GetForegroundWindow();
        var fgThread = fg != IntPtr.Zero ? NativeMethods.GetWindowThreadProcessId(fg, out _) : 0;
        var curThread = NativeMethods.GetCurrentThreadId();
        var attached = false;
        try
        {
            if (fgThread != 0 && fgThread != curThread)
                attached = NativeMethods.AttachThreadInput(curThread, fgThread, true);
            NativeMethods.SetForegroundWindow(h);
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(curThread, fgThread, false);
        }
    }
}
