namespace CampaignHelper.Tests;

using Xunit;

public sealed class CampaignUiTextTests
{
    [Fact]
    public void FormatsOverlayChromeWithSupportedAsciiCharacters()
    {
        var values = new[]
        {
            CampaignUiText.AreaHeader("The Grelwood", 4),
            CampaignUiText.PageHeader(1, 4, 142),
            CampaignUiText.GuideLine("Follow the road"),
            CampaignUiText.Target(null),
            CampaignUiText.CheckingStatus,
        };

        Assert.Equal("The Grelwood | Level 4", values[0]);
        Assert.Equal("Act 1 | Page 4/142", values[1]);
        Assert.Equal("- Follow the road", values[2]);
        Assert.Equal("Target: none", values[3]);
        Assert.Equal("Checking...", values[4]);
        Assert.All(values.SelectMany(value => value), character => Assert.InRange((int)character, 32, 126));
    }

    [Fact]
    public void MakesTaskHierarchyObviousAtAGlance()
    {
        Assert.Equal("1. Follow the road", CampaignUiText.TaskLine(1, "Follow the road"));
        Assert.Equal("2. GOAL: Enter Red Vale", CampaignUiText.DestinationLine(2, "Enter Red Vale"));
        Assert.Equal("+ OPTIONAL: Loot the hut", CampaignUiText.OptionalLine("Loot the hut"));
        Assert.Equal("Tip: Mushrooms mark the exit", CampaignUiText.TipLine("Mushrooms mark the exit"));
    }
}
