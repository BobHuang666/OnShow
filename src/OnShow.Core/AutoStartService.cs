using Microsoft.Win32;

namespace OnShow.Core;

/// <summary>开机自启动:HKCU\...\Run,用户级、免管理员;写入 "OnShow.exe" --minimized。</summary>
public static class AutoStartService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "OnShow";

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return k?.GetValue(ValueName) is string;
        }
        catch (Exception ex)
        {
            Log.Error("读取自启动状态失败", ex);
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            var exe = Environment.ProcessPath
                      ?? throw new InvalidOperationException("无法定位可执行文件路径");
            if (enabled)
                k.SetValue(ValueName, $"\"{exe}\" --minimized");
            else if (k.GetValue(ValueName) is not null)
                k.DeleteValue(ValueName);
            Log.Info($"开机自启动 -> {(enabled ? "开启" : "关闭")}");
        }
        catch (Exception ex)
        {
            Log.Error("设置开机自启动失败", ex);
            throw;
        }
    }
}
