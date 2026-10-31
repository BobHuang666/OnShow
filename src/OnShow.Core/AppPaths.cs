namespace OnShow.Core;

/// <summary>应用数据目录与文件路径(%APPDATA%\OnShow)。</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OnShow");

    public static string Logs => Path.Combine(Root, "logs");
    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static string StateFile => Path.Combine(Root, "state.json");
    public static string SecretFile => Path.Combine(Root, "tokens.dat");

    public static void EnsureAll()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
    }
}
