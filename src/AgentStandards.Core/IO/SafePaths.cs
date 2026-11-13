namespace AgentStandards.Core.IO;

/// <summary>
/// Validates relative paths coming from templates, manifests and backups, and guards against
/// path traversal and writes through symbolic links or junctions.
/// </summary>
public static class SafePaths
{
    private static readonly char[] InvalidSegmentChars = ['\0', ':', '*', '?', '"', '<', '>', '|'];

    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Returns a normalised forward-slash relative path or throws <see cref="UnsafePathException"/>.</summary>
    public static string NormalizeRelative(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new UnsafePathException(relativePath ?? "", "path is empty");

        var p = relativePath.Replace('\\', '/');
        if (p.StartsWith('/') || Path.IsPathRooted(relativePath) || (p.Length >= 2 && p[1] == ':'))
            throw new UnsafePathException(relativePath, "absolute paths are not allowed");

        var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new UnsafePathException(relativePath, "path is empty");

        foreach (var part in parts)
        {
            if (part is "." or "..")
                throw new UnsafePathException(relativePath, "relative segments ('.' or '..') are not allowed");
            if (part.IndexOfAny(InvalidSegmentChars) >= 0 || part.Any(char.IsControl))
                throw new UnsafePathException(relativePath, "path contains invalid characters");
            if (part.EndsWith(' ') || part.EndsWith('.'))
                throw new UnsafePathException(relativePath, "path segments may not end with a space or dot");
        }

        return string.Join('/', parts);
    }

    public static bool TryNormalizeRelative(string relativePath, out string normalized)
    {
        try
        {
            normalized = NormalizeRelative(relativePath);
            return true;
        }
        catch (UnsafePathException)
        {
            normalized = "";
            return false;
        }
    }

    /// <summary>Resolves a relative path under <paramref name="root"/>, rejecting anything that escapes it.</summary>
    public static string Resolve(string root, string relativePath)
    {
        var normalized = NormalizeRelative(relativePath);
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(rootFull, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = rootFull + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, PathComparison))
            throw new UnsafePathException(relativePath, "path escapes the target root");
        return full;
    }

    /// <summary>
    /// Throws if any existing component between <paramref name="root"/> (exclusive) and <paramref name="fullPath"/>
    /// (inclusive) is a symbolic link, junction or other reparse point.
    /// </summary>
    public static void EnsureNoLinks(string root, string fullPath)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var relative = Path.GetRelativePath(rootFull, fullPath);
        var current = rootFull;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsLink(current))
                throw new UnsafePathException(relative.Replace(Path.DirectorySeparatorChar, '/'),
                    $"'{Path.GetRelativePath(rootFull, current).Replace(Path.DirectorySeparatorChar, '/')}' is a symbolic link or reparse point");
            if (!File.Exists(current) && !Directory.Exists(current))
                return;
        }
    }

    /// <summary>True for symbolic links (including dangling ones), junctions and other reparse points.</summary>
    public static bool IsLink(string path)
    {
        try
        {
            if (new FileInfo(path).LinkTarget is not null)
                return true;
            if (File.Exists(path) || Directory.Exists(path))
                return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Resolves and link-checks in one step.</summary>
    public static string ResolveSafe(string root, string relativePath)
    {
        var full = Resolve(root, relativePath);
        EnsureNoLinks(root, full);
        return full;
    }
}
