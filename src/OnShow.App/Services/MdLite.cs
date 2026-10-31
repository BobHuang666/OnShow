using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace OnShow.App.Services;

/// <summary>
/// 极简 Markdown → FlowDocument:标题 / 加粗 / 行内代码 / 链接 / 列表 / 引用 / 分隔线。
/// 服务于内嵌的用户协议、隐私声明与更新日志展示,不追求完整 Markdown 覆盖。
/// </summary>
public static partial class MdLite
{
    [GeneratedRegex(@"\*\*(?<bold>.+?)\*\*|`(?<code>[^`\n]+)`|(?<link>https?://[^\s<>\""')]+)", RegexOptions.CultureInvariant)]
    private static partial Regex InlinePattern();

    [GeneratedRegex(@"^\d+[.、]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex OrderedItemPattern();

    internal sealed class Palette
    {
        public Brush Fg1 = Res("BrushFg1");
        public Brush Fg2 = Res("BrushFg2");
        public Brush Accent = Res("BrushAccent");
        public Brush Border = Res("BrushBorder");
        public Brush RowBg = Res("BrushRow");

        static Brush Res(string key) =>
            Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public static FlowDocument Render(string markdown)
    {
        var pal = new Palette();
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            FontSize = 13,
            LineHeight = double.NaN,
            PagePadding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = pal.Fg1,
        };

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;

            if (line is "-" or "---" or "***") { doc.Blocks.Add(Divider(pal)); continue; }

            if (line.StartsWith("### ")) doc.Blocks.Add(Para(line[4..], 13, FontWeights.SemiBold, pal, top: 10));
            else if (line.StartsWith("## ")) doc.Blocks.Add(Para(line[3..], 15, FontWeights.Bold, pal, top: 16));
            else if (line.StartsWith("# ")) doc.Blocks.Add(Para(line[2..], 19, FontWeights.Bold, pal, top: 4, bottom: 6));
            else if (line.StartsWith("> ")) doc.Blocks.Add(Quote(line[2..].Trim(), pal));
            else if (line.StartsWith("- ") || line.StartsWith("* "))
                doc.Blocks.Add(Para("•  " + line[2..], 13, FontWeights.Normal, pal, indent: 14, top: 2, bottom: 2));
            else if (OrderedItemPattern().IsMatch(line))
                doc.Blocks.Add(Para(line, 13, FontWeights.Normal, pal, indent: 14, top: 2, bottom: 2));
            else
                doc.Blocks.Add(Para(line, 13, FontWeights.Normal, pal));
        }
        return doc;
    }

    /// <summary>浅分隔线(也用于合并两份文档时的间隔)。</summary>
    internal static Block Divider(Palette? pal = null)
    {
        pal ??= new Palette();
        return new BlockUIContainer(new Border
        {
            Height = 1,
            Background = pal.Border,
            Margin = new Thickness(0, 8, 0, 8),
        });
    }

    static Paragraph Para(string text, double size, FontWeight weight, Palette pal,
        double indent = 0, double top = 5, double bottom = 5)
    {
        var p = new Paragraph
        {
            Margin = new Thickness(indent, top, 0, bottom),
            FontSize = size,
            FontWeight = weight,
            Foreground = pal.Fg1,
        };
        AddInline(p, text, pal);
        return p;
    }

    static Paragraph Quote(string text, Palette pal)
    {
        var p = new Paragraph
        {
            Margin = new Thickness(10, 4, 0, 4),
            FontSize = 12,
            FontStyle = FontStyles.Italic,
            Foreground = pal.Fg2,
        };
        AddInline(p, text, pal);
        return p;
    }

    static void AddInline(Paragraph p, string text, Palette pal)
    {
        var last = 0;
        foreach (Match m in InlinePattern().Matches(text))
        {
            if (m.Index > last) p.Inlines.Add(new Run(text[last..m.Index]));

            if (m.Groups["bold"].Success)
            {
                p.Inlines.Add(new Run(m.Groups["bold"].Value) { FontWeight = FontWeights.SemiBold });
            }
            else if (m.Groups["code"].Success)
            {
                p.Inlines.Add(new Run(m.Groups["code"].Value)
                {
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = pal.Accent,
                    Background = pal.RowBg,
                });
            }
            else
            {
                var link = new Hyperlink(new Run(m.Value))
                {
                    Foreground = pal.Accent,
                    NavigateUri = new Uri(m.Value),
                    ToolTip = m.Value,
                };
                link.RequestNavigate += (_, e) =>
                {
                    try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                    catch { /* 忽略打不开的链接 */ }
                };
                p.Inlines.Add(link);
            }
            last = m.Index + m.Length;
        }
        if (last < text.Length) p.Inlines.Add(new Run(text[last..]));
    }
}
