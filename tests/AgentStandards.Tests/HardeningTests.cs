using System.IO.Compression;
using System.Text.Json.Nodes;
using AgentStandards.Core.Diagnostics;
using AgentStandards.Core.Installation;
using AgentStandards.Core.Templates;

namespace AgentStandards.Tests;

/// <summary>Regression tests for the pre-release review fixes.</summary>
[Collection(FaultInjection.Name)]
public class HardeningTests
{
    private static (TempDir v1, TempDir v2) Versions(Action<TempDir>? changeV2 = null)
    {
        var v1 = new TempDir("v1");
        var v2 = new TempDir("v2");
        Fixtures.CopyTemplates(v1.Path, "1.0.0");
        Fixtures.CopyTemplates(v2.Path, "2.0.0");
        var testing = v2.Full("standards/testing.md");
        File.WriteAllText(testing, File.ReadAllText(testing).Replace("# Testing", "# Testing (v2)"));
        var security = v2.Full("standards/security.md");
        File.WriteAllText(security, File.ReadAllText(security).Replace("# Security", "# Security (v2)"));
        changeV2?.Invoke(v2);
        return (v1, v2);
    }

    private static SortedDictionary<string, string> Content(TempDir dir) =>
        new(Fixtures.Snapshot(dir.Path).Where(kv => !kv.Key.Contains("backups") && !kv.Key.EndsWith("agent-standards/lock") && !kv.Key.EndsWith("agent-standards\\lock")).ToDictionary(kv => kv.Key, kv => kv.Value == "dir" ? "dir" : kv.Value.Split(':')[2]));

    private static IDisposable FailOn(string root, Func<string, bool> predicate)
    {
        Transaction.Fault = (r, stage, path) =>
        {
            if (r == root && stage == Transaction.Stage.Change && predicate(path!))
                throw new IOException($"simulated failure writing {path}");
        };
        return new Reset();
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => Transaction.Fault = null;
    }

    // ---- Rollback --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Partial_rollback_keeps_modified_file_tracked_against_its_current_baseline()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        await h.Run("update", "--templates", v2.Path);
        var testing = h.Repo.Full(".agent-standards/standards/testing.md");
        File.AppendAllText(testing, "\nteam addition\n");

        var result = await h.Run("rollback", "--yes");

        Assert.True(result.Code == 2, result.ToString());
        Assert.Contains("team addition", File.ReadAllText(testing));
        Assert.DoesNotContain("(v2)", h.Repo.Read(".agent-standards/standards/security.md")); // others reverted
        var manifest = ManifestStore.Load(h.Repo.Path)!;
        Assert.Equal("1.0.0", manifest.Templates.Version);
        var entry = manifest.Files.Single(f => f.Path == ".agent-standards/standards/testing.md");
        Assert.Equal(FileState.Modified, StatusService.GetState(h.Repo.Path, entry));

        // The next update against v2 sees an honest local modification and keeps it.
        var update = await h.Run("update", "--templates", v2.Path);
        Assert.True(update.Code is 0 or 2, update.ToString());
        Assert.Contains("team addition", File.ReadAllText(testing));
    }

    [Fact]
    public async Task Forced_rollback_reverts_modified_files_and_dry_run_matches_real_actions()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        await h.Run("update", "--templates", v2.Path);
        File.AppendAllText(h.Repo.Full(".agent-standards/standards/testing.md"), "\nlocal\n");

        var dry = await h.Run("rollback", "--force", "--dry-run", "--json");
        var real = await h.Run("rollback", "--force", "--yes", "--json");

        Assert.Equal(0, real.Code);
        Assert.Equal(dry.Json["actions"]!.ToJsonString(), real.Json["actions"]!.ToJsonString());
        Assert.False(real.Json["partial"]!.GetValue<bool>());
        Assert.DoesNotContain("local", h.Repo.Read(".agent-standards/standards/testing.md"));
        Assert.DoesNotContain("(v2)", h.Repo.Read(".agent-standards/standards/testing.md"));
    }

    [Fact]
    public async Task Failed_rollback_is_reverted_and_can_be_retried()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        await h.Run("update", "--templates", v2.Path);
        var before = Content(h.Repo);

        CliResult failed;
        using (FailOn(h.Repo.Path, p => p.EndsWith("security.md", StringComparison.Ordinal)))
            failed = await h.Run("rollback", "--yes");

        Assert.Equal(1, failed.Code);
        Assert.Contains("reverted", failed.Err);
        Assert.Equal(before, Content(h.Repo));
        Assert.Null(OperationJournal.Read(h.Repo.Path));

        Assert.Equal(0, (await h.Run("rollback", "--yes")).Code);
        Assert.Equal("1.0.0", ManifestStore.Load(h.Repo.Path)!.Templates.Version);
    }

    // ---- Transactions and interrupted operations -------------------------------------------------------------

    [Fact]
    public async Task Failed_update_reverts_every_change_and_reports_it()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        var before = Content(h.Repo);
        var backups = BackupStore.List(h.Repo.Path).Count;

        CliResult result;
        using (FailOn(h.Repo.Path, p => p.EndsWith("testing.md", StringComparison.Ordinal)))
            result = await h.Run("update", "--templates", v2.Path);

        Assert.Equal(1, result.Code);
        Assert.Contains("All changes were reverted", result.Err);
        Assert.Equal(before, Content(h.Repo));
        Assert.Null(OperationJournal.Read(h.Repo.Path));
        Assert.Equal(backups, BackupStore.List(h.Repo.Path).Count);
        var retry = await h.Run("update", "--templates", v2.Path);
        Assert.True(retry.Code == 0, retry.ToString());
    }

    [Fact]
    public async Task Interrupted_operation_blocks_changes_and_rollback_recovers_it()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        var beforeUpdate = Content(h.Repo);
        await h.Run("update", "--templates", v2.Path);

        // Simulate a crash part-way through the update: journal present, only some files written.
        var backup = BackupStore.List(h.Repo.Path)[0];
        h.Repo.Write(OperationJournal.RelativePath, $"{{\"backupId\":\"{backup.Id}\",\"operation\":\"update\"}}");
        h.Repo.Write(".agent-standards/standards/security.md", BackupStore.ReadFile(h.Repo.Path, backup.Id, ".agent-standards/standards/security.md")!);
        h.Repo.Write(Manifest.RelativePath, BackupStore.ReadManifest(h.Repo.Path, backup.Id)!);

        var blocked = await h.Run("update", "--templates", v2.Path);
        Assert.Equal(1, blocked.Code);
        Assert.Contains("interrupted", blocked.Err);
        Assert.Equal(1, (await h.Run("status")).Code);
        Assert.Contains("Interrupted operation", (await h.Run("doctor")).Out);
        Assert.Equal(1, (await h.Run("validate")).Code);

        var recover = await h.Run("rollback", "--yes");
        Assert.True(recover.Code == 0, recover.ToString());
        Assert.Contains("Recovering interrupted update", recover.Out);
        Assert.Contains("already-restored", recover.Out);
        Assert.DoesNotContain("skip-modified", recover.Out);
        Assert.Null(OperationJournal.Read(h.Repo.Path));
        Assert.Equal(beforeUpdate, Content(h.Repo));
        Assert.True((await h.Run("update", "--templates", v2.Path)).Code is 0 or 2);
    }

    [Fact]
    public async Task Files_changed_between_plan_and_apply_abort_without_writing()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        var engine = h.Engine;
        var prepared = await engine.PrepareAsync(new AgentStandards.Core.InstallRequest { Root = h.Repo.Path, IsUpdate = true, Profiles = ["dotnet"] });
        Assert.Contains(prepared.Plan.Changes, c => c.Path == "AGENTS.md"); // the profile change rewrites the AGENTS.md index
        h.Repo.Write("AGENTS.md", h.Repo.Read("AGENTS.md") + "\nedited after planning\n");
        var before = Content(h.Repo);

        var ex = Assert.Throws<InvalidOperationException>(() => AgentStandards.Core.StandardsEngine.Apply(prepared, isUpdate: true));

        Assert.Contains("changed after the plan", ex.Message);
        Assert.Equal(before, Content(h.Repo));
    }

    // ---- Conflict tracking -----------------------------------------------------------------------------------

    [Fact]
    public async Task Conflict_is_resolved_automatically_when_local_matches_incoming()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        File.AppendAllText(h.Repo.Full(".agent-standards/standards/testing.md"), "\nmine\n");
        Assert.Equal(2, (await h.Run("update", "--templates", v2.Path)).Code);

        h.Repo.Write(".agent-standards/standards/testing.md", h.Repo.Read(".agent-standards/conflicts/.agent-standards/standards/testing.md.incoming"));
        var result = await h.Run("update", "--templates", v2.Path);

        Assert.True(result.Code == 0, result.ToString());
        Assert.False(h.Repo.Exists(".agent-standards/conflicts/.agent-standards/standards/testing.md.incoming"));
        Assert.False(h.Repo.Exists(".agent-standards/conflicts/.agent-standards/standards/testing.md.base"));
        Assert.All(StatusService.Get(h.Repo.Path, "x").Files, f => Assert.Equal(FileState.Unmodified, f.State));
    }

    [Fact]
    public async Task Conflict_copies_survive_repeated_updates()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        File.AppendAllText(h.Repo.Full(".agent-standards/standards/testing.md"), "\nmine\n");
        const string copies = ".agent-standards/conflicts/.agent-standards/standards/testing.md";

        Assert.Equal(2, (await h.Run("update", "--templates", v2.Path)).Code);
        var baseCopy = h.Repo.Read(copies + ".base");
        Assert.DoesNotContain("(v2)", baseCopy);

        Assert.Equal(2, (await h.Run("update", "--templates", v2.Path)).Code);
        Assert.Equal(baseCopy, h.Repo.Read(copies + ".base"));
        Assert.Contains("(v2)", h.Repo.Read(copies + ".incoming"));
    }

    [Fact]
    public async Task Upstream_revert_clears_pending_conflict_and_force_overwrites()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        File.AppendAllText(h.Repo.Full(".agent-standards/standards/testing.md"), "\nmine\n");
        Assert.Equal(2, (await h.Run("update", "--templates", v2.Path)).Code);

        var back = await h.Run("update", "--templates", v1.Path);
        Assert.True(back.Code == 0, back.ToString());
        Assert.Contains("mine", h.Repo.Read(".agent-standards/standards/testing.md"));
        Assert.DoesNotContain(ManifestStore.Load(h.Repo.Path)!.Files, f => f.PendingHash is not null);

        Assert.Equal(2, (await h.Run("update", "--templates", v2.Path)).Code);
        var forced = await h.Run("update", "--templates", v2.Path, "--force");
        Assert.Equal(0, forced.Code);
        Assert.DoesNotContain("mine", h.Repo.Read(".agent-standards/standards/testing.md"));
        Assert.DoesNotContain(ManifestStore.Load(h.Repo.Path)!.Files, f => f.PendingHash is not null);
    }

    [Fact]
    public async Task Resolve_validates_paths_and_supports_all_and_dry_run()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        File.AppendAllText(h.Repo.Full(".agent-standards/standards/testing.md"), "\nmine\n");
        File.AppendAllText(h.Repo.Full(".agent-standards/standards/security.md"), "\nmine\n");
        await h.Run("update", "--templates", v2.Path);

        Assert.Equal(1, (await h.Run("resolve", "README.md")).Code);
        Assert.Equal(1, (await h.Run("resolve", "AGENTS.md")).Code);
        Assert.Equal(1, (await h.Run("resolve")).Code);

        var dry = await h.Run("resolve", "--all", "--dry-run", "--json");
        Assert.Equal(2, dry.Json["resolved"]!.AsArray().Count);
        Assert.Equal(2, ConflictService.Pending(ManifestStore.Load(h.Repo.Path)!).Count);

        Assert.Equal(0, (await h.Run("resolve", "--all", "--accept-local", "--yes")).Code);
        Assert.Empty(ConflictService.Pending(ManifestStore.Load(h.Repo.Path)!));
        Assert.Equal(0, (await h.Run("update", "--templates", v2.Path)).Code);
        Assert.Contains("mine", h.Repo.Read(".agent-standards/standards/testing.md"));
    }

    // ---- Security -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("agent-standards/standards/a.md", "agent-standards/standards/a.md", "more than once")]
    [InlineData("agent-standards/standards/a.md", "agent-standards/Standards/A.md", "differ only by case")]
    [InlineData("agent-standards/standards/a.md", "agent-standards/standards//a.md", "more than once")]
    public void Package_with_colliding_entries_is_rejected(string first, string second, string message)
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "p.zip");
        using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        {
            Fixtures.AddText(zip, first, "one");
            Fixtures.AddText(zip, second, "two");
        }
        using var stream = File.OpenRead(path);
        var ex = Assert.Throws<TemplateException>(() => TemplateLoader.ReadPackage(stream));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public async Task Malformed_packages_fail_with_clear_errors()
    {
        using var garbage = new MemoryStream("this is not a zip archive"u8.ToArray());
        Assert.Throws<TemplateException>(() => TemplateLoader.ReadPackage(garbage));

        using var h = new Harness();
        var cache = Path.Combine(h.Home.Path, ".agent-standards", "cache", "packages", "agentstandards.templates", "95.0.0");
        Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(cache, "agentstandards.templates.95.0.0.nupkg"), "garbage");

        var result = await h.Run("init", "--version", "95.0.0", "--offline");

        Assert.Equal(1, result.Code);
        Assert.Contains("not a valid NuGet package", result.Err);
        Assert.DoesNotContain("   at ", result.Err); // no stack trace without --verbose
    }

    [Theory]
    [InlineData("\"files\": null")]
    [InlineData("\"templates\": null")]
    public async Task Incomplete_manifest_is_reported_not_crashing(string replacement)
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        var path = h.Repo.Full(Manifest.RelativePath);
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        var key = replacement.Split('"')[1];
        json[key] = null;
        File.WriteAllText(path, json.ToJsonString());

        foreach (var command in new[] { "status", "update", "validate" })
        {
            var result = await h.Run(command);
            Assert.Equal(1, result.Code);
            Assert.Contains("incomplete", result.Err);
        }
    }

    [Fact]
    public void Frontmatter_values_cannot_inject_keys()
    {
        var quoted = Frontmatter.Quote("line one\nname: injected\r\t\u0001\"quoted\" \\");
        Assert.DoesNotContain('\n', quoted);
        var fm = Frontmatter.Parse($"---\ndescription: {quoted}\n---\nbody");
        Assert.Equal(["description"], fm.Keys);
    }

    [Fact]
    public async Task Project_install_in_home_directory_is_refused()
    {
        using var h = new Harness();
        var result = await h.Run("init", "--project", h.Home.Path);

        Assert.Equal(1, result.Code);
        Assert.Contains("home directory", result.Err);
        Assert.Empty(Directory.EnumerateFileSystemEntries(h.Home.Path));
    }

    // ---- Configuration -------------------------------------------------------------------------------------

    private static TempDir TemplatesWithMcp()
    {
        var templates = new TempDir("mcp");
        Fixtures.CopyTemplates(templates.Path);
        var generic = templates.Full("profiles/generic.json");
        var json = JsonNode.Parse(File.ReadAllText(generic))!;
        json["mcpServers"] = JsonNode.Parse("{\"docs\": {\"type\": \"http\", \"url\": \"https://example.com/mcp\"}}");
        File.WriteAllText(generic, json.ToJsonString());
        return templates;
    }

    [Fact]
    public async Task Mcp_config_is_claude_only_and_differing_existing_servers_are_reported()
    {
        using var templates = TemplatesWithMcp();
        using (var h = new Harness())
        {
            var copilotOnly = await h.Run("init", "--profile", "generic", "--templates", templates.Path, "--assistants", "copilot", "--mcp");
            Assert.Equal(0, copilotOnly.Code);
            Assert.False(h.Repo.Exists(".mcp.json"));
            Assert.Contains("Copilot MCP configuration is not generated", copilotOnly.Out);
        }

        using (var h = new Harness())
        {
            const string existing = "{\"mcpServers\": {\"docs\": {\"type\": \"http\", \"url\": \"https://mine.example/mcp\"}}}";
            h.Repo.Write(".mcp.json", existing);
            var result = await h.Run("init", "--profile", "generic", "--templates", templates.Path, "--mcp");
            Assert.Equal(0, result.Code);
            Assert.Contains("kept your existing definition of docs", result.Out);
            Assert.Equal(existing, h.Repo.Read(".mcp.json"));
        }
    }

    [Fact]
    public async Task Doctor_warns_about_unsupported_config_dir_overrides()
    {
        using var h = new Harness();
        var previous = Environment.GetEnvironmentVariable("COPILOT_HOME");
        try
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", Path.Combine(h.Home.Path, "elsewhere"));
            var result = await h.Run("doctor");
            Assert.Contains("COPILOT_HOME", result.Out);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", previous);
        }
    }

    // ---- Diagnostics ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Status_reports_conflicts_unmanaged_files_and_detection()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        h.Repo.Write(".claude/agents/our-agent.md", "---\nname: our-agent\ndescription: ours\n---\nbody");
        File.AppendAllText(h.Repo.Full(".agent-standards/standards/testing.md"), "\nmine\n");
        await h.Run("update", "--templates", v2.Path);

        var status = await h.Run("status", "--json");

        Assert.Equal(0, status.Code);
        Assert.Contains(status.Json["unresolvedConflicts"]!.AsArray(), p => p!.GetValue<string>() == ".agent-standards/standards/testing.md");
        Assert.Contains(status.Json["unmanagedFiles"]!.AsArray(), p => p!.GetValue<string>() == ".claude/agents/our-agent.md");
        Assert.Contains(status.Json["files"]!.AsArray(), f => f!["state"]!.GetValue<string>() == "conflict");

        var doctor = await h.Run("doctor");
        Assert.Contains("Unresolved conflicts", doctor.Out);
        Assert.Contains("files scanned", doctor.Out);
    }

    [Fact]
    public async Task Every_json_output_is_valid_json()
    {
        using var h = new Harness();
        var commands = new[]
        {
            new[] { "init", "--profile", "generic", "--json" },
            ["update", "--dry-run", "--json"], ["diff", "--json"], ["status", "--json"], ["validate", "--json"],
            ["list", "--json"], ["doctor", "--json"], ["rollback", "--list", "--json"], ["rollback", "--dry-run", "--json"],
            ["resolve", "--all", "--dry-run", "--json"], ["update", "--profile", "nope", "--json"]
        };
        foreach (var args in commands)
        {
            var result = await h.Run(args);
            var node = JsonNode.Parse(result.Out); // throws if invalid
            Assert.NotNull(node);
        }
    }
}
