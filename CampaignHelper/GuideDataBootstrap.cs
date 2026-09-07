// <copyright file="GuideDataBootstrap.cs" company="None">Copyright (c) None.</copyright>
namespace CampaignHelper;

public static class GuideDataBootstrap
{
    public static void EnsureActiveData(string bundledDirectory, string activeDirectory)
    {
        Directory.CreateDirectory(activeDirectory);
        var guideTarget = Path.Combine(activeDirectory, "guide.json");
        var areasTarget = Path.Combine(activeDirectory, "areas.json");
        var guideExists = File.Exists(guideTarget);
        var areasExists = File.Exists(areasTarget);
        if (guideExists && areasExists)
        {
            return;
        }

        if (guideExists || areasExists)
        {
            throw new InvalidDataException("Active campaign data is an incomplete pair.");
        }

        var guide = BoundedTextFile.Read(
            Path.Combine(bundledDirectory, "guide.json"),
            BoundedTextFile.GuideMaxBytes);
        var areas = BoundedTextFile.Read(
            Path.Combine(bundledDirectory, "areas.json"),
            BoundedTextFile.GuideMaxBytes);
        var nonce = Guid.NewGuid().ToString("N");
        var guideTemporary = Path.Combine(activeDirectory, $"guide.json.tmp.{nonce}");
        var areasTemporary = Path.Combine(activeDirectory, $"areas.json.tmp.{nonce}");
        var guidePublished = false;
        try
        {
            File.WriteAllText(guideTemporary, guide);
            File.WriteAllText(areasTemporary, areas);
            File.Move(guideTemporary, guideTarget);
            guidePublished = true;
            File.Move(areasTemporary, areasTarget);
        }
        catch
        {
            if (guidePublished && File.Exists(guideTarget))
            {
                File.Delete(guideTarget);
            }

            throw;
        }
        finally
        {
            File.Delete(guideTemporary);
            File.Delete(areasTemporary);
        }
    }
}
