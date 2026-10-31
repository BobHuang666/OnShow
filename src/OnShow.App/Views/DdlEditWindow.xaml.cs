using System.Globalization;
using System.Windows;
using OnShow.Cards.Ddl;

namespace OnShow.App.Views;

/// <summary>DDL 新增 / 编辑 / 删除(Widget 快速添加与设置页共用)。</summary>
public partial class DdlEditWindow : Window
{
    readonly DdlCard _card;
    readonly DdlItem? _editing;

    public DdlEditWindow(DdlCard card, DdlItem? editing)
    {
        InitializeComponent();
        _card = card;
        _editing = editing;

        if (editing is not null)
        {
            Title = "编辑 DDL";
            HeaderText.Text = "编辑 DDL";
            TitleBox.Text = editing.Title;
            DueBox.Text = editing.Due.ToString("yyyy-MM-dd HH:mm");
            NoteBox.Text = editing.Note;
            DoneBox.Visibility = Visibility.Visible;
            DoneBox.IsChecked = editing.Done;
            DeleteBtn.Visibility = Visibility.Visible;
            foreach (var th in editing.RemindBefore)
            {
                switch (th)
                {
                    case "3d": R3d.IsChecked = true; break;
                    case "24h": R24h.IsChecked = true; break;
                    case "1h": R1h.IsChecked = true; break;
                }
            }
        }
        else
        {
            Title = "添加 DDL";
            HeaderText.Text = "添加 DDL";
            DueBox.Text = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd") + " 18:00";
        }

        Loaded += (_, _) => { TitleBox.Focus(); TitleBox.SelectAll(); };
    }

    void OnChipTomorrow(object sender, RoutedEventArgs e)
        => DueBox.Text = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd") + " 18:00";

    void OnChip3Days(object sender, RoutedEventArgs e)
        => DueBox.Text = DateTime.Now.AddDays(3).ToString("yyyy-MM-dd HH:mm");

    void OnChip7Days(object sender, RoutedEventArgs e)
        => DueBox.Text = DateTime.Now.AddDays(7).ToString("yyyy-MM-dd HH:mm");

    void OnSave(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        if (title.Length == 0)
        {
            Dialog.Warn(this, "无法保存", "标题不能为空");
            return;
        }
        if (!TryParseDue(DueBox.Text, out var due))
        {
            Dialog.Warn(this, "无法保存", "时间格式无法识别,请使用:2026-06-08 14:00");
            return;
        }

        var remind = new List<string>();
        if (R3d.IsChecked == true) remind.Add("3d");
        if (R24h.IsChecked == true) remind.Add("24h");
        if (R1h.IsChecked == true) remind.Add("1h");

        var item = _editing ?? new DdlItem();
        item.Title = title;
        item.Due = due;
        item.Note = NoteBox.Text.Trim();
        item.RemindBefore = remind.ToArray();
        if (_editing is not null)
            item.Done = DoneBox.IsChecked == true;

        _card.Upsert(item);
        DialogResult = true;
    }

    void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        if (!Dialog.Confirm(this, "删除 DDL", $"确定删除「{_editing.Title}」?")) return;
        _card.Remove(_editing.Id);
        DialogResult = true;
    }

    static bool TryParseDue(string s, out DateTime dt)
    {
        var formats = new[]
        {
            "yyyy-M-d H:m", "yyyy-M-d H:m:s", "yyyy-MM-dd HH:mm",
            "yyyy/M/d H:m", "yyyy-M-d", "yyyy/MM/dd",
        };
        return DateTime.TryParseExact(s.Trim(), formats,
                   CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.None, out dt)
               || DateTime.TryParse(s.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out dt);
    }
}
