using AgentStandards.Core.Diagnostics;
using AgentStandards.Core.Installation;
using AgentStandards.Core.IO;

namespace AgentStandards.Tests;

public class UpdateTests
{
    private static string Edit(string path, string find, string replace)
    {
        var text = File.ReadAllText(path);
        Assert.Contains(find, text);
        File.WriteAllText(path, text.Replace(find, replace));
        return path;
    }

    [Fact]
    public async Task Update_applies_upstream_changes_to_unmodified_files_and_keeps_local_edits()
    {
        using var h = new Harness();
        using var v1 = new TempDir("v1");
        using var v2 = new TempDir("v2");
        Fixtures.CopyTemplates(v1.Path, "1.0.0");
        Fixtures.CopyTemplates(v2.Path, "2.0.0");
        Edit(v2.Full("standards/testing.md"), "# Testing", "# Testing (v2)");
        Edit(v2.Full("standards/security.md"), "# Security", "# Security (v2)");
        Edit(v2.Full("core/policy.md"), "## Core engineering rules", "## Core engineering rules (v2)");

        Assert.Equal(0, (await h.Run("init", "--profile", "generic", "--templates", v1.Path)).Code);
        Edit(h.Repo.Full(".agent-standards/standards/security.md"), "## Secrets", "## Secrets (team edit)");
        h.Repo.Write("AGENTS.md", h.Repo.Read("AGENTS.md") + "\nTeam notes outside the block.\n");
        h.Repo.Write(".claude/skills/our-skill/SKILL.md", "---\nname: our-skill\ndescription: ours\n---\nbody\n");

        var result = await h.Run("update", "--templates", v2.Path);

        Assert.True(result.Code == 2, result.ToString()); // security.md conflicts
        Assert.Contains("# Testing (v2)", h.Repo.Read(".agent-standards/standards/testing.md"));
        var security = h.Repo.Read(".agent-standards/standards/security.md");
        Assert.Contains("(team edit)", security);
        Assert.DoesNotContain("(v2)", security);
        Assert.Contains("# Security (v2)", h.Repo.Read(".agent-standards/conflicts/.agent-standards/standards/security.md.incoming"));
        Assert.DoesNotContain("(v2)", h.Repo.Read(".agent-standards/conflicts/.agent-standards/standards/security.md.base")); // previously applied version
        var agents = h.Repo.Read("AGENTS.md");
        Assert.Contains("## Core engineering rules (v2)", agents);
        Assert.Contains("Team notes outside the block.", agents);
        Assert.True(h.Repo.Exists(".claude/skills/our-skill/SKILL.md"));
        Assert.Equal("2.0.0", ManifestStore.Load(h.Repo.Path)!.Templates.Version);

        // The conflict stays visible on later updates until it is resolved.
        var again = await h.Run("update", "--templates", v2.Path);
        Assert.True(again.Code == 2, again.ToString());
        Assert.Contains("(team edit)", h.Repo.Read(".agent-standards/standards/security.md"));
        Assert.Contains(StatusService.Get(h.Repo.Path, "x").Files, f => f.Path.EndsWith("security.md") && f.State == FileState.Conflict);

        var resolve = await h.Run("resolve", ".agent-standards/standards/security.md", "--accept-local", "--yes");
        Assert.True(resolve.Code == 0, resolve.ToString());
        Assert.False(h.Repo.Exists(".agent-standards/conflicts/.agent-standards/standards/security.md.incoming"));
        Assert.Equal(0, (await h.Run("update", "--templates", v2.Path)).Code);
        Assert.Contains(StatusService.Get(h.Repo.Path, "x").Files, f => f.Path.EndsWith("security.md") && f.State == FileState.Modified);
    }

    [Fact]
    public async Task Modified_managed_block_conflicts_but_user_content_outside_is_untouched()
    {
        using var h = new Harness();
        using var v1 = new TempDir("v1");
        using var v2 = new TempDir("v2");
        Fixtures.CopyTemplates(v1.Path, "1.0.0");
        Fixtures.CopyTemplates(v2.Path, "2.0.0");
        Edit(v2.Full("core/claude.md"), "## Claude Code", "## Claude Code v2");

        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        Edit(h.Repo.Full("CLAUDE.md"), "## Claude Code", "## Claude Code (edited inside block)");

        var result = await h.Run("update", "--templates", v2.Path, "--json");

        Assert.Equal(2, result.Code);
        Assert.Contains(result.Json["actions"]!.AsArray(), a => a!["path"]!.GetValue<string>() == "CLAUDE.md" && a["action"]!.GetValue<string>() == "conflict");
        Assert.Contains("(edited inside block)", h.Repo.Read("CLAUDE.md"));
        Assert.Contains("## Claude Code v2", h.Repo.Read(".agent-standards/conflicts/CLAUDE.md.incoming"));
    }

    [Fact]
    public async Task Malformed_block_markers_are_reported_not_rewritten()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        var broken = h.Repo.Read("CLAUDE.md").Replace("<!-- agent-standards:end -->", "");
        h.Repo.Write("CLAUDE.md", broken);

        var result = await h.Run("update", "--force");

        Assert.Equal(2, result.Code);
        Assert.Equal(broken, h.Repo.Read("CLAUDE.md"));
        Assert.Equal(1, (await h.Run("validate")).Code);
    }

    [Fact]
    public async Task Obsolete_files_are_kept_until_prune_and_modified_ones_are_never_deleted()
    {
        using var h = new Harness();
        using var v1 = new TempDir("v1");
        using var v2 = new TempDir("v2");
        Fixtures.CopyTemplates(v1.Path, "1.0.0");
        Fixtures.CopyTemplates(v2.Path, "2.0.0");
        var generic = v2.Full("profiles/generic.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(generic))!;
        json["skills"]!.AsArray().Remove(json["skills"]!.AsArray().First(n => n!.GetValue<string>() == "review-performance"));
        json["agents"]!.AsArray().Remove(json["agents"]!.AsArray().First(n => n!.GetValue<string>() == "migration-specialist"));
        File.WriteAllText(generic, json.ToJsonString());

        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        Edit(h.Repo.Full(".claude/agents/migration-specialist.md"), "You are", "We customised: you are");

        var keep = await h.Run("update", "--templates", v2.Path);
        Assert.Equal(0, keep.Code);
        Assert.Contains("obsolete", keep.Out);
        Assert.True(h.Repo.Exists(".claude/skills/review-performance/SKILL.md"));

        var prune = await h.Run("update", "--templates", v2.Path, "--prune");
        Assert.Equal(0, prune.Code);
        Assert.False(h.Repo.Exists(".claude/skills/review-performance/SKILL.md"));
        Assert.False(h.Repo.Exists(".github/agents/migration-specialist.agent.md"));
        Assert.True(h.Repo.Exists(".claude/agents/migration-specialist.md")); // modified: kept, untracked
        Assert.DoesNotContain(ManifestStore.Load(h.Repo.Path)!.Files, f => f.Path.Contains("migration-specialist"));
    }

    [Fact]
    public async Task Rollback_restores_previous_version_and_removes_created_files()
    {
        using var h = new Harness();
        using var v1 = new TempDir("v1");
        using var v2 = new TempDir("v2");
        Fixtures.CopyTemplates(v1.Path, "1.0.0");
        Fixtures.CopyTemplates(v2.Path, "2.0.0");
        Edit(v2.Full("standards/testing.md"), "# Testing", "# Testing (v2)");
        var generic = v2.Full("profiles/generic.json");
        File.WriteAllText(generic, File.ReadAllText(generic).Replace("\"rules\": [\"tests\"]", "\"rules\": [\"tests\", \"csharp\"]"));

        h.Repo.Write("CLAUDE.md", "# mine\n");
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        var afterInit = Fixtures.Snapshot(h.Repo.Path).Where(kv => !kv.Key.Contains("backups") && Path.GetFileName(kv.Key) != "lock").ToDictionary();
        await h.Run("update", "--templates", v2.Path);
        Assert.True(h.Repo.Exists(".claude/rules/csharp.md"));

        var dry = await h.Run("rollback", "--dry-run");
        Assert.Equal(0, dry.Code);
        Assert.True(h.Repo.Exists(".claude/rules/csharp.md"));

        var refused = await h.Run("rollback");
        Assert.Equal(1, refused.Code);
        Assert.Contains("--yes", refused.Err);

        var rollback = await h.Run("rollback", "--yes");
        Assert.True(rollback.Code == 0, rollback.ToString());
        Assert.False(h.Repo.Exists(".claude/rules/csharp.md"));
        Assert.DoesNotContain("(v2)", h.Repo.Read(".agent-standards/standards/testing.md"));
        Assert.Equal("1.0.0", ManifestStore.Load(h.Repo.Path)!.Templates.Version);
        var afterRollback = Fixtures.Snapshot(h.Repo.Path).Where(kv => !kv.Key.Contains("backups") && Path.GetFileName(kv.Key) != "lock").ToDictionary();
        Assert.Equal(afterInit.Keys.Order(), afterRollback.Keys.Order());
        foreach (var (k, v) in afterInit.Where(kv => kv.Value != "dir"))
            Assert.Equal(v.Split(':')[2], afterRollback[k].Split(':')[2]);

        // Rolling back the init removes everything it created and restores the user's CLAUDE.md.
        Assert.Equal(0, (await h.Run("rollback", "--yes")).Code);
        Assert.Equal("# mine\n", h.Repo.Read("CLAUDE.md"));
        Assert.False(h.Repo.Exists("AGENTS.md"));
        Assert.False(h.Repo.Exists(".agent-standards/manifest.json"));
    }

    [Fact]
    public async Task Rollback_skips_files_changed_after_the_operation()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        h.Repo.Write("AGENTS.md", h.Repo.Read("AGENTS.md").Replace("# AI engineering standards", "# AI engineering standards\nlater edit"));

        var result = await h.Run("rollback", "--yes");

        Assert.Equal(2, result.Code); // partial
        Assert.Contains("skip-modified", result.Out);
        Assert.Contains("later edit", h.Repo.Read("AGENTS.md"));
        // The kept file stays tracked, so its local edit is still recognised.
        var manifest = ManifestStore.Load(h.Repo.Path)!;
        Assert.Equal(["AGENTS.md"], manifest.Files.Select(f => f.Path));
        Assert.Equal(FileState.Modified, StatusService.GetState(h.Repo.Path, manifest.Files[0]));
    }

    [Fact]
    public async Task Line_ending_changes_do_not_count_as_local_modifications()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        var path = h.Repo.Full(".agent-standards/standards/testing.md");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\n", "\r\n"));

        var status = StatusService.Get(h.Repo.Path, "x");

        Assert.All(status.Files, f => Assert.Equal(FileState.Unmodified, f.State));
        Assert.Equal(ContentHash.Compute("a\nb"), ContentHash.Compute("a\r\nb"));
    }
}
