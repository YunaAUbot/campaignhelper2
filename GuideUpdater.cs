// <copyright file="GuideUpdater.cs" company="None">Copyright (c) None.</copyright>
namespace CampaignHelper;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

public sealed record UpdateCandidate(
    string Guide,
    string Areas,
    string Hash,
    string? GuideEtag,
    string? AreasEtag,
    DateTimeOffset? Modified);

public sealed record GuideMetadata(
    string GuideUrl,
    string AreasUrl,
    string Hash,
    DateTime InstalledUtc,
    DateTime? CheckedUtc,
    string? GuideEtag,
    string? AreasEtag,
    DateTimeOffset? LastModified);

public interface IGuideFileOps
{
    void Replace(string source, string destination, string backup);

    void MoveDirectory(string source, string destination)
    {
        Directory.Move(source, destination);
    }

    void Move(string source, string destination, bool overwrite)
    {
        File.Move(source, destination, overwrite);
    }
}

public sealed class RealGuideFileOps : IGuideFileOps
{
    public void Replace(string source, string destination, string backup)
    {
        File.Replace(source, destination, backup, true);
    }

    public void Move(string source, string destination, bool overwrite)
    {
        File.Move(source, destination, overwrite);
    }
}

public sealed class GuideUpdater : IDisposable
{
    public const string GuideUrl = "https://raw.githubusercontent.com/Lailloken/Exile-UI/main/data/english/%5Bleveltracker%5D%20default%20guide%202.json";
    public const string AreasUrl = "https://raw.githubusercontent.com/Lailloken/Exile-UI/main/data/english/%5Bleveltracker%5D%20areas%202.json";
    private const int MaxBytes = 2_000_000;
    private readonly HttpClient client;
    private readonly CancellationTokenSource lifetime = new();

    public GuideUpdater()
        : this(CreateDefaultHandler())
    {
    }

    public GuideUpdater(HttpMessageHandler handler)
    {
        client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = MaxBytes,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GameHelper2-CampaignHelper/1.0");
    }

    public static HttpClientHandler CreateDefaultHandler()
    {
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
    }

    public static bool IsAllowed(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.IsDefaultPort)
        {
            return false;
        }

        return uri.AbsolutePath is
            "/Lailloken/Exile-UI/main/data/english/%5Bleveltracker%5D%20default%20guide%202.json" or
            "/Lailloken/Exile-UI/main/data/english/%5Bleveltracker%5D%20areas%202.json";
    }

    public async Task<UpdateCandidate> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        var guide = await Fetch(GuideUrl, linked.Token).ConfigureAwait(false);
        var areas = await Fetch(AreasUrl, linked.Token).ConfigureAwait(false);
        CampaignGuide.Parse(guide.Body, areas.Body);
        var modified = new[] { guide.Modified, areas.Modified }.Where(value => value.HasValue).Max();
        return new UpdateCandidate(
            guide.Body,
            areas.Body,
            CombinedHash(guide.Body, areas.Body),
            guide.Etag,
            areas.Etag,
            modified);
    }

    public static string CombinedHash(string guide, string areas)
    {
        var bytes = Encoding.UTF8.GetBytes(guide + "\n--CAMPAIGN-AREAS--\n" + areas);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static CampaignGuide Apply(string dataDirectory, UpdateCandidate candidate, IGuideFileOps? ops = null)
    {
        if (!string.Equals(candidate.Hash, CombinedHash(candidate.Guide, candidate.Areas), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Candidate hash does not match its content.");
        }

        ops ??= new RealGuideFileOps();
        Directory.CreateDirectory(dataDirectory);
        var nonce = Guid.NewGuid().ToString("N");
        var targets = new[]
        {
            Path.Combine(dataDirectory, "guide.json"),
            Path.Combine(dataDirectory, "areas.json"),
            Path.Combine(dataDirectory, "metadata.json"),
        };
        var temporary = targets.Select(path => path + ".tmp." + nonce).ToArray();
        var backups = targets.Select(path => path + ".bak." + nonce).ToArray();
        var metadata = new GuideMetadata(
            GuideUrl,
            AreasUrl,
            candidate.Hash,
            DateTime.UtcNow,
            DateTime.UtcNow,
            candidate.GuideEtag,
            candidate.AreasEtag,
            candidate.Modified);
        var swapped = new List<(int Index, bool HadOriginal)>();
        BackupTransaction? backupTransaction = null;
        var preserveRecoveryArtifacts = false;

        try
        {
            WriteDurable(temporary[0], candidate.Guide);
            WriteDurable(temporary[1], candidate.Areas);
            _ = CampaignGuide.Parse(
                BoundedTextFile.Read(temporary[0], BoundedTextFile.GuideMaxBytes),
                BoundedTextFile.Read(temporary[1], BoundedTextFile.GuideMaxBytes));
            WriteDurable(temporary[2], JsonConvert.SerializeObject(metadata, Formatting.Indented));
            ValidateMetadata(temporary[2], candidate.Hash);
            backupTransaction = PublishLatestBackup(dataDirectory, targets, nonce, ops);

            for (var index = 0; index < targets.Length; index++)
            {
                var hadOriginal = File.Exists(targets[index]);
                if (hadOriginal)
                {
                    ops.Replace(temporary[index], targets[index], backups[index]);
                }
                else
                {
                    File.Move(temporary[index], targets[index]);
                }

                swapped.Add((index, hadOriginal));
            }

            var installedGuide = BoundedTextFile.Read(targets[0], BoundedTextFile.GuideMaxBytes);
            var installedAreas = BoundedTextFile.Read(targets[1], BoundedTextFile.GuideMaxBytes);
            var installed = CampaignGuide.Parse(installedGuide, installedAreas);
            if (!string.Equals(CombinedHash(installedGuide, installedAreas), candidate.Hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Installed campaign data failed hash verification.");
            }

            ValidateMetadata(targets[2], candidate.Hash);
            CommitLatestBackup(backupTransaction);
            return installed;
        }
        catch (Exception applyException)
        {
            try
            {
                RollBack(swapped, targets, backups, ops);
                RestorePreviousLatestBackup(backupTransaction);
            }
            catch (Exception rollbackException)
            {
                preserveRecoveryArtifacts = true;
                throw new IOException(
                    $"Apply failed: {applyException.Message} Rollback failed: {rollbackException.Message}",
                    new AggregateException(applyException, rollbackException));
            }

            throw;
        }
        finally
        {
            foreach (var path in temporary)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            if (!preserveRecoveryArtifacts)
            {
                foreach (var path in backups)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
        }
    }

    private static BackupTransaction? PublishLatestBackup(
        string dataDirectory,
        IReadOnlyList<string> liveFiles,
        string nonce,
        IGuideFileOps ops)
    {
        if (!File.Exists(liveFiles[0]) || !File.Exists(liveFiles[1]))
        {
            return null;
        }

        var backupRoot = Path.Combine(dataDirectory, "Backups");
        var latest = Path.Combine(backupRoot, "latest");
        var staged = Path.Combine(backupRoot, "latest.tmp." + nonce);
        var previous = Path.Combine(backupRoot, "latest.bak." + nonce);
        Directory.CreateDirectory(backupRoot);

        try
        {
            Directory.CreateDirectory(staged);
            for (var index = 0; index < liveFiles.Count; index++)
            {
                if (File.Exists(liveFiles[index]))
                {
                    File.Copy(liveFiles[index], Path.Combine(staged, Path.GetFileName(liveFiles[index])));
                }
            }

            var hadPrevious = Directory.Exists(latest);
            if (hadPrevious)
            {
                ops.MoveDirectory(latest, previous);
            }

            try
            {
                ops.MoveDirectory(staged, latest);
            }
            catch
            {
                if (hadPrevious && Directory.Exists(previous) && !Directory.Exists(latest))
                {
                    ops.MoveDirectory(previous, latest);
                }

                throw;
            }

            return new BackupTransaction(latest, staged, previous, hadPrevious);
        }
        catch
        {
            DeleteDirectoryIfExists(staged);
            throw;
        }
    }

    private static void CommitLatestBackup(BackupTransaction? transaction)
    {
        if (transaction is not null)
        {
            DeleteDirectoryIfExists(transaction.Previous);
        }
    }

    private static void RestorePreviousLatestBackup(BackupTransaction? transaction)
    {
        if (transaction is null)
        {
            return;
        }

        DeleteDirectoryIfExists(transaction.Latest);
        if (transaction.HadPrevious && Directory.Exists(transaction.Previous))
        {
            Directory.Move(transaction.Previous, transaction.Latest);
        }

        DeleteDirectoryIfExists(transaction.Staged);
        DeleteDirectoryIfExists(transaction.Previous);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private async Task<FetchResult> Fetch(string url, CancellationToken token)
    {
        var uri = new Uri(url);
        if (!IsAllowed(uri))
        {
            throw new InvalidOperationException("URL is not allowlisted.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxBytes)
        {
            throw new InvalidDataException("Response is too large.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var limited = new MemoryStream();
        var buffer = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaxBytes)
            {
                throw new InvalidDataException("Response is too large.");
            }

            limited.Write(buffer, 0, read);
        }

        return new FetchResult(
            Encoding.UTF8.GetString(limited.ToArray()),
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified);
    }

    private static void ValidateMetadata(string path, string expectedHash)
    {
        var metadata = JsonConvert.DeserializeObject<GuideMetadata>(
            BoundedTextFile.Read(path, BoundedTextFile.SmallMaxBytes))
            ?? throw new InvalidDataException("Installed metadata is invalid.");
        if (!string.Equals(metadata.Hash, expectedHash, StringComparison.OrdinalIgnoreCase) ||
            metadata.GuideUrl != GuideUrl || metadata.AreasUrl != AreasUrl)
        {
            throw new InvalidDataException("Installed metadata does not match campaign data.");
        }
    }

    private static void RollBack(
        IEnumerable<(int Index, bool HadOriginal)> swapped,
        IReadOnlyList<string> targets,
        IReadOnlyList<string> backups,
        IGuideFileOps ops)
    {
        foreach (var entry in swapped.Reverse())
        {
            if (entry.HadOriginal && File.Exists(backups[entry.Index]))
            {
                ops.Move(backups[entry.Index], targets[entry.Index], true);
            }
            else if (!entry.HadOriginal && File.Exists(targets[entry.Index]))
            {
                File.Delete(targets[entry.Index]);
            }
        }
    }

    private static void WriteDurable(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        writer.Write(text);
        writer.Flush();
        stream.Flush(true);
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
        client.Dispose();
    }

    private sealed record FetchResult(string Body, string? Etag, DateTimeOffset? Modified);

    private sealed record BackupTransaction(
        string Latest,
        string Staged,
        string Previous,
        bool HadPrevious);
}
