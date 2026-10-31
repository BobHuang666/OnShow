using System.Windows;
using System.Windows.Media;

namespace OnShow.App.Services;

/// <summary>
/// 主面板背景预设:黑白与各种浅色;浅色背景自动切换深色文字调色板。
/// 通过窗口级资源覆盖 + DynamicResource 实现,设置即时生效。
/// </summary>
internal static class WidgetTheme
{
    public sealed record Preset(string Key, string Name, Color Panel, bool Light);

    public static readonly Preset[] Presets =
    {
        new("Dark",   "深色", Color.FromRgb(0x1D, 0x1E, 0x23), Light: false),
        new("Black",  "纯黑", Color.FromRgb(0x00, 0x00, 0x00), Light: false),
        new("White",  "纯白", Color.FromRgb(0xFF, 0xFF, 0xFF), Light: true),
        new("Gray",   "浅灰", Color.FromRgb(0xF2, 0xF3, 0xF5), Light: true),
        new("Blue",   "浅蓝", Color.FromRgb(0xE3, 0xF0, 0xFB), Light: true),
        new("Green",  "浅绿", Color.FromRgb(0xE4, 0xF5, 0xEC), Light: true),
        new("Yellow", "浅黄", Color.FromRgb(0xFB, 0xF3, 0xDC), Light: true),
        new("Pink",   "浅粉", Color.FromRgb(0xFB, 0xE9, 0xEE), Light: true),
        new("Purple", "浅紫", Color.FromRgb(0xEF, 0xEA, 0xFB), Light: true),
    };

    public static Preset Resolve(string? key) =>
        Presets.FirstOrDefault(p => p.Key == key) ?? Presets[0];

    static readonly Color Fg1Dark = Color.FromRgb(0xEC, 0xEF, 0xF3);
    static readonly Color Fg1Light = Color.FromRgb(0x20, 0x24, 0x2B);

    /// <summary>把预设调色板写入窗口资源(DynamicResource 引用即时刷新)。</summary>
    public static void Apply(Window window, string? key)
    {
        var preset = Resolve(key);
        var light = preset.Light;
        byte panelAlpha = 0xF2;   // 轻微透出桌面,与原 0xE8 观感一致

        void Set(string keyName, Color c) =>
            window.Resources[keyName] = new SolidColorBrush(Color.FromArgb(panelAlpha, c.R, c.G, c.B));

        Set("BrushPanel", preset.Panel);

        Color Fg1() => light ? Fg1Light : Fg1Dark;
        Color Fg2() => light ? Color.FromRgb(0x5A, 0x62, 0x70) : Color.FromRgb(0xA8, 0xB0, 0xBA);
        Color Row(bool hover) => light
            ? Color.FromArgb(hover ? (byte)0x1A : (byte)0x0D, 0x00, 0x00, 0x00)
            : Color.FromArgb(hover ? (byte)0x22 : (byte)0x12, 0xFF, 0xFF, 0xFF);
        Color BorderColor() => light ? Color.FromRgb(0x00, 0x00, 0x00) : Color.FromRgb(0xFF, 0xFF, 0xFF);

        window.Resources["BrushBorder"] = new SolidColorBrush(
            Color.FromArgb(light ? (byte)0x22 : (byte)0x22, BorderColor().R, BorderColor().G, BorderColor().B));
        window.Resources["BrushRow"] = new SolidColorBrush(Row(hover: false));
        window.Resources["BrushRowHover"] = new SolidColorBrush(Row(hover: true));
        window.Resources["BrushTokenTrack"] = new SolidColorBrush(
            Color.FromArgb((byte)0x1A, Fg2().R, Fg2().G, Fg2().B));

        window.Resources["BrushFg1"] = new SolidColorBrush(Fg1());
        window.Resources["BrushFg2"] = new SolidColorBrush(Fg2());

        // 语义色:浅色背景加深到可读
        window.Resources["BrushFar"] = new SolidColorBrush(
            light ? Color.FromRgb(0x6B, 0x72, 0x80) : Color.FromRgb(0xC9, 0xCE, 0xD6));
        window.Resources["BrushNear"] = new SolidColorBrush(
            light ? Color.FromRgb(0xB2, 0x5F, 0x00) : Color.FromRgb(0xFF, 0xA7, 0x5F));
        window.Resources["BrushUrgent"] = new SolidColorBrush(
            light ? Color.FromRgb(0xD6, 0x45, 0x45) : Color.FromRgb(0xFF, 0x6B, 0x6B));
        window.Resources["BrushOver"] = new SolidColorBrush(
            light ? Color.FromRgb(0x8A, 0x90, 0x99) : Color.FromRgb(0x78, 0x7E, 0x85));
        window.Resources["BrushWarn"] = new SolidColorBrush(
            light ? Color.FromRgb(0xB0, 0x7A, 0x00) : Color.FromRgb(0xFD, 0xD6, 0x63));
        window.Resources["BrushDanger"] = new SolidColorBrush(
            light ? Color.FromRgb(0xD6, 0x45, 0x45) : Color.FromRgb(0xFF, 0x6B, 0x6B));
        window.Resources["BrushNormal"] = new SolidColorBrush(
            light ? Color.FromRgb(0x4A, 0x51, 0x60) : Color.FromRgb(0xC9, 0xCE, 0xD6));

        // 强调色保持品牌蓝,浅色下加深
        window.Resources["BrushAccent"] = new SolidColorBrush(
            light ? Color.FromRgb(0x2F, 0x6F, 0xD0) : Color.FromRgb(0x8A, 0xB4, 0xF8));
        window.Resources["BrushAccentDim"] = new SolidColorBrush(
            light ? Color.FromArgb(0x33, 0x2F, 0x6F, 0xD0) : Color.FromArgb(0x33, 0x8A, 0xB4, 0xF8));
    }
}
