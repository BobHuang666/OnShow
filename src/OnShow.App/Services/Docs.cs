using System.IO;
using System.Windows;
using System.Windows.Documents;

namespace OnShow.App.Services;

/// <summary>内嵌文档加载:用户协议 / 隐私声明 / 更新日志(csproj 中以 Resource 打包)。</summary>
internal static class Docs
{
    // 注意:Resource Include 按项目内路径打包,Assets 下的文档带 Assets/ 前缀;
    // CHANGELOG 通过 Link=Docs\CHANGELOG.md 改写为 /Docs/。
    const string UserAgreementUri = "pack://application:,,,/Assets/Docs/UserAgreement.md";
    const string PrivacyStatementUri = "pack://application:,,,/Assets/Docs/PrivacyStatement.md";
    const string ChangeLogUri = "pack://application:,,,/Docs/CHANGELOG.md";

    static string LoadText(string packUri)
    {
        var sri = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute))
                  ?? throw new FileNotFoundException("内嵌资源缺失:" + packUri);
        using var reader = new StreamReader(sri.Stream);
        return reader.ReadToEnd();
    }

    public static FlowDocument UserAgreement() => MdLite.Render(LoadText(UserAgreementUri));

    public static FlowDocument PrivacyStatement() => MdLite.Render(LoadText(PrivacyStatementUri));

    public static FlowDocument ChangeLog() => MdLite.Render(LoadText(ChangeLogUri));

    /// <summary>首启协议窗口展示:用户协议 + 隐私声明(分隔线衔接)。</summary>
    public static FlowDocument AgreementCombined()
    {
        var doc = UserAgreement();
        doc.Blocks.Add(MdLite.Divider());
        foreach (var block in PrivacyStatement().Blocks.ToList())
            doc.Blocks.Add(block);
        return doc;
    }
}
