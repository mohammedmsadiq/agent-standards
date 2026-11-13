using System.Text.Json;
using AgentStandards.Core.Templates;

namespace AgentStandards.Core.Profiles;

/// <summary>The flattened content of one or more profiles after applying <c>extends</c> and exclusions.</summary>
public sealed class ResolvedProfiles
{
    public required IReadOnlyList<string> Requested { get; init; }
    /// <summary>All profiles in dependency order (bases first).</summary>
    public required IReadOnlyList<ProfileDefinition> Ordered { get; init; }
    public required IReadOnlyList<string> Standards { get; init; }
    public required IReadOnlyList<string> Rules { get; init; }
    public required IReadOnlyList<string> Skills { get; init; }
    public required IReadOnlyList<string> Agents { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> McpServers { get; init; }
}

public static class ProfileResolver
{
    /// <summary>Exclusion syntax: <c>standard:NAME</c>, <c>rule:NAME</c>, <c>skill:NAME</c>, <c>agent:NAME</c>.</summary>
    public static readonly string[] ExclusionKinds = ["standard", "rule", "skill", "agent"];

    public static ResolvedProfiles Resolve(TemplateSet templates, IEnumerable<string> profileIds, IEnumerable<string>? exclusions = null)
    {
        var requested = profileIds.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requested.Count == 0)
            throw new TemplateException("At least one profile is required.");

        var ordered = new List<ProfileDefinition>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string id, string? from)
        {
            if (!templates.Profiles.TryGetValue(id, out var profile))
                throw new TemplateException(from is null
                    ? $"Unknown profile '{id}'. Available: {string.Join(", ", templates.Profiles.Keys.Order())}."
                    : $"Profile '{from}' extends unknown profile '{id}'.");
            if (visited.Contains(profile.Id))
                return;
            if (!visiting.Add(profile.Id))
                throw new TemplateException($"Profile inheritance cycle detected at '{profile.Id}'.");
            foreach (var parent in profile.Extends)
                Visit(parent, profile.Id);
            visiting.Remove(profile.Id);
            visited.Add(profile.Id);
            ordered.Add(profile);
        }

        foreach (var id in requested)
            Visit(id, null);

        var excluded = ParseExclusions(exclusions ?? []);
        List<string> Collect(Func<ProfileDefinition, List<string>> selector, string kind, ICollection<string> available)
        {
            var result = new List<string>();
            foreach (var profile in ordered)
            {
                foreach (var item in selector(profile))
                {
                    if (!available.Contains(item))
                        throw new TemplateException($"Profile '{profile.Id}' references missing {kind} '{item}'.");
                    if (!excluded.Contains($"{kind}:{item}") && !result.Contains(item, StringComparer.Ordinal))
                        result.Add(item);
                }
            }
            return result;
        }

        var mcp = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var profile in ordered)
            foreach (var (name, server) in profile.McpServers)
                mcp[name] = server;

        return new ResolvedProfiles
        {
            Requested = requested.Select(r => templates.Profiles[r].Id).ToList(),
            Ordered = ordered,
            Standards = Collect(p => p.Standards, "standard", templates.Standards.ToList()),
            Rules = Collect(p => p.Rules, "rule", templates.Rules.Keys.ToList()),
            Skills = Collect(p => p.Skills, "skill", templates.Skills.Keys.ToList()),
            Agents = Collect(p => p.Agents, "agent", templates.Agents.Keys.ToList()),
            McpServers = mcp
        };
    }

    public static HashSet<string> ParseExclusions(IEnumerable<string> exclusions)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in exclusions.SelectMany(e => e.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            var colon = raw.IndexOf(':');
            if (colon <= 0 || !ExclusionKinds.Contains(raw[..colon]))
                throw new TemplateException($"Invalid exclusion '{raw}'. Use one of: {string.Join(", ", ExclusionKinds.Select(k => k + ":NAME"))}.");
            set.Add(raw);
        }
        return set;
    }

    /// <summary>True when <paramref name="ancestor"/> is reachable through <paramref name="profile"/>'s extends chain.</summary>
    public static bool Extends(TemplateSet templates, string profile, string ancestor)
    {
        if (!templates.Profiles.TryGetValue(profile, out var definition))
            return false;
        foreach (var parent in definition.Extends)
        {
            if (string.Equals(parent, ancestor, StringComparison.OrdinalIgnoreCase) || Extends(templates, parent, ancestor))
                return true;
        }
        return false;
    }
}
