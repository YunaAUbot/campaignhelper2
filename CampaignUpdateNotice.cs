// <copyright file="CampaignUpdateNotice.cs" company="None">Copyright (c) None.</copyright>
namespace CampaignHelper;

public static class CampaignUpdateNotice
{
    public static bool ShouldUseDetachedWindow(bool hasCandidate, bool canDrawCampaign)
    {
        return hasCandidate && !canDrawCampaign;
    }

    public static string? MessageFor(bool hasCandidate)
    {
        return hasCandidate ? "Guide update available - open CampaignHelper settings" : null;
    }
}
