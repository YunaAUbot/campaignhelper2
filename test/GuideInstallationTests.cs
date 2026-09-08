namespace CampaignHelper.Tests;

using Xunit;

public sealed class GuideInstallationTests
{
    [Fact]
    public void InstalledHashIsUnavailableForMissingOrCorruptPair()
    {
        using var directory = new TemporaryDirectory();
        Assert.Null(GuideInstallation.InstalledHash(directory.Path));

        File.WriteAllText(Path.Combine(directory.Path, "guide.json"), "not json");
        File.WriteAllText(Path.Combine(directory.Path, "areas.json"), "also not json");
        Assert.Null(GuideInstallation.InstalledHash(directory.Path));
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
