using AgentStandards.Core.Adapters;
using AgentStandards.Core.Diagnostics;
using AgentStandards.Core.IO;
using AgentStandards.Core.Profiles;
using AgentStandards.Core.Rendering;
using AgentStandards.Core.Templates;

namespace AgentStandards.Tests;

public class UnitTests
{
    [Theory]
    [InlineData("**/*.cs", "a/b/C.cs", true)]
    [InlineData("**/*.cs", "C.cs", true)]
    [InlineData("**/*.cs", "a/C.csproj", false)]
    [InlineData("src/**/*", "src/x/y.txt", true)]
    [InlineData("*.md", "docs/a.md", false)]
    [InlineData("**/Platforms/**", "App/Platforms/Android/Main.cs", true)]
    [InlineData("**/*.{razor,cshtml}", "Pages/Index.cshtml", true)]
    [InlineData("**/test_*.py", "pkg/test_util.py", true)]
    public void Glob_matching(string pattern, string path, bool expected) => Assert.Equal(expected, Glob.IsMatch(pattern, path));

    [Fact]
    public void Frontmatter_parses_scalars_and_lists()
    {
        var fm = Frontmatter.Parse("---\nname: x\ndescription: \"quoted: value\"\nglobs: [\"a\", 'b']\npaths:\n  - one\n  - \"two\"\n---\nBody\n");
        Assert.True(fm.IsPresent);
        Assert.Null(fm.Error);
        Assert.Equal("x", fm.GetString("name"));
        Assert.Equal("quoted: value", fm.GetString("description"));
        Assert.Equal(["a", "b"], fm.GetList("globs"));
        Assert.Equal(["one", "two"], fm.GetList("paths"));
        Assert.Equal("Body\n", fm.Body);
    }

    [Fact]
    public void Frontmatter_reports_unclosed_header()
    {
        var fm = Frontmatter.Parse("---\nname: x\n");
        Assert.False(fm.IsPresent);
        Assert.NotNull(fm.Error);
    }

    [Fact]
    public void Managed_block_round_trip_preserves_surrounding_content()
    {
        var file = ManagedBlock.Append("# Mine\nkeep\n", "v1");
        Assert.Equal((ManagedBlock.BlockState.Present, "v1"), ManagedBlock.Extract(file));
        var replaced = ManagedBlock.Replace(file + "after\n", "v2\nline");
        Assert.StartsWith("# Mine\nkeep\n", replaced);
        Assert.EndsWith("after\n", replaced);
        Assert.Equal("v2\nline", ManagedBlock.Extract(replaced).Inner);
        Assert.Equal("# Mine\nkeep\n\nafter\n", ManagedBlock.Remove(replaced));
        Assert.Equal(ManagedBlock.BlockState.Malformed, ManagedBlock.Extract(ManagedBlock.BeginLine + "\nno end").State);
        Assert.Equal(ManagedBlock.BlockState.Absent, ManagedBlock.Extract("plain").State);
    }

    [Fact]
    public void Text_diff_produces_unified_hunks()
    {
        var diff = TextDiff.Unified("a\nb\nc\n", "a\nB\nc\n", "f.md");
        Assert.Contains("--- a/f.md", diff);
        Assert.Contains("-b", diff);
        Assert.Contains("+B", diff);
        Assert.Equal("", TextDiff.Unified("same", "same", "f"));
        Assert.Contains("/dev/null", TextDiff.Unified(null, "new", "f"));
    }

    [Theory]
    [InlineData("Review-Code", false)]
    [InlineData("review--code", false)]
    [InlineData("review-code", true)]
    public void Skill_names_follow_agent_skills_rules(string name, bool valid)
    {
        var issues = FileValidators.ValidateSkill($"skills/{name}/SKILL.md", $"---\nname: {name}\ndescription: d\n---\nbody", name).ToList();
        Assert.Equal(valid, issues.Count == 0);
    }

    [Fact]
    public void Skill_name_must_match_directory_and_description_is_required()
    {
        var issues = FileValidators.ValidateSkill("x/SKILL.md", "---\nname: one\n---\n", "two").ToList();
        Assert.Contains(issues, i => i.Message.Contains("must match its directory"));
        Assert.Contains(issues, i => i.Message.Contains("description"));
    }

    [Fact]
    public void Import_and_link_checks_ignore_code_and_urls()
    {
        using var dir = new TempDir();
        dir.Write("AGENTS.md", "x");
        var content = "@AGENTS.md\n@missing.md\n`@inline.md`\n```\n@fenced.md\n```\n[ok](AGENTS.md) [web](https://x.y) [bad](nope.md) [a](#anchor) contact me@example.com";
        var imports = FileValidators.CheckImports(dir.Path, dir.Path, "CLAUDE.md", content, Severity.Error).ToList();
        var links = FileValidators.CheckLinks(dir.Path, dir.Path, "CLAUDE.md", content, Severity.Error).ToList();
        Assert.Single(imports);
        Assert.Contains("missing.md", imports[0].Message);
        Assert.Single(links);
        Assert.Contains("nope.md", links[0].Message);
    }

    [Fact]
    public void Bundled_templates_are_valid()
    {
        using var h = new Harness();
        var set = h.Engine.Resolver.LoadBundled();
        Assert.Empty(TemplateValidator.Validate(set));
        Assert.Equal(["aspnetcore", "blazor", "classlib", "dotnet", "generic", "maui", "webapi"], set.Profiles.Keys.Order());
        foreach (var skill in new[] { "review-code", "investigate-bug", "write-tests", "refactor-code", "review-security", "review-performance",
                     "document-architecture", "review-dependencies", "create-feature", "review-pull-request" })
            Assert.Contains(skill, set.Skills.Keys);
        foreach (var agent in new[] { "software-architect", "code-reviewer", "security-reviewer", "performance-engineer", "test-engineer",
                     "documentation-engineer", "migration-specialist" })
            Assert.Contains(agent, set.Agents.Keys);
        Assert.All(set.Agents.Values.Where(a => a.Id.Contains("review") || a.Id.Contains("architect")), a => Assert.Equal(AgentAccess.ReadOnly, a.Access));
    }

    [Fact]
    public void Broken_templates_are_reported()
    {
        using var templates = new TempDir("tpl");
        Fixtures.CopyTemplates(templates.Path);
        File.WriteAllText(templates.Full("skills/review-code/SKILL.md"), "---\nname: wrong-name\ndescription: d\n---\nx");
        File.WriteAllText(templates.Full("profiles/dotnet.json"), File.ReadAllText(templates.Full("profiles/dotnet.json")).Replace("\"generic\"", "\"missing\""));
        File.WriteAllText(templates.Full("core/agents.md"), "{{unknown_placeholder}}");

        var set = TemplateSet.Create(TemplateLoader.ReadDirectory(templates.Path), TemplateSet.DefaultPackageId, null, "t");
        var issues = TemplateValidator.Validate(set);

        Assert.Contains(issues, i => i.Message.Contains("must match its directory"));
        Assert.Contains(issues, i => i.Message.Contains("extends unknown profile 'missing'"));
        Assert.Contains(issues, i => i.Message.Contains("{{unknown_placeholder}}"));
    }

    [Fact]
    public void Profile_cycles_are_detected()
    {
        using var templates = new TempDir("tpl");
        Fixtures.CopyTemplates(templates.Path);
        File.WriteAllText(templates.Full("profiles/generic.json"), File.ReadAllText(templates.Full("profiles/generic.json")).Replace("\"extends\": []", "\"extends\": [\"maui\"]"));
        var set = TemplateSet.Create(TemplateLoader.ReadDirectory(templates.Path), TemplateSet.DefaultPackageId, null, "t");

        var ex = Assert.Throws<TemplateException>(() => ProfileResolver.Resolve(set, ["maui"]));
        Assert.Contains("cycle", ex.Message);
    }

    [Fact]
    public void Adapters_produce_assistant_specific_formats()
    {
        using var h = new Harness();
        var set = h.Engine.Resolver.LoadBundled();
        var ctx = new RenderContext { Templates = set, Profiles = ProfileResolver.Resolve(set, ["maui"]), Scope = InstallScope.Project, Assistants = ["claude", "copilot"] };

        var claude = new ClaudeCodeAdapter().Render(ctx).ToDictionary(f => f.Path);
        var copilot = new CopilotAdapter().Render(ctx).ToDictionary(f => f.Path);

        Assert.StartsWith("@AGENTS.md", claude["CLAUDE.md"].Content);
        Assert.Contains("paths:\n  - \"**/*.xaml\"", claude[".claude/rules/maui-xaml.md"].Content);
        Assert.Contains("tools: Read, Grep, Glob", claude[".claude/agents/maui-architect.md"].Content);
        Assert.DoesNotContain("tools:", claude[".claude/agents/test-engineer.md"].Content);
        Assert.Contains("applyTo: \"**/*.xaml\"", copilot[".github/instructions/maui-xaml.instructions.md"].Content);
        Assert.Contains("tools: ['read', 'search']", copilot[".github/agents/code-reviewer.agent.md"].Content);
        Assert.Contains("[AGENTS.md](../AGENTS.md)", copilot[".github/copilot-instructions.md"].Content);
        Assert.Contains("](../.agent-standards/standards/maui.md)", copilot[".github/copilot-instructions.md"].Content);
        Assert.Equal(claude[".claude/skills/review-code/SKILL.md"].Content, copilot[".github/skills/review-code/SKILL.md"].Content);
    }
}
