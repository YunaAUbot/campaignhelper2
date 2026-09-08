// <copyright file="GuideInstallation.cs" company="None">Copyright (c) None.</copyright>
namespace CampaignHelper;

public static class GuideInstallation
{
    public static string? InstalledHash(string dataDirectory)
    {
        try
        {
            var guide = BoundedTextFile.Read(
                Path.Combine(dataDirectory, "guide.json"),
                BoundedTextFile.GuideMaxBytes);
            var areas = BoundedTextFile.Read(
                Path.Combine(dataDirectory, "areas.json"),
                BoundedTextFile.GuideMaxBytes);
            _ = CampaignGuide.Parse(guide, areas);
            return GuideUpdater.CombinedHash(guide, areas);
        }
        catch
        {
            return null;
        }
    }
}
