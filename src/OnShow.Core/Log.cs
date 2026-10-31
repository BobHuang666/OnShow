namespace OnShow.Core;

/// <summary>极简滚动日志(保留 7 天),绝不抛异常。</summary>
public static class Log
{
    static readonly object Gate = new();
    static bool _cleaned;

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null)
        => Write("ERROR", ex is null ? msg : $"{msg}{Environment.NewLine}{ex}");

    static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                var file = Path.Combine(AppPaths.Logs, $"onshow-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}");
                if (!_cleaned)
                {
                    _cleaned = true;
                    foreach (var f in Directory.GetFiles(AppPaths.Logs, "onshow-*.log"))
                        if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7))
                            File.Delete(f);
                }
            }
        }
        catch { /* 日志绝不抛异常 */ }
    }
}
