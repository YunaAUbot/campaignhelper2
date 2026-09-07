// <copyright file="BoundedTextFile.cs" company="None">Copyright (c) None.</copyright>
namespace CampaignHelper;

using System.Text;

public static class BoundedTextFile
{
    public const int GuideMaxBytes = 2_000_000;
    public const int SmallMaxBytes = 256 * 1024;

    public static string Read(string path, int maxBytes)
    {
        if (maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidDataException("Path must be a regular file.");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maxBytes)
        {
            throw new InvalidDataException($"File exceeds the {maxBytes}-byte limit.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException($"File exceeds the {maxBytes}-byte limit.");
        }

        return new UTF8Encoding(false, true).GetString(bytes);
    }
}
