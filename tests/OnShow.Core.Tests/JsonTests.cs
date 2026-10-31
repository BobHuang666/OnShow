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
