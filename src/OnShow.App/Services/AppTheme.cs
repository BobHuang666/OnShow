using System.Windows;

namespace OnShow.App.Services;

/// <summary>
/// 全局主题:黑 / 灰 / 白 + 红橙黄绿青蓝紫浅色,作用于全部窗口与弹窗。
/// 通过应用级资源覆盖实现;各 XAML 以 DynamicResource 引用,切换即时生效。
/// </summary>
internal static class AppTheme
{
    public sealed record Preset(string Key, string Name, bool Light);

    public static readonly Preset[] Presets =
    {
        new("Black",  "黑", Light: false),
        new("Gray",   "灰", Light: false),
        new("White",  "白", Light: true),
        new("Red",    "红", Light: true),
        new("Orange", "橙", Light: true),
        new("Yellow", "黄", Light: true),
        new("Green",  "绿", Light: true),
        new("Cyan",   "青", Light: true),
        new("Blue",   "蓝", Light: true),
        new("Purple", "紫", Light: true),
    };

    public static Preset Resolve(string? key) =>
        Presets.FirstOrDefault(p => p.Key == key) ?? Presets[0];

    static readonly (string Key, bool Light)[] PanelColors =
    {
        ("Black",  false), ("Gray", false), ("White", true),
        ("Red",    true),  ("Orange", true), ("Yellow", true),
        ("Green",  true),  ("Cyan", true),  ("Blue", true), ("Purple", true),
    };

    public static System.Windows.Media.Color PanelColor(string key)
    {
        var lightKey = key;
        return lightKey switch
        {
            "Black"  => System.Windows.Media.Color.FromRgb(0x1D, 0x1E, 0x23),
            "Gray"   => System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x45),
            "White"  => System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF),
            "Red"    => System.Windows.Media.Color.FromRgb(0xFB, 0xE7, 0xE9),
            "Orange" => System.Windows.Media.Color.FromRgb(0xFD, 0xEE, 0xDC),
            "Yellow" => System.Windows.Media.Color.FromRgb(0xFB, 0xF3, 0xDC),
            "Green"  => System.Windows.Media.Color.FromRgb(0xE4, 0xF5, 0xEC),
            "Cyan"   => System.Windows.Media.Color.FromRgb(0xE0, 0xF4, 0xF4),
            "Blue"   => System.Windows.Media.Color.FromRgb(0xE3, 0xF0, 0xFB),
            "Purple" => System.Windows.Media.Color.FromRgb(0xEF, 0xEA, 0xFB),
            _        => System.Windows.Media.Color.FromRgb(0x1D, 0x1E, 0x23),
        };
    }

    /// <summary>把预设调色板写入应用级资源,全部窗口即时刷新。</summary>
    public static void Apply(string? key)
    {
        var preset = Resolve(key);
        var light = preset.Light;
        var panel = PanelColor(preset.Key);
        var res = Application.Current.Resources;

        void Set(string name, System.Windows.Media.Color c)
            => res[name] = new System.Windows.Media.SolidColorBrush(c);

        const byte panelAlpha = 0xF2;
        Set("BrushPanel", System.Windows.Media.Color.FromArgb(panelAlpha, panel.R, panel.G, panel.B));
        Set("BrushWindowBg", light
            ? System.Windows.Media.Color.FromRgb(0xEF, 0xF1, 0xF4)
            : System.Windows.Media.Color.FromRgb(0x1B, 0x1C, 0x21));

        Set("BrushCard", light
            ? System.Windows.Media.Color.FromArgb(0x12, 0x00, 0x00, 0x00)
            : System.Windows.Media.Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
        Set("BrushRow", light
            ? System.Windows.Media.Color.FromArgb(0x0D, 0x00, 0x00, 0x00)
            : System.Windows.Media.Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
        Set("BrushRowHover", light
            ? System.Windows.Media.Color.FromArgb(0x1A, 0x00, 0x00, 0x00)
            : System.Windows.Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        Set("BrushBorder", light
            ? System.Windows.Media.Color.FromArgb(0x22, 0x00, 0x00, 0x00)
            : System.Windows.Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        Set("BrushBorderStrong", light
            ? System.Windows.Media.Color.FromArgb(0x3D, 0x00, 0x00, 0x00)
            : System.Windows.Media.Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF));
        Set("BrushInputBg", light
            ? System.Windows.Media.Color.FromArgb(0x1A, 0x00, 0x00, 0x00)
            : System.Windows.Media.Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        Set("BrushBtnBg", light
            ? System.Windows.Media.Color.FromArgb(0x1A, 0x00, 0x00, 0x00)
            : System.Windows.Media.Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        Set("BrushBtnBgHover", light
            ? System.Windows.Media.Color.FromArgb(0x2E, 0x00, 0x00, 0x00)
            : System.Windows.Media.Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        Set("BrushTokenTrack", light
            ? System.Windows.Media.Color.FromArgb(0x1A, 0x00, 0x00, 0x00)
            : System.Windows.Media.Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));

        Set("BrushFg1", light
            ? System.Windows.Media.Color.FromRgb(0x20, 0x24, 0x2B)
            : System.Windows.Media.Color.FromRgb(0xEC, 0xEF, 0xF3));
        Set("BrushFg2", light
            ? System.Windows.Media.Color.FromRgb(0x5A, 0x62, 0x70)
            : System.Windows.Media.Color.FromRgb(0xA8, 0xB0, 0xBA));
        Set("BrushAccent", light
            ? System.Windows.Media.Color.FromRgb(0x2F, 0x6F, 0xD0)
            : System.Windows.Media.Color.FromRgb(0x8A, 0xB4, 0xF8));
        Set("BrushAccentDim", light
            ? System.Windows.Media.Color.FromArgb(0x33, 0x2F, 0x6F, 0xD0)
            : System.Windows.Media.Color.FromArgb(0x33, 0x8A, 0xB4, 0xF8));

        Set("BrushFar", light
            ? System.Windows.Media.Color.FromRgb(0x6B, 0x72, 0x80)
            : System.Windows.Media.Color.FromRgb(0xC9, 0xCE, 0xD6));
        Set("BrushNear", light
            ? System.Windows.Media.Color.FromRgb(0xB2, 0x5F, 0x00)
            : System.Windows.Media.Color.FromRgb(0xFF, 0xA7, 0x5F));
        Set("BrushUrgent", light
            ? System.Windows.Media.Color.FromRgb(0xD6, 0x45, 0x45)
            : System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B));
        Set("BrushOver", light
            ? System.Windows.Media.Color.FromRgb(0x8A, 0x90, 0x99)
            : System.Windows.Media.Color.FromRgb(0x78, 0x7E, 0x85));
        Set("BrushWarn", light
            ? System.Windows.Media.Color.FromRgb(0xB0, 0x7A, 0x00)
            : System.Windows.Media.Color.FromRgb(0xFD, 0xD6, 0x63));
        Set("BrushDanger", light
            ? System.Windows.Media.Color.FromRgb(0xD6, 0x45, 0x45)
            : System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B));
        Set("BrushNormal", light
            ? System.Windows.Media.Color.FromRgb(0x4A, 0x51, 0x60)
            : System.Windows.Media.Color.FromRgb(0xC9, 0xCE, 0xD6));
    }
}
