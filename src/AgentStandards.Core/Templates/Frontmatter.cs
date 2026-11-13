namespace AgentStandards.Core.Templates;

/// <summary>
/// Parses the small YAML subset used in Markdown frontmatter by SKILL.md, agent and instruction files:
/// scalar <c>key: value</c>, inline lists <c>[a, b]</c> and block lists (<c>- item</c>).
/// </summary>
public sealed class Frontmatter
{
    private readonly Dictionary<string, object> _values;

    private Frontmatter(bool present, Dictionary<string, object> values, string body, string? error)
    {
        IsPresent = present;
        _values = values;
        Body = body;
        Error = error;
    }

    public bool IsPresent { get; }
    public string Body { get; }
    public string? Error { get; }
    public IReadOnlyCollection<string> Keys => _values.Keys;

    public string? GetString(string key) => _values.TryGetValue(key, out var v) ? v as string : null;

    public IReadOnlyList<string> GetList(string key) => _values.TryGetValue(key, out var v)
        ? v switch
        {
            List<string> list => list,
            string s when s.Length > 0 => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Unquote).ToList(),
            _ => []
        }
        : [];

    public bool Has(string key) => _values.ContainsKey(key);

    public static Frontmatter Parse(string content)
    {
        var text = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.StartsWith('﻿'))
            text = text[1..];
        if (!text.StartsWith("---\n", StringComparison.Ordinal))
            return new Frontmatter(false, [], text, null);

        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
            return new Frontmatter(false, [], text, "frontmatter is not closed with '---'");

        var afterMarker = end + 4;
        if (afterMarker < text.Length && text[afterMarker] != '\n')
            return new Frontmatter(false, [], text, "frontmatter closing '---' must be on its own line");

        var yaml = text[4..end];
        var body = afterMarker >= text.Length ? "" : text[(afterMarker + 1)..];
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        string? currentListKey = null;
        string? error = null;

        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
                continue;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) && currentListKey is not null && line.Length > trimmed.Length)
            {
                ((List<string>)values[currentListKey]).Add(Unquote(trimmed[2..].Trim()));
                continue;
            }

            if (line.Length > trimmed.Length)
            {
                // Indented content we do not interpret (nested maps); keep parsing.
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                error ??= $"cannot parse frontmatter line '{line}'";
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            currentListKey = null;
            if (values.ContainsKey(key))
                error ??= $"duplicate frontmatter key '{key}'";

            if (value.Length == 0)
            {
                values[key] = new List<string>();
                currentListKey = key;
            }
            else if (value.StartsWith('[') && value.EndsWith(']'))
            {
                values[key] = value[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(Unquote).ToList();
            }
            else
            {
                values[key] = Unquote(value);
            }
        }

        return new Frontmatter(true, values, body, error);
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;

    /// <summary>Quotes a scalar for YAML output when needed.</summary>
    public static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
