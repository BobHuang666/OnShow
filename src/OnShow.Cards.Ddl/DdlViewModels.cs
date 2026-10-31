using OnShow.Core;

namespace OnShow.Cards.Ddl;

/// <summary>单条 DDL 的视图模型:卡片每秒原地更新,UI 走 INPC 增量刷新,不整表重建。</summary>
public class DdlItemVm : ObservableObject
{
    string _remainText = "";
    string _level = "far";

    public required DdlItem Item { get; init; }

    public string Id => Item.Id;
    public string Title => Item.Title;
    public string Note => Item.Note;
    public bool Done => Item.Done;
    public string DueText => Item.Due.ToString("MM-dd HH:mm");

    public string RemainText { get => _remainText; private set => Set(ref _remainText, value); }
    /// <summary>far / near / urgent / over / done → 决定颜色。</summary>
    public string Level { get => _level; private set => Set(ref _level, value); }

    public void Update(DateTime now)
    {
        if (Item.Done)
        {
            RemainText = "已完成";
            Level = "done";
            return;
        }
        var (text, level, _) = TimeUtil.Describe(Item.Due, now);
        RemainText = text;
        Level = level;
    }
}
