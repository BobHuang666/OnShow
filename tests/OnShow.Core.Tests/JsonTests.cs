using OnShow.Core;
using Xunit;

namespace OnShow.Core.Tests;

public class JsonTests
{
    [Fact]
    public void Roundtrip_Preserves_Config()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onshow-test-{Guid.NewGuid():N}.json");
        try
        {
            var config = new AppConfig
            {
                TokenPollMinutes = 15,
                ShowDdlCard = false,
                AutoStart = true,
                Window = new WindowConfig { Mode = "AlwaysTop", Locked = true, Opacity = 0.8, X = 10, Y = 20 },
            };
            Json.Save(path, config);
            var loaded = Json.Load<AppConfig>(path);

            Assert.Equal(15, loaded.TokenPollMinutes);
            Assert.False(loaded.ShowDdlCard);
            Assert.True(loaded.AutoStart);
            Assert.Equal("AlwaysTop", loaded.Window.Mode);
            Assert.True(loaded.Window.Locked);
            Assert.Equal(0.8, loaded.Window.Opacity);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_Missing_File_Returns_Default()
    {
        var config = Json.Load<AppConfig>(Path.Combine(Path.GetTempPath(), $"onshow-none-{Guid.NewGuid():N}.json"));
        Assert.Equal("DesktopTop", config.Window.Mode);
        Assert.Equal(10, config.TokenPollMinutes);
    }

    [Fact]
    public void Roundtrip_NaN_Position_Saves_And_Loads()
    {
        // 回归:WindowConfig.X/Y 默认 NaN,System.Text.Json 默认拒绝序列化 NaN,
        // 曾导致整份 config.json 保存静默失败(EULA 同意记录/跳过版本等全部丢失)。
        var path = Path.Combine(Path.GetTempPath(), $"onshow-nan-{Guid.NewGuid():N}.json");
        try
        {
            var config = new AppConfig { AgreementAcceptedVersion = 1, SkippedVersion = "1.1.0" };
            Json.Save(path, config);
            var loaded = Json.Load<AppConfig>(path);
            Assert.Equal(1, loaded.AgreementAcceptedVersion);
            Assert.Equal("1.1.0", loaded.SkippedVersion);
            Assert.True(double.IsNaN(loaded.Window.X));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_Corrupt_File_Does_Not_Throw()
    {
        var path = Path.Combine(Path.GetTempPath(), $"onshow-bad-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not valid json !!!");
        try
        {
            var config = Json.Load<AppConfig>(path);
            Assert.NotNull(config);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
