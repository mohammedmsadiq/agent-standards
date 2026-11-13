using AgentStandards.Core.Diagnostics;
using AgentStandards.Core.Installation;
using AgentStandards.Core.Rendering;

namespace AgentStandards.Tests;

public class InstallTests
{
    [Fact]
    public async Task Generic_profile_installs_shared_claude_and_copilot_files()
    {
        using var h = new Harness();
        h.Repo.Write("package.json", "{\"name\":\"web\"}");

        var result = await h.Run("init");

        Assert.Equal(0, result.Code);
        Assert.Contains("Profiles:   generic", result.Out);
        foreach (var path in new[]
                 {
                     "AGENTS.md", "CLAUDE.md", ".github/copilot-instructions.md", ".agent-standards/manifest.json",
                     ".agent-standards/standards/security.md", ".agent-standards/standards/ai-agent-autonomy.md",
                     ".claude/skills/review-code/SKILL.md", ".github/skills/review-code/SKILL.md",
                     ".claude/agents/code-reviewer.md", ".github/agents/code-reviewer.agent.md",
                     ".claude/rules/tests.md", ".github/instructions/tests.instructions.md"
                 })
            Assert.True(h.Repo.Exists(path), $"{path} should exist");

        // No .NET guidance is forced onto a non-.NET repository.
        Assert.False(h.Repo.Exists(".agent-standards/standards/dotnet.md"));
        Assert.False(h.Repo.Exists(".claude/rules/csharp.md"));
        Assert.DoesNotContain("dotnet", h.Repo.Read("AGENTS.md"), StringComparison.OrdinalIgnoreCase);

        var validate = await h.Run("validate");
        Assert.True(validate.Code == 0, validate.ToString());
    }

    [Theory]
    [InlineData("generic", new[] { "security" }, new string[0])]
    [InlineData("dotnet", new[] { "dotnet", "security" }, new[] { "csharp", "msbuild" })]
    [InlineData("maui", new[] { "maui", "dotnet" }, new[] { "maui-xaml", "maui-platforms", "csharp" })]
    [InlineData("aspnetcore", new[] { "aspnetcore", "dotnet" }, new[] { "aspnetcore-hosting" })]
    [InlineData("webapi", new[] { "webapi", "aspnetcore", "dotnet" }, new[] { "api-endpoints", "aspnetcore-hosting" })]
    [InlineData("blazor", new[] { "blazor", "dotnet" }, new[] { "blazor-components" })]
    [InlineData("classlib", new[] { "dotnet-libraries", "dotnet" }, new[] { "library-public-api" })]
    public async Task Each_profile_installs_inherited_content_and_validates(string profile, string[] standards, string[] rules)
    {
        using var h = new Harness();

        var result = await h.Run("init", "--profile", profile);

        Assert.True(result.Code == 0, result.ToString());
        foreach (var s in standards)
            Assert.True(h.Repo.Exists($".agent-standards/standards/{s}.md"), $"standard {s}");
        foreach (var r in rules)
        {
            Assert.True(h.Repo.Exists($".claude/rules/{r}.md"), $"claude rule {r}");
            Assert.True(h.Repo.Exists($".github/instructions/{r}.instructions.md"), $"copilot rule {r}");
        }
        var validate = await h.Run("validate");
        Assert.True(validate.Code == 0, validate.ToString());
    }

    [Fact]
    public async Task Framework_specific_skills_and_agents_come_from_profiles()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "maui,webapi");

        Assert.True(h.Repo.Exists(".claude/agents/maui-architect.md"));
        Assert.True(h.Repo.Exists(".claude/agents/api-designer.md"));
        Assert.True(h.Repo.Exists(".github/skills/review-maui-page/SKILL.md"));
        Assert.True(h.Repo.Exists(".github/skills/design-api-endpoint/SKILL.md"));
        Assert.False(h.Repo.Exists(".claude/skills/review-blazor-component/SKILL.md"));
    }

    [Fact]
    public async Task Assistants_and_exclusions_limit_generated_files()
    {
        using var h = new Harness();
        var result = await h.Run("init", "--profile", "generic", "--assistants", "claude", "--exclude", "skill:review-performance,agent:migration-specialist");

        Assert.Equal(0, result.Code);
        Assert.True(h.Repo.Exists("CLAUDE.md"));
        Assert.True(h.Repo.Exists("AGENTS.md"));
        Assert.False(Directory.Exists(h.Repo.Full(".github")));
        Assert.False(h.Repo.Exists(".claude/skills/review-performance/SKILL.md"));
        Assert.False(h.Repo.Exists(".claude/agents/migration-specialist.md"));

        var manifest = ManifestStore.Load(h.Repo.Path)!;
        Assert.Equal(["claude"], manifest.Assistants);
        Assert.Contains("skill:review-performance", manifest.Exclude);

        // Options persist: update without options keeps them.
        await h.Run("update");
        Assert.False(Directory.Exists(h.Repo.Full(".github")));
    }

    [Fact]
    public async Task Existing_configuration_is_preserved_and_unmanaged_files_are_not_overwritten()
    {
        using var h = new Harness();
        const string claude = "# Team notes\r\n\r\nUse tabs.\r\n";
        const string agents = "# Existing agents guide\n\nDo X.\n";
        const string copilot = "Existing Copilot rules.";
        const string reviewer = "---\nname: code-reviewer\ndescription: our own\n---\nCustom reviewer.\n";
        h.Repo.Write("CLAUDE.md", claude);
        h.Repo.Write("AGENTS.md", agents);
        h.Repo.Write(".github/copilot-instructions.md", copilot);
        h.Repo.Write(".claude/agents/code-reviewer.md", reviewer);
        h.Repo.Write("README.md", "readme");

        var result = await h.Run("init", "--profile", "generic");

        Assert.Equal(2, result.Code); // conflicts reported
        Assert.StartsWith(claude, h.Repo.Read("CLAUDE.md"));
        Assert.StartsWith(agents, h.Repo.Read("AGENTS.md"));
        Assert.StartsWith(copilot, h.Repo.Read(".github/copilot-instructions.md"));
        Assert.Contains("@AGENTS.md", h.Repo.Read("CLAUDE.md"));
        Assert.Contains("\r\n", h.Repo.Read("CLAUDE.md")); // line endings of the user's file kept
        Assert.Equal(reviewer, h.Repo.Read(".claude/agents/code-reviewer.md"));
        Assert.Equal("readme", h.Repo.Read("README.md"));
        Assert.True(h.Repo.Exists(".agent-standards/conflicts/.claude/agents/code-reviewer.md.incoming"));
        Assert.DoesNotContain(ManifestStore.Load(h.Repo.Path)!.Files, f => f.Path == ".claude/agents/code-reviewer.md");

        // --force replaces it, keeping a backup.
        var forced = await h.Run("update", "--force");
        Assert.Equal(0, forced.Code);
        Assert.Contains("tools: Read, Grep, Glob", h.Repo.Read(".claude/agents/code-reviewer.md"));
        var backup = BackupStore.List(h.Repo.Path)[0];
        Assert.Equal(reviewer, BackupStore.ReadFile(h.Repo.Path, backup.Id, ".claude/agents/code-reviewer.md"));
    }

    [Fact]
    public async Task Deleted_managed_files_stay_deleted_unless_forced()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        File.Delete(h.Repo.Full(".claude/skills/write-tests/SKILL.md"));

        var update = await h.Run("update");
        Assert.Equal(0, update.Code);
        Assert.False(h.Repo.Exists(".claude/skills/write-tests/SKILL.md"));
        Assert.Contains(StatusService.Get(h.Repo.Path, "x").Files, f => f.Path == ".claude/skills/write-tests/SKILL.md" && f.State == FileState.Missing);

        await h.Run("update", "--force");
        Assert.True(h.Repo.Exists(".claude/skills/write-tests/SKILL.md"));
    }

    [Fact]
    public async Task Second_init_is_idempotent()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "dotnet");
        var before = Fixtures.Snapshot(h.Repo.Path);

        var again = await h.Run("update");

        Assert.Equal(0, again.Code);
        Assert.Contains("Nothing to do", again.Out);
        Assert.Equal(before, Fixtures.Snapshot(h.Repo.Path));
    }

    [Fact]
    public async Task Global_install_targets_assistant_home_folders_and_preserves_existing_files()
    {
        using var h = new Harness();
        h.Home.Write(".claude/CLAUDE.md", "# My personal prefs\n");

        var result = await h.Run("init", "--global");

        Assert.True(result.Code == 0, result.ToString());
        Assert.StartsWith("# My personal prefs\n", h.Home.Read(".claude/CLAUDE.md"));
        Assert.Contains("Personal engineering standards", h.Home.Read(".claude/CLAUDE.md"));
        Assert.Contains("~/.agent-standards/standards/security.md", h.Home.Read(".claude/CLAUDE.md"));
        Assert.True(h.Home.Exists(".copilot/copilot-instructions.md"));
        Assert.True(h.Home.Exists(".claude/skills/review-code/SKILL.md"));
        Assert.True(h.Home.Exists(".copilot/agents/code-reviewer.agent.md"));
        Assert.True(h.Home.Exists(".copilot/instructions/tests.instructions.md"));
        Assert.True(h.Home.Exists(".agent-standards/standards/new-projects.md"));
        Assert.True(h.Home.Exists(".agent-standards/manifest.json"));
        Assert.False(h.Home.Exists("AGENTS.md"));
        Assert.False(h.Repo.Exists("AGENTS.md"));
        Assert.Equal(InstallScope.Global, ManifestStore.Load(h.Home.Path)!.Scope);

        var validate = await h.Run("validate", "--global");
        Assert.True(validate.Code == 0, validate.ToString());
        var status = await h.Run("status", "--global");
        Assert.Contains("global scope", status.Out);
    }

    [Fact]
    public async Task Global_and_project_scopes_are_independent()
    {
        using var h = new Harness();
        await h.Run("init", "--global");
        await h.Run("init", "--profile", "generic", "--exclude", "standard:accessibility");

        Assert.True(h.Home.Exists(".agent-standards/standards/accessibility.md"));
        Assert.False(h.Repo.Exists(".agent-standards/standards/accessibility.md"));
        Assert.Equal(InstallScope.Project, ManifestStore.Load(h.Repo.Path)!.Scope);
    }

    [Fact]
    public async Task Mcp_is_rejected_for_global_scope()
    {
        using var h = new Harness();
        var result = await h.Run("init", "--global", "--mcp");
        Assert.Equal(1, result.Code);
        Assert.Contains("project-scoped", result.Err);
    }
}
