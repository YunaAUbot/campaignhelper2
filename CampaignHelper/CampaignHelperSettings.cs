// <copyright file="CampaignHelperSettings.cs" company="None">Copyright (c) None.</copyright>
namespace CampaignHelper;

using GameHelper.Plugin;

public sealed class CampaignHelperSettings : IPSettings
{
    public bool LeagueStart = true;
    public bool IncludeOptional = true;
    public bool Paused;
    public bool AutomaticDailyUpdateCheck = true;
    public int StabilizationMilliseconds = 1200;
    public int ProgressIndex;
    public bool ProgressInitialized;
    public string? LastReachedAreaId;
    public int? CurrentAnchorAct;
    public int? CurrentAnchorSourceIndex;
    public int? LastReachedAnchorAct;
    public int? LastReachedAnchorSourceIndex;
    public DateTime? LastCheckedUtc;
}
