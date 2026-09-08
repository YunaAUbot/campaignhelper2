namespace CampaignHelper.Tests;

using Xunit;

public sealed class UpdateNoticeTests
{
    [Fact]
    public void ShowsCompactMessageOnlyWhenCandidateExists()
    {
        Assert.Null(CampaignUpdateNotice.MessageFor(false));
        Assert.Equal(
            "Guide update available - open CampaignHelper settings",
            CampaignUpdateNotice.MessageFor(true));
        Assert.True(CampaignUpdateNotice.ShouldUseDetachedWindow(true, false));
        Assert.False(CampaignUpdateNotice.ShouldUseDetachedWindow(false, false));
        Assert.False(CampaignUpdateNotice.ShouldUseDetachedWindow(true, true));
    }
}
