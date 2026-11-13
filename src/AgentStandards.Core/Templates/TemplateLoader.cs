using System.IO.Compression;
using System.Text;
using AgentStandards.Core.IO;

namespace AgentStandards.Core.Templates;

/// <summary>Reads template files into memory from a directory or a .nupkg, never extracting to disk.</summary>
public static class TemplateLoader
{
    /// <summary>Folder inside the templates package that holds the template tree. Deliberately not <c>content/</c> or
    /// <c>contentFiles/</c>, so NuGet never injects these files into a consuming project.</summary>
    public const string PackageFolder = "agent-standards";

    public const long MaxFileBytes = 1024 * 1024;
    public const long MaxTotalBytes = 32 * 1024 * 1024;
    public const int MaxFiles = 4000;

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    public static Dictionary<string, string> ReadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            throw new TemplateException($"Template directory '{directory}' does not exist.");
        if (SafePaths.IsLink(directory))
            throw new TemplateException($"Template directory '{directory}' is a symbolic link; refusing to read it.");

        var root = Path.GetFullPath(directory);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", new EnumerationOptions
                 {
                     RecurseSubdirectories = true,
                     AttributesToSkip = 0,
                     IgnoreInaccessible = false
                 }))
        {
            var rel = Path.GetRelativePath(root, entry.FullName).Replace(Path.DirectorySeparatorChar, '/');
            if (rel.Split('/').Any(s => s.StartsWith('.')))
                continue;
            if (entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new TemplateException($"Template entry '{rel}' is a symbolic link; refusing to read it.");
            if (entry is not FileInfo file)
                continue;

            var normalized = NormalizeEntry(rel);
            AddUnique(files, normalized, rel);
            if (file.Length > MaxFileBytes)
                throw new TemplateException($"Template file '{rel}' exceeds {MaxFileBytes} bytes.");
            total += file.Length;
            CheckLimits(files.Count + 1, total);
            files[normalized] = Decode(File.ReadAllBytes(file.FullName), rel);
        }

        return files;
    }

    public static Dictionary<string, string> ReadPackage(Stream nupkg)
    {
        try
        {
            return ReadPackageCore(nupkg);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or ArgumentException)
        {
            throw new TemplateException($"Package is not a valid zip archive: {ex.Message}", ex);
        }
    }

    private static Dictionary<string, string> ReadPackageCore(Stream nupkg)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        long total = 0;
        using var zip = new ZipArchive(nupkg, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(PackageFolder + "/", StringComparison.OrdinalIgnoreCase) || name.EndsWith('/'))
                continue;

            var rel = name[(PackageFolder.Length + 1)..];
            var normalized = NormalizeEntry(rel);
            AddUnique(files, normalized, name);

            // Unix symlinks stored in zip external attributes (S_IFLNK).
            var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixMode == 0xA000)
                throw new TemplateException($"Package entry '{name}' is a symbolic link; refusing to read it.");
            if (entry.Length > MaxFileBytes)
                throw new TemplateException($"Package entry '{name}' exceeds {MaxFileBytes} bytes.");

            total += entry.Length;
            CheckLimits(files.Count + 1, total);

            // Do not trust the header's size: read at most MaxFileBytes + 1 bytes.
            using var stream = entry.Open();
            var bytes = ReadBounded(stream, MaxFileBytes, name);
            files[normalized] = Decode(bytes, name);
        }

        if (files.Count == 0)
            throw new TemplateException($"Package does not contain a '{PackageFolder}/' template folder.");
        return files;
    }

    /// <summary>
    /// Rejects duplicate entries and paths that differ only by case, which would silently overwrite each other or
    /// collide on case-insensitive file systems (Windows, default macOS).
    /// </summary>
    private static void AddUnique(Dictionary<string, string> files, string normalized, string original)
    {
        if (files.ContainsKey(normalized))
            throw new TemplateException($"Template entry '{original}' appears more than once.");
        var collision = files.Keys.FirstOrDefault(k => string.Equals(k, normalized, StringComparison.OrdinalIgnoreCase));
        if (collision is not null)
            throw new TemplateException($"Template entries '{collision}' and '{normalized}' differ only by case.");
        files[normalized] = "";
    }

    private static byte[] ReadBounded(Stream stream, long max, string name)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > max)
                throw new TemplateException($"Package entry '{name}' exceeds {max} bytes.");
        }
        return buffer.ToArray();
    }

    private static string NormalizeEntry(string rel)
    {
        try
        {
            return SafePaths.NormalizeRelative(rel);
        }
        catch (UnsafePathException ex)
        {
            throw new TemplateException($"Template entry is unsafe: {ex.Message}", ex);
        }
    }

    private static void CheckLimits(int count, long total)
    {
        if (count > MaxFiles)
            throw new TemplateException($"Template source contains more than {MaxFiles} files.");
        if (total > MaxTotalBytes)
            throw new TemplateException($"Template source exceeds {MaxTotalBytes} bytes.");
    }

    private static string Decode(byte[] bytes, string name)
    {
        try
        {
            var text = StrictUtf8.GetString(bytes);
            return text.StartsWith('﻿') ? text[1..] : text;
        }
        catch (DecoderFallbackException ex)
        {
            throw new TemplateException($"Template file '{name}' is not valid UTF-8 text.", ex);
        }
    }
}
