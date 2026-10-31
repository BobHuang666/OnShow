namespace OnShow.Core;

/// <summary>全局配置(config.json)。字段均为可序列化简单类型,热更新由宿主负责应用。</summary>
public class AppConfig
{
    public int Version { get; set; } = 1;

    public WindowConfig Window { get; set; } = new();

    /// <summary>开机自启动(注册表 HKCU Run)。</summary>
    public bool AutoStart { get; set; }

    /// <summary>带 --minimized 启动时是否只进托盘(遵循上次退出状态时始终为 true)。</summary>
    public bool LaunchMinimized { get; set; } = true;

    public bool ShowDdlCard { get; set; } = true;
    public bool ShowTokenCard { get; set; } = true;

    /// <summary>Token 轮询间隔(分钟)。</summary>
    public int TokenPollMinutes { get; set; } = 10;

    /// <summary>DDL 临期提醒阈值,逐条可覆盖。</summary>
    public bool DdlRemindersEnabled { get; set; } = true;

    /// <summary>已同意的用户协议版本(0 = 尚未同意,下次启动弹协议窗口)。</summary>
    public int AgreementAcceptedVersion { get; set; }

    /// <summary>自动检查更新(启动后延迟检查一次,24 小时节流)。</summary>
    public bool AutoCheckUpdate { get; set; } = true;

    /// <summary>上次检查更新的 UTC 时间(ISO8601),用于 24h 节流。</summary>
    public string? LastUpdateCheckUtc { get; set; }

    /// <summary>用户选择「跳过此版本」的版本号(如 1.1.0),自动检查不再提示该版本。</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>
    /// 更新包下载加速镜像前缀(如 https://ghfast.top/),留空直连 GitHub。
    /// 仅 zip 安装包走镜像;SHA256 校验文件永远走官方,镜像无法篡改内容。
    /// </summary>
    public string? DownloadMirrorPrefix { get; set; } = "https://ghfast.top/";
}

public class WindowConfig
{
    /// <summary>DesktopTop(默认,图标之上应用之下)| AlwaysTop(置顶)。</summary>
    public string Mode { get; set; } = "DesktopTop";

    /// <summary>锁定位置:禁用拖动,防误移。</summary>
    public bool Locked { get; set; }

    /// <summary>面板不透明度 0.5–1。</summary>
    public double Opacity { get; set; } = 0.96;

    /// <summary>上次位置(逻辑坐标,DIP);NaN 表示首次运行,自动落到右下角。</summary>
    public double X { get; set; } = double.NaN;
    public double Y { get; set; } = double.NaN;
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 560;

    /// <summary>是否显示 Widget(托盘可切换)。</summary>
    public bool Visible { get; set; } = true;

    /// <summary>托盘悬停快览开关。</summary>
    public bool FlyoutEnabled { get; set; } = true;
}
