using OnShow.Cards.Ddl;
using Xunit;

namespace OnShow.Core.Tests;

public class DdlCardTests
{
    [Theory]
    [InlineData("30m", 30)]
    [InlineData("1h", 60)]
    [InlineData("24h", 24 * 60)]
    [InlineData("3d", 3 * 24 * 60)]
    public void ParseThreshold_Known_Values(string input, double expectMinutes)
    {
        var span = DdlCard.ParseThreshold(input);
        Assert.NotNull(span);
        Assert.Equal(expectMinutes, span!.Value.TotalMinutes);
    }

    [Fact]
    public void ParseThreshold_Unknown_Returns_Null()
    {
        Assert.Null(DdlCard.ParseThreshold("xx"));
        Assert.Null(DdlCard.ParseThreshold(""));
    }
}
