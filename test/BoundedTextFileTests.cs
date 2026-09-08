namespace CampaignHelper.Tests;

using System.Text;
using Xunit;

public sealed class BoundedTextFileTests
{
    [Fact]
    public void ReadsUtf8AtExactByteBound()
    {
        using var file = new TemporaryFile();
        var text = new string('a', BoundedTextFile.GuideMaxBytes);
        File.WriteAllText(file.Path, text, new UTF8Encoding(false));

        Assert.Equal(text, BoundedTextFile.Read(file.Path, BoundedTextFile.GuideMaxBytes));
    }

    [Fact]
    public void RejectsFileAboveByteBound()
    {
        using var file = new TemporaryFile();
        using (var stream = new FileStream(file.Path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(BoundedTextFile.GuideMaxBytes + 1L);
        }

        Assert.Throws<InvalidDataException>(() =>
            BoundedTextFile.Read(file.Path, BoundedTextFile.GuideMaxBytes));
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
