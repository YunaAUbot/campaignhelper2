namespace CampaignHelper.Tests;

using Xunit;

public sealed class UiGateTests
{
    [Theory]
    [InlineData(false, "g1_1", false)]
    [InlineData(true, "", false)]
    [InlineData(true, "   ", false)]
    [InlineData(true, "g1_1", true)]
    public void RequiresInGameStateAndValidAreaId(bool isInGame, string areaId, bool expected)
    {
        var knownAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "g1_1" };
        Assert.Equal(expected, CampaignUiGate.CanDraw(isInGame, areaId, knownAreas));
    }

    [Fact]
    public void RejectsUnknownNonblankAreaId()
    {
        Assert.False(CampaignUiGate.CanDraw(true, "unknown", new HashSet<string> { "g1_1" }));
    }
}
