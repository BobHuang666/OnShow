using System.Collections.ObjectModel;
using OnShow.Core;
using OnShow.Cards.Ddl;
using OnShow.Cards.Token;

namespace OnShow.App.ViewModels;

/// <summary>快览视图模型:任务栏缩略图与托盘 hover flyout 共用。</summary>
public sealed class GlanceVm : ObservableObject
{
    string _ddlTitle = "暂无 DDL";
    string _ddlRemain = "—";
    string _ddlLevel = "far";
    string _updatedText = "";

    public ObservableCollection<GlanceLine> Lines { get; } = new();

    public string DdlTitle { get => _ddlTitle; private set => Set(ref _ddlTitle, value); }
    public string DdlRemain { get => _ddlRemain; private set => Set(ref _ddlRemain, value); }
    public string DdlLevel { get => _ddlLevel; private set => Set(ref _ddlLevel, value); }
    public string UpdatedText { get => _updatedText; private set => Set(ref _updatedText, value); }

    public void Update(DdlCard? ddl, TokenCard? token)
    {
        var next = ddl?.Next;
        if (next is not null)
        {
            DdlTitle = next.Title;
            DdlRemain = next.RemainText;
            DdlLevel = next.Level;
        }
        else
        {
            DdlTitle = "暂无进行中的 DDL";
            DdlRemain = "—";
            DdlLevel = "far";
        }

        Lines.Clear();
        var rows = token?.Rows.Where(r => r.Account.Enabled).Take(4) ?? Enumerable.Empty<TokenRowVm>();
        foreach (var r in rows)
            Lines.Add(new GlanceLine(r.Name, r.ErrorText.Length > 0 ? "!" : r.StatusText, r.Level));
        if (Lines.Count == 0)
            Lines.Add(new GlanceLine("Token 监控", "未配置账户", "normal"));

        UpdatedText = token is null ? "" : $"更新于 {token.LastRefresh:HH:mm}";
    }
}

public sealed record GlanceLine(string Name, string Value, string Level);
