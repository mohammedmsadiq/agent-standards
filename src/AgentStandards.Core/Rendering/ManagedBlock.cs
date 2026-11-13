namespace AgentStandards.Core.Rendering;

/// <summary>Reads and writes the toolkit-managed block inside a user-owned Markdown file.</summary>
public static class ManagedBlock
{
    public const string BeginPrefix = "<!-- agent-standards:begin";
    public const string BeginLine =
        "<!-- agent-standards:begin — managed by agent-standards. Edits inside this block are detected and preserved on update; add project-specific content outside it. -->";
    public const string EndLine = "<!-- agent-standards:end -->";

    public enum BlockState
    {
        Absent,
        Present,
        Malformed
    }

    public static (BlockState State, string? Inner) Extract(string fileContent)
    {
        var lines = Normalize(fileContent).Split('\n');
        var begins = lines.Select((l, i) => (l, i)).Where(x => x.l.TrimStart().StartsWith(BeginPrefix, StringComparison.Ordinal)).Select(x => x.i).ToList();
        var ends = lines.Select((l, i) => (l, i)).Where(x => x.l.Trim() == EndLine).Select(x => x.i).ToList();
        if (begins.Count == 0 && ends.Count == 0)
            return (BlockState.Absent, null);
        if (begins.Count != 1 || ends.Count != 1 || ends[0] < begins[0])
            return (BlockState.Malformed, null);
        return (BlockState.Present, string.Join('\n', lines[(begins[0] + 1)..ends[0]]).Trim('\n'));
    }

    public static string Wrap(string inner) => $"{BeginLine}\n{inner.Trim('\n')}\n{EndLine}\n";

    public static string CreateFile(string inner, string? seed) =>
        string.IsNullOrWhiteSpace(seed) ? Wrap(inner) : Wrap(inner) + "\n" + seed.Trim('\n') + "\n";

    /// <summary>Replaces the block's body, leaving all other content untouched.</summary>
    public static string Replace(string fileContent, string inner)
    {
        var crlf = fileContent.Contains("\r\n", StringComparison.Ordinal);
        var lines = Normalize(fileContent).Split('\n').ToList();
        var begin = lines.FindIndex(l => l.TrimStart().StartsWith(BeginPrefix, StringComparison.Ordinal));
        var end = lines.FindIndex(l => l.Trim() == EndLine);
        if (begin < 0 || end < begin)
            throw new InvalidOperationException("Managed block markers are missing or malformed.");
        lines.RemoveRange(begin, end - begin + 1);
        lines.InsertRange(begin, Wrap(inner).TrimEnd('\n').Split('\n'));
        return Restore(string.Join('\n', lines), crlf);
    }

    /// <summary>Appends a new block after existing user content, never altering that content.</summary>
    public static string Append(string fileContent, string inner)
    {
        var crlf = fileContent.Contains("\r\n", StringComparison.Ordinal);
        var normalized = Normalize(fileContent);
        var separator = normalized.Length == 0 ? "" : normalized.EndsWith("\n\n", StringComparison.Ordinal) ? "" : normalized.EndsWith('\n') ? "\n" : "\n\n";
        return Restore(normalized + separator + Wrap(inner), crlf);
    }

    public static string Remove(string fileContent)
    {
        var crlf = fileContent.Contains("\r\n", StringComparison.Ordinal);
        var lines = Normalize(fileContent).Split('\n').ToList();
        var begin = lines.FindIndex(l => l.TrimStart().StartsWith(BeginPrefix, StringComparison.Ordinal));
        var end = lines.FindIndex(l => l.Trim() == EndLine);
        if (begin < 0 || end < begin)
            return fileContent;
        lines.RemoveRange(begin, end - begin + 1);
        if (begin < lines.Count && lines[begin].Length == 0)
            lines.RemoveAt(begin);
        return Restore(string.Join('\n', lines), crlf);
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);
    private static string Restore(string s, bool crlf) => crlf ? s.Replace("\n", "\r\n", StringComparison.Ordinal) : s;
}
