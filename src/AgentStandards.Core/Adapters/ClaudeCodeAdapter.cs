using System.Text;
using AgentStandards.Core.Rendering;
using AgentStandards.Core.Templates;

namespace AgentStandards.Core.Adapters;

/// <summary>
/// Claude Code layout. Claude Code reads CLAUDE.md (not AGENTS.md when a CLAUDE.md exists), so the project CLAUDE.md
/// imports AGENTS.md with <c>@AGENTS.md</c>. Path-scoped rules go to <c>.claude/rules/</c> with a <c>paths</c> list,
/// skills to <c>.claude/skills/NAME/SKILL.md</c>, subagents to <c>.claude/agents/NAME.md</c>.
/// Global scope targets <c>~/.claude/</c>.
/// </summary>
public sealed class ClaudeCodeAdapter : IAssistantAdapter
{
    public string Id => "claude";
    public string DisplayName => "Claude Code";
    public string Summary => "CLAUDE.md (imports AGENTS.md), .claude/rules, .claude/skills, .claude/agents";

    /// <summary>Claude Code tool names granted to read-only agents.</summary>
    public static readonly string[] ReadOnlyTools = ["Read", "Grep", "Glob"];

    public IEnumerable<RenderedFile> Render(RenderContext ctx)
    {
        var global = ctx.Scope == InstallScope.Global;
        var instructions = global ? ".claude/CLAUDE.md" : "CLAUDE.md";
        yield return new RenderedFile(instructions, ManagedMode.Block,
            ContentBuilder.Expand(ctx, global ? "core/global.md" : "core/claude.md", instructions,
                new Dictionary<string, string> { ["assistant"] = DisplayName, ["skills_dir"] = "~/.claude/skills", ["agents_dir"] = "~/.claude/agents" }),
            "core:claude");

        foreach (var id in ctx.Profiles.Rules)
        {
            var rule = ctx.Templates.Rules[id];
            var sb = new StringBuilder("---\npaths:\n");
            foreach (var glob in rule.Globs)
                sb.Append("  - ").Append(Frontmatter.Quote(glob)).Append('\n');
            sb.Append("---\n").Append(rule.Body.TrimStart('\n'));
            yield return new RenderedFile($".claude/rules/{id}.md", ManagedMode.Whole, sb.ToString(), $"rule:{id}");
        }

        foreach (var name in ctx.Profiles.Skills)
            foreach (var (file, content) in ctx.Templates.Skills[name].Files)
                yield return new RenderedFile($".claude/skills/{name}/{file}", ManagedMode.Whole, content, $"skill:{name}");

        foreach (var id in ctx.Profiles.Agents)
        {
            var agent = ctx.Templates.Agents[id];
            var sb = new StringBuilder("---\n")
                .Append("name: ").Append(id).Append('\n')
                .Append("description: ").Append(Frontmatter.Quote(agent.Description)).Append('\n');
            if (agent.Access == AgentAccess.ReadOnly)
                sb.Append("tools: ").Append(string.Join(", ", ReadOnlyTools)).Append('\n');
            sb.Append("---\n").Append(agent.Body.TrimStart('\n'));
            yield return new RenderedFile($".claude/agents/{id}.md", ManagedMode.Whole, sb.ToString(), $"agent:{id}");
        }
    }
}
