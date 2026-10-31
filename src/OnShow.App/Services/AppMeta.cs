using System.Reflection;

namespace OnShow.App.Services;

/// <summary>
/// 应用元信息:仓库地址、版本、反馈渠道、协议版本。
/// 更换仓库 / 联系方式 / 协议内容时只改这里。
/// </summary>
public static class AppMeta
{
    public const string RepoOwner = "BobHuang666";
    public const string RepoName = "OnShow";
    public const string RepoUrl = $"https://github.com/{RepoOwner}/{RepoName}";
    public const string IssuesUrl = $"{RepoUrl}/issues";
    public const string IssuesNewUrl = $"{IssuesUrl}/new";
    public const string LatestReleaseApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
    public const string LatestReleasePageUrl = $"{RepoUrl}/releases/latest";

    /// <summary>反馈备用渠道(无 GitHub 账号的用户导出反馈包后发送)。</summary>
    public const string ContactEmail = "bobhuang666@foxmail.com";

    /// <summary>用户协议文本版本:协议有实质变更时 +1,下次启动会重新弹出同意窗口。</summary>
    public const int AgreementVersion = 1;

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>形如 "1.1.0"(不含修订号以外的部分)。</summary>
    public static string CurrentVersionString
    {
        get
        {
            var v = CurrentVersion;
            return v.Revision <= 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : v.ToString();
        }
    }
}
