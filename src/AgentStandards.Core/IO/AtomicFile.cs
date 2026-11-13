using System.Text;

namespace AgentStandards.Core.IO;

/// <summary>Link-safe, atomic file operations confined to a root directory.</summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteText(string root, string relativePath, string content)
    {
        var full = SafePaths.ResolveSafe(root, relativePath);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        // Re-check after directory creation in case a link was raced in.
        SafePaths.EnsureNoLinks(root, full);
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, content, Utf8NoBom);
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public static string? ReadTextOrNull(string root, string relativePath)
    {
        var full = SafePaths.ResolveSafe(root, relativePath);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    public static void Delete(string root, string relativePath)
    {
        var full = SafePaths.ResolveSafe(root, relativePath);
        if (File.Exists(full))
            File.Delete(full);
    }
}
