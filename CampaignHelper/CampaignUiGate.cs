// <copyright file="CampaignUiGate.cs" company="None">Copyright (c) None.</copyright>
namespace CampaignHelper;

public static class CampaignUiGate
{
    public static bool CanDraw(bool isInGame, string? areaId, IReadOnlySet<string> knownAreaIds)
    {
        return isInGame && !string.IsNullOrWhiteSpace(areaId) && knownAreaIds.Contains(areaId);
    }
}
