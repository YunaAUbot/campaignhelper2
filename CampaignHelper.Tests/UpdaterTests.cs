namespace CampaignHelper.Tests;

using System.Net;
using System.Text;
using Xunit;

public sealed class UpdaterTests
{
    private const string Guide = "[[[\"enter areaida ;; A\"]]]";
    private const string Areas = "[[{\"id\":\"a\",\"name\":\"A\"}]]";
    private const string GuideUrl = "https://raw.githubusercontent.com/Lailloken/Exile-UI/main/data/english/%5Bleveltracker%5D%20default%20guide%202.json";
    private const string AreasUrl = "https://raw.githubusercontent.com/Lailloken/Exile-UI/main/data/english/%5Bleveltracker%5D%20areas%202.json";

    [Fact]
    public void HashIsDeterministicAndOrdered()
    {
        Assert.Equal(GuideUpdater.CombinedHash(Guide, Areas), GuideUpdater.CombinedHash(Guide, Areas));
        Assert.NotEqual(GuideUpdater.CombinedHash(Guide, Areas), GuideUpdater.CombinedHash(Areas, Guide));
    }

    [Fact]
    public void DefaultTransportDisablesRedirects()
    {
        Assert.False(GuideUpdater.CreateDefaultHandler().AllowAutoRedirect);
    }

    [Fact]
    public void UsesOnlyTheExactExileUiSourceUrls()
    {
        Assert.Equal(GuideUrl, GuideUpdater.GuideUrl);
        Assert.Equal(AreasUrl, GuideUpdater.AreasUrl);
        Assert.True(GuideUpdater.IsAllowed(new Uri(GuideUrl)));
        Assert.True(GuideUpdater.IsAllowed(new Uri(AreasUrl)));
    }

    [Theory]
    [InlineData("http://raw.githubusercontent.com/Lailloken/Exile-UI/main/data/english/%5Bleveltracker%5D%20areas%202.json")]
    [InlineData("https://evil.test/x")]
    [InlineData("https://raw.githubusercontent.com/Lailloken/Lailloken-UI/main/data/english/%5Bleveltracker%5D%20areas%202.json")]
    [InlineData("https://raw.githubusercontent.com/Lailloken/Exile-UI/main/LICENSE")]
    [InlineData("https://raw.githubusercontent.com/Lailloken/Exile-UI/main/data/english/%5Bleveltracker%5D%20areas%202.json?x=1")]
    public void UrlAllowlistRejectsEverythingElse(string value)
    {
        Assert.False(GuideUpdater.IsAllowed(new Uri(value)));
    }

    [Fact]
    public async Task CheckUsesExactlyTwoUrlsWithoutNetwork()
    {
        var handler = new FakeHandler(request => Ok(request.RequestUri!.AbsoluteUri == GuideUpdater.GuideUrl ? Guide : Areas));
        using var updater = new GuideUpdater(handler);

        var candidate = await updater.CheckAsync();

        Assert.Equal(new[] { GuideUpdater.GuideUrl, GuideUpdater.AreasUrl }, handler.Requests);
        Assert.All(handler.UserAgents, value => Assert.Equal("GameHelper2-CampaignHelper/1.0", value));
        Assert.NotEmpty(candidate.Hash);
    }

    [Fact]
    public async Task RejectsDeclaredContentLengthOverLimit()
    {
        using var updater = new GuideUpdater(new FakeHandler(_ =>
        {
            var response = Ok("small");
            response.Content.Headers.ContentLength = 2_000_001;
            return response;
        }));

        await Assert.ThrowsAsync<InvalidDataException>(() => updater.CheckAsync());
    }

    [Fact]
    public async Task RejectsStreamedContentOverLimitWithoutLength()
    {
        using var updater = new GuideUpdater(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(new byte[2_000_001]),
        }));

        await Assert.ThrowsAsync<InvalidDataException>(() => updater.CheckAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task RejectsEveryNonSuccessStatus(HttpStatusCode status)
    {
        using var updater = new GuideUpdater(new FakeHandler(_ => new HttpResponseMessage(status)));

        await Assert.ThrowsAsync<HttpRequestException>(() => updater.CheckAsync());
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        using var updater = new GuideUpdater(new CancellingHandler());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updater.CheckAsync(cancellation.Token));
    }

    [Fact]
    public void ApplyRejectsHashMismatchBeforeWriting()
    {
        using var directory = new TemporaryDirectory();
        Seed(directory.Path, Guide, Areas, "old metadata");
        Assert.Throws<InvalidDataException>(() => GuideUpdater.Apply(directory.Path, Candidate(Guide, Areas) with { Hash = "wrong" }));
        Assert.Equal("old metadata", File.ReadAllText(System.IO.Path.Combine(directory.Path, "metadata.json")));
        AssertNoTemporaryDebris(directory.Path);
    }

    [Fact]
    public void SuccessfulUpdatesKeepOnlyLatestBackupOfImmediatelyPreviousInstalledSet()
    {
        using var directory = new TemporaryDirectory();
        Seed(directory.Path, Guide, Areas, "old metadata");
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "guide.json.bak"), "preexisting backup");
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "areas.json.bak"), "preexisting backup");
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "metadata.json.bak"), "preexisting backup");
        var secondGuide = "[[[\"second areaida\"]]]";
        var thirdGuide = "[[[\"third areaida\"]]]";

        GuideUpdater.Apply(directory.Path, Candidate(secondGuide, Areas));
        AssertBackup(directory.Path, Guide, Areas, "old metadata");
        var secondMetadata = File.ReadAllText(System.IO.Path.Combine(directory.Path, "metadata.json"));

        var installed = GuideUpdater.Apply(directory.Path, Candidate(thirdGuide, Areas));

        Assert.Equal("Third A", installed.PagesFor(new(true, true)).Single().Lines.Single());
        Assert.Equal(thirdGuide, File.ReadAllText(System.IO.Path.Combine(directory.Path, "guide.json")));
        AssertBackup(directory.Path, secondGuide, Areas, secondMetadata);
        Assert.Equal("preexisting backup", File.ReadAllText(System.IO.Path.Combine(directory.Path, "metadata.json.bak")));
        Assert.Single(Directory.EnumerateDirectories(System.IO.Path.Combine(directory.Path, "Backups")));
        AssertNoTemporaryDebris(directory.Path);
    }

    [Fact]
    public void FailedSwapPreservesLiveSetAndPreviousSuccessfulLatestBackupIncludingMetadata()
    {
        using var directory = new TemporaryDirectory();
        const string originalMetadata = "{\"version\":\"original\"}";
        Seed(directory.Path, Guide, Areas, originalMetadata);
        var secondGuide = "[[[\"second areaida\"]]]";
        GuideUpdater.Apply(directory.Path, Candidate(secondGuide, Areas));
        var installedMetadata = File.ReadAllText(System.IO.Path.Combine(directory.Path, "metadata.json"));
        var failedGuide = "[[[\"failed areaida\"]]]";

        Assert.Throws<IOException>(() =>
            GuideUpdater.Apply(directory.Path, Candidate(failedGuide, Areas), new FailAtReplace(2)));

        Assert.Equal(secondGuide, File.ReadAllText(System.IO.Path.Combine(directory.Path, "guide.json")));
        Assert.Equal(Areas, File.ReadAllText(System.IO.Path.Combine(directory.Path, "areas.json")));
        Assert.Equal(installedMetadata, File.ReadAllText(System.IO.Path.Combine(directory.Path, "metadata.json")));
        AssertBackup(directory.Path, Guide, Areas, originalMetadata);
        AssertNoTemporaryDebris(directory.Path);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FailureAtEverySwapRestoresGuideAreasAndMetadata(int failurePosition)
    {
        using var directory = new TemporaryDirectory();
        const string oldMetadata = "{\"Hash\":\"old\"}";
        Seed(directory.Path, Guide, Areas, oldMetadata);
        var newerGuide = "[[[\"new areaida\"]]]";

        Assert.Throws<IOException>(() => GuideUpdater.Apply(directory.Path, Candidate(newerGuide, Areas), new FailAtReplace(failurePosition)));

        Assert.Equal(Guide, File.ReadAllText(System.IO.Path.Combine(directory.Path, "guide.json")));
        Assert.Equal(Areas, File.ReadAllText(System.IO.Path.Combine(directory.Path, "areas.json")));
        Assert.Equal(oldMetadata, File.ReadAllText(System.IO.Path.Combine(directory.Path, "metadata.json")));
        AssertNoTemporaryDebris(directory.Path);
    }

    [Fact]
    public void FailedBackupPublicationRetainsPreviousPersistentBackupWhenRestoreAlsoFails()
    {
        using var directory = new TemporaryDirectory();
        Seed(directory.Path, Guide, Areas, "old metadata");
        var latest = System.IO.Path.Combine(directory.Path, "Backups", "latest");
        Directory.CreateDirectory(latest);
        File.WriteAllText(System.IO.Path.Combine(latest, "marker.txt"), "previous backup");

        Assert.Throws<IOException>(() =>
            GuideUpdater.Apply(directory.Path, Candidate("[[[\"new areaida\"]]]", Areas), new FailBackupSwapAndRestore()));

        var preserved = Assert.Single(Directory.EnumerateDirectories(
            System.IO.Path.Combine(directory.Path, "Backups"),
            "latest.bak.*"));
        Assert.Equal("previous backup", File.ReadAllText(System.IO.Path.Combine(preserved, "marker.txt")));
    }

    [Fact]
    public void RollbackFailureRetainsRecoveryArtifactsAndReportsBothFailures()
    {
        using var directory = new TemporaryDirectory();
        Seed(directory.Path, Guide, Areas, "old metadata");
        var newerGuide = "[[[\"new areaida\"]]]";

        var exception = Assert.Throws<IOException>(() =>
            GuideUpdater.Apply(directory.Path, Candidate(newerGuide, Areas), new FailApplyAndRestore()));

        Assert.Contains("Injected apply failure.", exception.Message);
        Assert.Contains("Injected rollback failure.", exception.Message);
        Assert.True(Directory.Exists(System.IO.Path.Combine(directory.Path, "Backups", "latest")));
        Assert.Equal(2, Directory.EnumerateFiles(directory.Path, "*.bak.*").Count());
        Assert.DoesNotContain(Directory.EnumerateFiles(directory.Path), path => path.Contains(".tmp."));
    }

    private static UpdateCandidate Candidate(string guide, string areas)
    {
        return new(guide, areas, GuideUpdater.CombinedHash(guide, areas), "g", "a", null);
    }

    private static HttpResponseMessage Ok(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
    }

    private static void Seed(string directory, string guide, string areas, string metadata)
    {
        File.WriteAllText(System.IO.Path.Combine(directory, "guide.json"), guide);
        File.WriteAllText(System.IO.Path.Combine(directory, "areas.json"), areas);
        File.WriteAllText(System.IO.Path.Combine(directory, "metadata.json"), metadata);
    }

    private static void AssertNoTemporaryDebris(string directory)
    {
        Assert.DoesNotContain(
            Directory.EnumerateFiles(directory),
            path => path.Contains(".tmp.") || path.Contains(".bak."));
        var backups = System.IO.Path.Combine(directory, "Backups");
        if (Directory.Exists(backups))
        {
            Assert.DoesNotContain(
                Directory.EnumerateFileSystemEntries(backups),
                path => System.IO.Path.GetFileName(path) != "latest");
        }
    }

    private static void AssertBackup(string directory, string guide, string areas, string metadata)
    {
        var latest = System.IO.Path.Combine(directory, "Backups", "latest");
        Assert.Equal(guide, File.ReadAllText(System.IO.Path.Combine(latest, "guide.json")));
        Assert.Equal(areas, File.ReadAllText(System.IO.Path.Combine(latest, "areas.json")));
        Assert.Equal(metadata, File.ReadAllText(System.IO.Path.Combine(latest, "metadata.json")));
        Assert.Equal(3, Directory.EnumerateFiles(latest).Count());
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            return Task.FromResult(factory(request));
        }
    }

    private sealed class CancellingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(bytes).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class FailAtReplace(int failurePosition) : IGuideFileOps
    {
        private int position;

        public void Replace(string source, string destination, string backup)
        {
            if (++position == failurePosition)
            {
                throw new IOException("Injected swap failure.");
            }

            File.Replace(source, destination, backup, true);
        }
    }

    private sealed class FailBackupSwapAndRestore : IGuideFileOps
    {
        private int directoryMoves;

        public void Replace(string source, string destination, string backup)
        {
            File.Replace(source, destination, backup, true);
        }

        public void MoveDirectory(string source, string destination)
        {
            directoryMoves++;
            if (directoryMoves is 2 or 3)
            {
                throw new IOException("Injected backup directory move failure.");
            }

            Directory.Move(source, destination);
        }
    }

    private sealed class FailApplyAndRestore : IGuideFileOps
    {
        private int replacements;

        public void Replace(string source, string destination, string backup)
        {
            if (++replacements == 3)
            {
                throw new IOException("Injected apply failure.");
            }

            File.Replace(source, destination, backup, true);
        }

        public void Move(string source, string destination, bool overwrite)
        {
            throw new IOException("Injected rollback failure.");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
