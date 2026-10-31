using OnShow.Core;
using Xunit;

namespace OnShow.Core.Tests;

public class UpdateCheckTests
{
    const string SampleJson = """
        {
          "url": "https://api.github.com/repos/BobHuang666/OnShow/releases/1",
          "tag_name": "v1.1.0",
          "name": "OnShow v1.1.0",
          "body": "### 新增\n- 应用内更新",
          "draft": false,
          "prerelease": false,
          "assets": [
            {
              "name": "checksums.txt",
              "browser_download_url": "https://github.com/BobHuang666/OnShow/releases/download/v1.1.0/checksums.txt",
              "size": 95
            },
            {
              "name": "OnShow-v1.1.0-win-x64.zip",
              "browser_download_url": "https://github.com/BobHuang666/OnShow/releases/download/v1.1.0/OnShow-v1.1.0-win-x64.zip",
              "size": 68000000,
              "digest": "sha256:a063f25607f35f2b3349e14e43571ad1e90273b986d8a337862d229553178a77"
            }
          ]
        }
        """;

    [Fact]
    public void Parse_ExtractsTagAndAssets()
    {
        var info = GitHubReleaseParser.Parse(SampleJson);
        Assert.NotNull(info);
        Assert.Equal("v1.1.0", info!.TagName);
        Assert.Equal(new Version(1, 1, 0), info.Version);
        Assert.Equal("OnShow-v1.1.0-win-x64.zip", info.ZipAssetName);
        Assert.Equal("https://github.com/BobHuang666/OnShow/releases/download/v1.1.0/OnShow-v1.1.0-win-x64.zip", info.ZipAssetUrl);
        Assert.Equal(68000000, info.ZipSize);
        Assert.Equal("https://github.com/BobHuang666/OnShow/releases/download/v1.1.0/checksums.txt", info.ChecksumsUrl);
        Assert.Equal("a063f25607f35f2b3349e14e43571ad1e90273b986d8a337862d229553178a77", info.ZipDigestHex);
        Assert.Contains("应用内更新", info.Body);
    }

    [Fact]
    public void Parse_BadJson_ReturnsNull()
    {
        Assert.Null(GitHubReleaseParser.Parse("not json"));
        Assert.Null(GitHubReleaseParser.Parse("{}"));
        Assert.Null(GitHubReleaseParser.Parse("""{"assets":[]}"""));
    }

    [Theory]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("v1.9.0", "1.10.0", false)]     // 语义比较,不是字符串比较
    [InlineData("v2.0.0", "1.99.99", true)]
    [InlineData("bogus", "1.0.0", false)]       // 无法解析的 tag 不触发更新
    public void IsNewer_ComparesSemantically(string tag, string current, bool expected)
    {
        Assert.Equal(expected, UpdateHelper.IsNewer(tag, Version.Parse(current)));
    }

    [Fact]
    public void ChecksumFile_FindsEntryForFile()
    {
        const string content = "abc123  OnShow-v1.1.0-win-x64.zip\n" +
                               "def456  other-file.zip\n";
        Assert.Equal("abc123", ChecksumFile.FindFor(content, "OnShow-v1.1.0-win-x64.zip"));
        Assert.Equal("abc123", ChecksumFile.FindFor(content, "onshow-V1.1.0-WIN-X64.zip")); // 大小写不敏感
        Assert.Null(ChecksumFile.FindFor(content, "missing.zip"));
        Assert.Null(ChecksumFile.FindFor("", "x.zip"));
    }
}

public class FeedbackComposerTests
{
    [Fact]
    public void ComposeTitle_UsesFirstNonEmptyLine()
    {
        Assert.Equal("Token 面板不显示", FeedbackComposer.ComposeTitle("\n  Token 面板不显示 \n后面的话"));
        Assert.Equal("用户反馈", FeedbackComposer.ComposeTitle("   "));
    }

    [Fact]
    public void ComposeTitle_TruncatesLongLine()
    {
        var title = FeedbackComposer.ComposeTitle(new string('长', 100));
        Assert.True(title.Length <= 49);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void ComposeBody_AppendsEnvBlock()
    {
        var body = FeedbackComposer.ComposeBody("有问题", "- OnShow 版本:1.1.0");
        Assert.StartsWith("有问题", body);
        Assert.Contains("**环境信息**", body);
        Assert.Contains("- OnShow 版本:1.1.0", body);
        Assert.Contains("OnShow 反馈窗口", body);

        var noEnv = FeedbackComposer.ComposeBody("有问题", null);
        Assert.DoesNotContain("环境信息", noEnv);
    }

    [Fact]
    public void BuildIssueUrl_EncodesAndTruncates()
    {
        var url = FeedbackComposer.BuildIssueUrl(
            "https://github.com/BobHuang666/OnShow/issues/new", "标题", "内容描述");
        Assert.StartsWith("https://github.com/BobHuang666/OnShow/issues/new?title=", url);
        Assert.Contains(Uri.EscapeDataString("标题"), url);

        // 超长正文必须截断,URL 不超过浏览器安全长度
        var huge = new string('描', 100000);
        var longUrl = FeedbackComposer.BuildIssueUrl(
            "https://github.com/BobHuang666/OnShow/issues/new", "标题", huge);
        Assert.True(longUrl.Length < 8000, $"URL 过长:{longUrl.Length}");
        Assert.Contains("已截断", Uri.UnescapeDataString(longUrl));
    }
}
