using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentStandards.Core.Templates;

namespace AgentStandards.Core.Rendering;

/// <summary>Builds the shared (assistant-neutral) outputs and the placeholder values used by every adapter.</summary>
public static class ContentBuilder
{
    public const string AgentsFile = "AGENTS.md";
    public const string McpFile = ".mcp.json";
    public const string StateGitIgnore = ".agent-standards/.gitignore";

    public static IEnumerable<RenderedFile> RenderShared(RenderContext ctx)
    {
        foreach (var standard in ctx.Profiles.Standards)
            yield return new RenderedFile($"{RenderContext.StandardsFolder}/{standard}.md", ManagedMode.Whole,
                ctx.Templates.GetFile($"standards/{standard}.md"), $"standard:{standard}");

        if (ctx.Scope == InstallScope.Project)
        {
            yield return new RenderedFile(AgentsFile, ManagedMode.Block,
                Expand(ctx, "core/agents.md", AgentsFile), "core:agents",
                Seed: ctx.Templates.Files.GetValueOrDefault("core/agents-seed.md"));

            yield return new RenderedFile(StateGitIgnore, ManagedMode.Whole, "# Local backups, conflict copies, the in-progress journal and the lock file are machine-specific.\nbackups/\nconflicts/\noperation.json\nlock\n", "core:gitignore");
        }
    }

    /// <summary>Claude Code project MCP file (<c>.mcp.json</c>, <c>mcpServers</c> schema). Copilot uses different files.</summary>
    public static RenderedFile? RenderClaudeMcp(RenderContext ctx)
    {
        if (ctx.Scope != InstallScope.Project || !ctx.IncludeMcp || ctx.Profiles.McpServers.Count == 0)
            return null;
        var servers = new JsonObject();
        foreach (var (name, server) in ctx.Profiles.McpServers.OrderBy(s => s.Key, StringComparer.Ordinal))
            servers[name] = JsonNode.Parse(server.GetRawText());
        return new RenderedFile(McpFile, ManagedMode.JsonMerge, servers.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), "mcp");
    }

    public static string Expand(RenderContext ctx, string templatePath, string outputFile, IReadOnlyDictionary<string, string>? extra = null)
    {
        var values = BuildValues(ctx, outputFile);
        if (extra is not null)
            foreach (var (k, v) in extra)
                values[k] = v;
        return Placeholders.Expand(ctx.Templates.GetFile(templatePath), values).Trim('\n');
    }

    public static Dictionary<string, string> BuildValues(RenderContext ctx, string outputFile)
    {
        var standards = new StringBuilder();
        foreach (var name in ctx.Profiles.Standards)
            standards.Append("- [").Append(TitleOf(ctx.Templates.GetFile($"standards/{name}.md"), name)).Append("](")
                .Append(ctx.LinkToStandard(outputFile, name)).Append(")\n");

        var skills = new StringBuilder();
        foreach (var name in ctx.Profiles.Skills)
            skills.Append("- `").Append(name).Append("` — ").Append(OneLine(ctx.Templates.Skills[name].Description)).Append('\n');

        var agents = new StringBuilder();
        foreach (var id in ctx.Profiles.Agents)
        {
            var agent = ctx.Templates.Agents[id];
            agents.Append("- `").Append(id).Append("` (").Append(agent.Access == AgentAccess.ReadOnly ? "read-only" : "read-write")
                .Append(") — ").Append(OneLine(agent.Description)).Append('\n');
        }

        var rules = new StringBuilder();
        foreach (var id in ctx.Profiles.Rules)
        {
            var rule = ctx.Templates.Rules[id];
            rules.Append("- `").Append(string.Join("`, `", rule.Globs)).Append("` — ").Append(OneLine(rule.Description)).Append('\n');
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["policy"] = ctx.Templates.GetFile("core/policy.md").Trim('\n'),
            ["profiles"] = string.Join(", ", ctx.Profiles.Ordered.Select(p => p.Requested(ctx) ? $"`{p.Id}`" : $"`{p.Id}` (inherited)")),
            ["standards_index"] = Fallback(standards),
            ["skills_index"] = Fallback(skills),
            ["agents_index"] = Fallback(agents),
            ["rules_index"] = Fallback(rules),
            ["templates_version"] = ctx.Templates.Version,
            ["assistants"] = string.Join(", ", ctx.Assistants),
            ["skill_locations"] = Locations(ctx, ".claude/skills/", ".github/skills/"),
            ["agent_locations"] = Locations(ctx, ".claude/agents/", ".github/agents/"),
            ["rule_locations"] = Locations(ctx, ".claude/rules/", ".github/instructions/")
        };
    }

    private static string Locations(RenderContext ctx, string claude, string copilot)
    {
        var parts = new List<string>();
        if (ctx.Assistants.Contains("claude"))
            parts.Add($"`{claude}` (Claude Code)");
        if (ctx.Assistants.Contains("copilot"))
            parts.Add($"`{copilot}` (GitHub Copilot)");
        return parts.Count == 0 ? "the assistant's configuration folder" : string.Join(", ", parts);
    }

    private static bool Requested(this ProfileDefinition profile, RenderContext ctx) =>
        ctx.Profiles.Requested.Contains(profile.Id, StringComparer.OrdinalIgnoreCase);

    public static string TitleOf(string markdown, string fallback)
    {
        foreach (var line in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            if (line.StartsWith("# ", StringComparison.Ordinal))
                return line[2..].Trim();
        return fallback;
    }

    private static string OneLine(string text) => string.Join(' ', text.Split((char[])['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Fallback(StringBuilder sb) => sb.Length == 0 ? "_None installed._" : sb.ToString().TrimEnd('\n');
}
