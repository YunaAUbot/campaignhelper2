namespace CampaignHelper.Tests;

using Xunit;

public sealed class UpdateScheduleTests
{
    [Fact]
    public void StartsOnlyWhenEnabledIdleAndAtLeastOneDayDue()
    {
        var now = DateTime.UtcNow;

        Assert.True(UpdateSchedule.IsDue(true, null, now, false));
        Assert.True(UpdateSchedule.IsDue(true, now.AddDays(-1), now, false));
        Assert.False(UpdateSchedule.IsDue(false, now.AddDays(-2), now, false));
        Assert.False(UpdateSchedule.IsDue(true, now.AddDays(-2), now, true));
        Assert.False(UpdateSchedule.IsDue(true, now.AddHours(-23), now, false));
    }
}
