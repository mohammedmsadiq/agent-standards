using System.Text.RegularExpressions;

namespace AgentStandards.Core.Rendering;

public static partial class Placeholders
{
    [GeneratedRegex(@"\{\{\s*([a-z_]+)\s*\}\}")]
    private static partial Regex Pattern();

    public static string Expand(string template, IReadOnlyDictionary<string, string> values) =>
        Pattern().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    public static IEnumerable<string> FindUnresolved(string content) =>
        Pattern().Matches(content).Select(m => m.Value).Distinct();
}
