namespace AgentStandards.Tests;

public class CliTests
{
    [Fact]
    public async Task Help_lists_all_commands()
    {
        using var h = new Harness();
        var result = await h.Run("--help");
        Assert.Equal(0, result.Code);
        foreach (var command in new[] { "init", "update", "status", "validate", "diff", "list", "doctor", "rollback" })
            Assert.Contains(command, result.Out);
    }

    [Fact]
    public async Task Init_help_documents_required_options()
    {
        using var h = new Harness();
        var result = await h.Run("init", "--help");
        foreach (var option in new[] { "--project", "--profile", "--version", "--dry-run", "--json", "--verbose", "--force", "--global", "--source", "--offline" })
            Assert.Contains(option, result.Out);
    }

    [Fact]
    public async Task List_json_reports_profiles_skills_and_agents()
    {
        using var h = new Harness();
        var result = await h.Run("list", "--json");
        Assert.Equal(0, result.Code);
        Assert.Equal(7, result.Json["profiles"]!.AsArray().Count);
        Assert.Contains(result.Json["skills"]!.AsArray(), s => s!["name"]!.GetValue<string>() == "review-pull-request");
        Assert.Contains(result.Json["agents"]!.AsArray(), a => a!["id"]!.GetValue<string>() == "security-reviewer" && a["access"]!.GetValue<string>() == "read-only");
        Assert.Equal(2, result.Json["assistants"]!.AsArray().Count);

        var profiles = await h.Run("list", "profiles");
        Assert.Contains("webapi", profiles.Out);
        Assert.DoesNotContain("Skills:", profiles.Out);
    }

    [Fact]
    public async Task Errors_return_exit_code_one_with_message()
    {
        using var h = new Harness();
        var unknown = await h.Run("init", "--profile", "cobol");
        Assert.Equal(1, unknown.Code);
        Assert.Contains("Unknown profile 'cobol'", unknown.Err);

        var notInstalled = await h.Run("update");
        Assert.Equal(1, notInstalled.Code);
        Assert.Contains("agent-standards init", notInstalled.Err);

        var badAssistant = await h.Run("init", "--assistants", "notepad");
        Assert.Equal(1, badAssistant.Code);

        var badExclude = await h.Run("init", "--exclude", "widget:x");
        Assert.Equal(1, badExclude.Code);

        var missingProject = await h.Run("init", "--project", Path.Combine(h.Repo.Path, "nope"));
        Assert.Equal(1, missingProject.Code);
    }

    [Fact]
    public async Task Init_json_output_is_machine_readable()
    {
        using var h = new Harness();
        var result = await h.Run("init", "--profile", "generic", "--json");
        Assert.Equal(0, result.Code);
        Assert.True(result.Json["applied"]!.GetValue<bool>());
        Assert.NotNull(result.Json["backupId"]);

        var status = await h.Run("status", "--json");
        Assert.Equal("project", status.Json["scope"]!.GetValue<string>());
        Assert.All(status.Json["files"]!.AsArray(), f => Assert.Equal("unmodified", f!["state"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Doctor_reports_healthy_installation()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        var result = await h.Run("doctor", "--json");
        Assert.Equal(0, result.Code);
        Assert.True(result.Json["healthy"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Doctor_warns_when_claude_md_does_not_import_agents_md()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic", "--assistants", "copilot");
        h.Repo.Write("CLAUDE.md", "# no import\n");

        var result = await h.Run("doctor");

        Assert.Contains("does not import @AGENTS.md", result.Out);
    }

    [Fact]
    public async Task Verbose_shows_unchanged_files()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        var result = await h.Run("update", "--dry-run", "--verbose");
        Assert.Contains("unchanged", result.Out);
    }
}
