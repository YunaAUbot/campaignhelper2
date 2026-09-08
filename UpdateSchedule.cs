// <copyright file="UpdateSchedule.cs" company="None">Copyright (c) None.</copyright>
namespace CampaignHelper;

public static class UpdateSchedule
{
    public static bool IsDue(bool enabled, DateTime? lastCheckedUtc, DateTime nowUtc, bool checkRunning)
    {
        return enabled &&
            !checkRunning &&
            (!lastCheckedUtc.HasValue || nowUtc - lastCheckedUtc.Value >= TimeSpan.FromDays(1));
    }
}
