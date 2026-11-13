using System.Text;
using AgentStandards.Core.Rendering;
using AgentStandards.Core.Templates;

namespace AgentStandards.Core.Adapters;

/// <summary>
/// GitHub Copilot layout. <c>.github/copilot-instructions.md</c> carries the core policy because some Copilot surfaces
/// (for example Copilot Chat on GitHub.com) do not read AGENTS.md. Path-scoped rules go to
/// <c>.github/instructions/NAME.instructions.md</c> with <c>applyTo</c>, skills to <c>.github/skills/NAME/SKILL.md</c>,
/// custom agents to <c>.github/agents/NAME.agent.md</c>. Global scope targets <c>~/.copilot/</c>.
/// </summary>
public sealed class CopilotAdapter : IAssistantAdapter
{
    public string Id => "copilot";
    public string DisplayName => "GitHub Copilot";
    public string Summary => ".github/copilot-instructions.md, .github/instructions, .github/skills, .github/agents";

    /// <summary>Copilot tool aliases granted to read-only agents.</summary>
    public static readonly string[] ReadOnlyTools = ["read", "search"];

    public IEnumerable<RenderedFile> Render(RenderContext ctx)
    {
        var global = ctx.Scope == InstallScope.Global;
        var baseDir = global ? ".copilot" : ".github";
        var instructions = $"{baseDir}/copilot-instructions.md";
        yield return new RenderedFile(instructions, ManagedMode.Block,
            ContentBuilder.Expand(ctx, global ? "core/global.md" : "core/copilot.md", instructions,
                new Dictionary<string, string> { ["assistant"] = DisplayName, ["skills_dir"] = "~/.copilot/skills", ["agents_dir"] = "~/.copilot/agents" }),
            "core:copilot");

        foreach (var id in ctx.Profiles.Rules)
        {
            var rule = ctx.Templates.Rules[id];
            var sb = new StringBuilder("---\n")
                .Append("applyTo: ").Append(Frontmatter.Quote(string.Join(',', rule.Globs))).Append('\n')
                .Append("description: ").Append(Frontmatter.Quote(rule.Description)).Append('\n')
                .Append("---\n").Append(rule.Body.TrimStart('\n'));
            yield return new RenderedFile($"{baseDir}/instructions/{id}.instructions.md", ManagedMode.Whole, sb.ToString(), $"rule:{id}");
        }

        foreach (var name in ctx.Profiles.Skills)
            foreach (var (file, content) in ctx.Templates.Skills[name].Files)
                yield return new RenderedFile($"{baseDir}/skills/{name}/{file}", ManagedMode.Whole, content, $"skill:{name}");

        foreach (var id in ctx.Profiles.Agents)
        {
            var agent = ctx.Templates.Agents[id];
            var sb = new StringBuilder("---\n")
                .Append("name: ").Append(id).Append('\n')
                .Append("description: ").Append(Frontmatter.Quote(agent.Description)).Append('\n');
            if (agent.Access == AgentAccess.ReadOnly)
                sb.Append("tools: [").Append(string.Join(", ", ReadOnlyTools.Select(t => $"'{t}'"))).Append("]\n");
            sb.Append("---\n").Append(agent.Body.TrimStart('\n'));
            yield return new RenderedFile($"{baseDir}/agents/{id}.agent.md", ManagedMode.Whole, sb.ToString(), $"agent:{id}");
        }
    }
}
