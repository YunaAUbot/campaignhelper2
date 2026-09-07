namespace CampaignHelper.Tests;

using Xunit;

public sealed class GuideDataBootstrapTests
{
    [Fact]
    public void CreatesMissingActiveDataOnceWithoutOverwritingActiveRecoveryState()
    {
        using var root = new TemporaryDirectory();
        var bundled = Path.Combine(root.Path, "Data");
        var active = Path.Combine(root.Path, "config", "guide-data");
        Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "guide.json"), "bundled guide");
        File.WriteAllText(Path.Combine(bundled, "areas.json"), "bundled areas");

        GuideDataBootstrap.EnsureActiveData(bundled, active);

        Assert.Equal("bundled guide", File.ReadAllText(Path.Combine(active, "guide.json")));
        Assert.Equal("bundled areas", File.ReadAllText(Path.Combine(active, "areas.json")));

        File.WriteAllText(Path.Combine(active, "guide.json"), "active guide");
        File.WriteAllText(Path.Combine(active, "areas.json"), "active areas");
        File.WriteAllText(Path.Combine(active, "metadata.json"), "active metadata");
        var backup = Path.Combine(active, "Backups", "latest");
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "guide.json"), "backup guide");
        File.WriteAllText(Path.Combine(bundled, "guide.json"), "new bundled guide");
        File.WriteAllText(Path.Combine(bundled, "areas.json"), "new bundled areas");

        GuideDataBootstrap.EnsureActiveData(bundled, active);

        Assert.Equal("active guide", File.ReadAllText(Path.Combine(active, "guide.json")));
        Assert.Equal("active areas", File.ReadAllText(Path.Combine(active, "areas.json")));
        Assert.Equal("active metadata", File.ReadAllText(Path.Combine(active, "metadata.json")));
        Assert.Equal("backup guide", File.ReadAllText(Path.Combine(backup, "guide.json")));
    }

    [Theory]
    [InlineData("guide.json")]
    [InlineData("areas.json")]
    public void RejectsPartialActivePairWithoutFillingMissingHalf(string existingName)
    {
        using var root = new TemporaryDirectory();
        var bundled = Path.Combine(root.Path, "Data");
        var active = Path.Combine(root.Path, "active");
        Directory.CreateDirectory(bundled);
        Directory.CreateDirectory(active);
        File.WriteAllText(Path.Combine(bundled, "guide.json"), "bundled guide");
        File.WriteAllText(Path.Combine(bundled, "areas.json"), "bundled areas");
        File.WriteAllText(Path.Combine(active, existingName), "active value");

        Assert.Throws<InvalidDataException>(() => GuideDataBootstrap.EnsureActiveData(bundled, active));

        Assert.Single(Directory.EnumerateFiles(active));
        Assert.Equal("active value", File.ReadAllText(Path.Combine(active, existingName)));
    }

    [Fact]
    public void FailedBundledPairReadPublishesNoMixedActivePair()
    {
        using var root = new TemporaryDirectory();
        var bundled = Path.Combine(root.Path, "Data");
        var active = Path.Combine(root.Path, "active");
        Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "guide.json"), "bundled guide");

        Assert.ThrowsAny<IOException>(() => GuideDataBootstrap.EnsureActiveData(bundled, active));

        Assert.False(File.Exists(Path.Combine(active, "guide.json")));
        Assert.False(File.Exists(Path.Combine(active, "areas.json")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
