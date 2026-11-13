using System.Diagnostics;
using System.Text.Json.Nodes;
using AgentStandards.Core;
using AgentStandards.Core.Diagnostics;
using AgentStandards.Core.Installation;

namespace AgentStandards.Tests;

/// <summary>Regression tests for the final pre-release review (locking, conflict copies, transaction stages).</summary>
[Collection(FaultInjection.Name)]
public class FinalHardeningTests
{
    private static (TempDir v1, TempDir v2) Versions()
    {
        var v1 = new TempDir("v1");
        var v2 = new TempDir("v2");
        Fixtures.CopyTemplates(v1.Path, "1.0.0");
        Fixtures.CopyTemplates(v2.Path, "2.0.0");
        foreach (var name in new[] { "testing", "security" })
        {
            var path = v2.Full($"standards/{name}.md");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\n## ", "\n## (v2) "));
        }
        return (v1, v2);
    }

    [Fact]
    public async Task Second_writer_during_an_operation_is_refused()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        var a = await h.Engine.PrepareAsync(new InstallRequest { Root = h.Repo.Path, IsUpdate = true, TemplatesDirectory = v2.Path });
        var b = await h.Engine.PrepareAsync(new InstallRequest { Root = h.Repo.Path, IsUpdate = true, Profiles = ["dotnet"] });

        Exception? second = null;
        var fired = false;
        Transaction.Fault = (root, stage, _) =>
        {
            if (root != h.Repo.Path || stage != Transaction.Stage.Change || fired) return;
            fired = true;
            second = Record.Exception(() => StandardsEngine.Apply(b, isUpdate: true)); // another process starts mid-write
        };
        try
        {
            StandardsEngine.Apply(a, isUpdate: true);
        }
        finally
        {
            Transaction.Fault = null;
        }

        Assert.IsType<OperationInProgressException>(second);
        Assert.False(h.Repo.Exists(".agent-standards/standards/dotnet.md")); // B wrote nothing
        Assert.Equal("2.0.0", ManifestStore.Load(h.Repo.Path)!.Templates.Version);
        Assert.Null(OperationJournal.Read(h.Repo.Path));
        Assert.Equal(0, (await h.Run("validate")).Code);
    }

    [Fact]
    public async Task Edited_incoming_copy_survives_repeated_update()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        File.AppendAllText(h.Repo.Full(".agent-standards/standards/testing.md"), "\nmine\n");
        await h.Run("update", "--templates", v2.Path);
        const string incoming = ".agent-standards/conflicts/.agent-standards/standards/testing.md.incoming";
        h.Repo.Write(incoming, h.Repo.Read(incoming) + "\nhalf-merged work\n");

        await h.Run("update", "--templates", v2.Path);

        Assert.Contains("half-merged work", h.Repo.Read(incoming));
    }

    private const string Incoming = ".agent-standards/conflicts/.agent-standards/standards/testing.md.incoming";
    private const string BaseCopy = ".agent-standards/conflicts/.agent-standards/standards/testing.md.base";

    private static async Task<(Harness h, TempDir v1, TempDir v2)> WithConflict()
    {
        var h = new Harness();
        var (v1, v2) = Versions();
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        File.AppendAllText(h.Repo.Full(".agent-standards/standards/testing.md"), "\nmine\n");
        Assert.Equal(2, (await h.Run("update", "--templates", v2.Path)).Code);
        return (h, v1, v2);
    }

    private static SortedDictionary<string, string> State(TempDir dir) =>
        new(Fixtures.Snapshot(dir.Path)
            .Where(kv => !kv.Key.Replace('\\', '/').Contains(".agent-standards/backups") && Path.GetFileName(kv.Key) != "lock")
            .ToDictionary(kv => kv.Key, kv => kv.Value == "dir" ? "dir" : kv.Value.Split(':')[2]));

    private static IDisposable Inject(string root, Func<Transaction.Stage, string?, bool> when)
    {
        Transaction.Fault = (r, stage, path) =>
        {
            if (r == root && when(stage, path))
                throw new IOException($"simulated failure at {stage} {path}");
        };
        return new ResetFault();
    }

    private sealed class ResetFault : IDisposable
    {
        public void Dispose() => Transaction.Fault = null;
    }

    private static string[] BackupFolders(Harness h)
    {
        var dir = h.Repo.Full(".agent-standards/backups");
        return Directory.Exists(dir) ? Directory.GetDirectories(dir).Select(d => Path.GetFileName(d)!).Order().ToArray() : [];
    }

    // ---- Locking ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Held_lock_blocks_every_writer_but_not_readers()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        File.AppendAllText(h.Repo.Full(".agent-standards/standards/testing.md"), "\nmine\n");
        await h.Run("update", "--templates", v2.Path);
        var before = State(h.Repo);

        using (OperationLock.Acquire(h.Repo.Path, "test"))
        {
            foreach (var args in new[] { new[] { "update", "--templates", v1.Path }, ["init", "--force"], ["resolve", "--all"], ["rollback", "--yes"] })
            {
                var result = await h.Run(args);
                Assert.True(result.Code == 1, $"{string.Join(' ', args)}: {result}");
                Assert.Contains("Another agent-standards operation is running", result.Err);
            }
            Assert.Equal(before, State(h.Repo));

            foreach (var args in new[] { new[] { "status" }, ["validate"], ["doctor"], ["diff"], ["list"], ["update", "--dry-run"], ["rollback", "--dry-run"], ["resolve", "--all", "--dry-run"] })
                Assert.True((await h.Run(args)).Code is 0 or 2, string.Join(' ', args));
        }

        Assert.Equal(0, (await h.Run("resolve", "--all")).Code);
    }

    [Fact]
    public async Task Lock_held_by_another_process_is_respected()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        var tool = Path.Combine(AppContext.BaseDirectory, "agent-standards.dll");

        async Task<(int Code, string Err)> RunChild(params string[] args)
        {
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { RedirectStandardError = true, RedirectStandardOutput = true };
            info.ArgumentList.Add(tool);
            foreach (var a in args) info.ArgumentList.Add(a);
            info.ArgumentList.Add("--project");
            info.ArgumentList.Add(h.Repo.Path);
            info.Environment["AGENT_STANDARDS_HOME"] = h.Home.Path;
            using var process = Process.Start(info)!;
            var err = await process.StandardError.ReadToEndAsync();
            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, err);
        }

        using (OperationLock.Acquire(h.Repo.Path, "test"))
        {
            var blocked = await RunChild("update", "--profile", "dotnet");
            Assert.Equal(1, blocked.Code);
            Assert.Contains("Another agent-standards operation is running", blocked.Err);
        }

        var after = await RunChild("update", "--profile", "dotnet");
        Assert.True(after.Code == 0, after.Err);
        Assert.True(h.Repo.Exists(".agent-standards/standards/dotnet.md"));
    }

    [Fact]
    public async Task Stale_lock_file_from_a_crashed_process_does_not_block()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        h.Repo.Write(OperationLock.RelativePath, "pid 99999, update, started long ago");

        Assert.False(OperationLock.IsHeld(h.Repo.Path));
        Assert.Equal(0, (await h.Run("update", "--profile", "dotnet")).Code);
        Assert.False(OperationLock.IsHeld(h.Repo.Path));
    }

    [Fact]
    public async Task Journal_is_reported_as_in_progress_while_locked_and_interrupted_after()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        var backup = BackupStore.List(h.Repo.Path)[0];
        h.Repo.Write(OperationJournal.RelativePath, $"{{\"backupId\":\"{backup.Id}\",\"operation\":\"update\"}}");

        using (OperationLock.Acquire(h.Repo.Path, "test"))
        {
            var status = await h.Run("status");
            Assert.Equal(0, status.Code);
            Assert.Contains("IN PROGRESS", status.Out);
            Assert.Contains("is running", (await h.Run("update", "--dry-run")).Err);
        }

        var after = await h.Run("status");
        Assert.Equal(1, after.Code);
        Assert.Contains("INTERRUPTED", after.Out);
    }

    [Fact]
    public async Task Parallel_updates_never_corrupt_the_installation()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        for (var round = 0; round < 5; round++)
        {
            var results = await Task.WhenAll(
                Task.Run(() => h.Run("update", "--profile", round % 2 == 0 ? "dotnet" : "webapi")),
                Task.Run(() => h.Run("update", "--profile", round % 2 == 0 ? "maui" : "classlib")),
                Task.Run(() => h.Run("update", "--profile", "generic")));

            Assert.All(results, r => Assert.True(r.Code is 0 or 1, r.ToString()));
            Assert.All(results.Where(r => r.Code == 1), r => Assert.Matches("is running|changed after the plan", r.Err));
            Assert.Null(OperationJournal.Read(h.Repo.Path));
            Assert.False(OperationLock.IsHeld(h.Repo.Path));
            var validate = await h.Run("validate");
            Assert.True(validate.Code == 0, validate.ToString());
            Assert.All(StatusService.Get(h.Repo.Path, "x").Files, f => Assert.Equal(FileState.Unmodified, f.State));
        }
    }

    // ---- Conflict copies -------------------------------------------------------------------------------------

    [Fact]
    public async Task Edited_copies_are_kept_on_resolve_unless_forced()
    {
        var (h, v1, v2) = await WithConflict();
        using var _ = h;
        using var __ = v1;
        using var ___ = v2;
        h.Repo.Write(Incoming, h.Repo.Read(Incoming) + "\nmerge notes\n");

        var resolve = await h.Run("resolve", "--all");
        Assert.Equal(0, resolve.Code);
        Assert.Contains("kept", resolve.Out);
        Assert.True(h.Repo.Exists(Incoming));
        Assert.False(h.Repo.Exists(BaseCopy)); // unedited copy removed
        Assert.Empty(ConflictService.Pending(ManifestStore.Load(h.Repo.Path)!));

        // A later update does not delete the edited copy either, and says so.
        var update = await h.Run("update", "--templates", v2.Path);
        Assert.Equal(0, update.Code);
        Assert.Contains("because it was edited", update.Out);
        Assert.Contains("merge notes", h.Repo.Read(Incoming));

        var forced = await h.Run("update", "--templates", v2.Path, "--force");
        Assert.Equal(0, forced.Code);
        Assert.False(h.Repo.Exists(Incoming));
        Assert.Null(ManifestStore.Load(h.Repo.Path)!.ConflictCopies);
    }

    [Fact]
    public async Task Edited_copy_is_kept_when_conflict_clears_and_resolve_force_removes_copies()
    {
        var (h, v1, v2) = await WithConflict();
        using var _ = h;
        using var __ = v1;
        using var ___ = v2;
        h.Repo.Write(BaseCopy, "my annotated base");

        var revert = await h.Run("update", "--templates", v1.Path); // upstream returns to the applied version
        Assert.Equal(0, revert.Code);
        Assert.Equal("my annotated base", h.Repo.Read(BaseCopy));
        Assert.False(h.Repo.Exists(Incoming));

        Assert.Equal(2, (await h.Run("update", "--templates", v2.Path)).Code);
        Assert.Equal("my annotated base", h.Repo.Read(BaseCopy)); // still not overwritten
        Assert.Equal(0, (await h.Run("resolve", "--all", "--force")).Code);
        Assert.False(h.Repo.Exists(BaseCopy));
        Assert.False(h.Repo.Exists(Incoming));
    }

    [Fact]
    public async Task Rollback_keeps_edited_copies_and_their_tracking()
    {
        var (h, v1, v2) = await WithConflict();
        using var _ = h;
        using var __ = v1;
        using var ___ = v2;
        h.Repo.Write(Incoming, "work in progress");

        var rollback = await h.Run("rollback", "--yes");

        Assert.Equal(2, rollback.Code);
        Assert.Equal("work in progress", h.Repo.Read(Incoming));
        var manifest = ManifestStore.Load(h.Repo.Path)!;
        Assert.Equal("1.0.0", manifest.Templates.Version);
        Assert.True(manifest.ConflictCopies!.ContainsKey(Incoming)); // still known as a (now edited) toolkit copy
        Assert.Equal(0, (await h.Run("validate")).Code);
    }

    // ---- Transaction stages ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("BackupFile")]
    [InlineData("BackupRecord")]
    [InlineData("Journal")]
    [InlineData("Change")]
    [InlineData("Manifest")]
    public async Task Failure_before_commit_leaves_installation_untouched(string stage)
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        var before = State(h.Repo);
        var backups = BackupFolders(h);

        CliResult result;
        using (Inject(h.Repo.Path, (s, path) => stage switch
               {
                   "Manifest" => s == Transaction.Stage.Change && path == Manifest.RelativePath,
                   _ => s.ToString() == stage
               }))
            result = await h.Run("update", "--templates", v2.Path);

        Assert.Equal(1, result.Code);
        Assert.Contains("nothing was modified", result.Err);
        Assert.Equal(before, State(h.Repo));
        Assert.Equal(backups, BackupFolders(h)); // no partial or unused backup left behind
        Assert.Null(OperationJournal.Read(h.Repo.Path));
        Assert.False(OperationLock.IsHeld(h.Repo.Path));
        Assert.True((await h.Run("update", "--templates", v2.Path)).Code == 0);
    }

    [Fact]
    public async Task Failed_automatic_restore_leaves_a_recoverable_interrupted_state()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        var before = State(h.Repo);

        CliResult failed;
        var changes = 0;
        using (Inject(h.Repo.Path, (s, _) => s == Transaction.Stage.Restore || (s == Transaction.Stage.Change && ++changes == 3)))
            failed = await h.Run("update", "--templates", v2.Path);

        Assert.Equal(1, failed.Code);
        Assert.Contains("automatic restore also failed", failed.Err);
        Assert.NotNull(OperationJournal.Read(h.Repo.Path));
        Assert.Equal(1, (await h.Run("status")).Code);
        Assert.Equal(1, (await h.Run("update", "--templates", v2.Path)).Code);

        var recover = await h.Run("rollback", "--yes");
        Assert.True(recover.Code == 0, recover.ToString());
        Assert.Equal(before, State(h.Repo));
        Assert.Null(OperationJournal.Read(h.Repo.Path));
        Assert.Equal("1.0.0", ManifestStore.Load(h.Repo.Path)!.Templates.Version);
    }

    [Fact]
    public async Task Journal_cleanup_failure_is_not_reported_as_success()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        var before = State(h.Repo);

        CliResult result;
        using (Inject(h.Repo.Path, (s, _) => s == Transaction.Stage.JournalCleanup))
            result = await h.Run("update", "--templates", v2.Path);

        Assert.Equal(1, result.Code);
        Assert.Contains("could not remove", result.Err);
        Assert.Equal("2.0.0", ManifestStore.Load(h.Repo.Path)!.Templates.Version); // changes are complete
        Assert.Equal(1, (await h.Run("status")).Code); // but flagged as interrupted

        Assert.Equal(0, (await h.Run("rollback", "--yes")).Code); // undo is possible and safe
        Assert.Equal(before, State(h.Repo));
    }

    [Fact]
    public async Task Interrupted_recovery_can_itself_be_recovered()
    {
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        var beforeUpdate = State(h.Repo);

        // 1. The update is interrupted and cannot restore itself.
        var changes = 0;
        using (Inject(h.Repo.Path, (s, _) => s == Transaction.Stage.Restore || (s == Transaction.Stage.Change && ++changes == 3)))
            Assert.Equal(1, (await h.Run("update", "--templates", v2.Path)).Code);
        var original = OperationJournal.Read(h.Repo.Path)!;

        // 2. The recovering rollback is interrupted the same way.
        changes = 0;
        using (Inject(h.Repo.Path, (s, _) => s == Transaction.Stage.Restore || (s == Transaction.Stage.Change && ++changes == 2)))
            Assert.Equal(1, (await h.Run("rollback", "--yes")).Code);
        Assert.Equal(original.BackupId, OperationJournal.Read(h.Repo.Path)!.Previous!.BackupId);

        // 3. Recovering the rollback puts the original interruption back; recovering that restores the start state.
        var first = await h.Run("rollback", "--yes");
        Assert.True(first.Code == 0, first.ToString());
        Assert.Contains("still pending", first.Out + first.Err);
        Assert.Equal(original.BackupId, OperationJournal.Read(h.Repo.Path)!.BackupId);

        var second = await h.Run("rollback", "--yes");
        Assert.True(second.Code == 0, second.ToString());
        Assert.Null(OperationJournal.Read(h.Repo.Path));
        Assert.Equal(beforeUpdate, State(h.Repo));
    }

    [Fact]
    public async Task Unwritable_directory_fails_cleanly_and_reverts()
    {
        if (OperatingSystem.IsWindows())
            return; // directory permission bits are POSIX-specific
        using var h = new Harness();
        var (v1, v2) = Versions();
        using var _ = v1;
        using var __ = v2;
        await h.Run("init", "--profile", "generic", "--templates", v1.Path);
        var before = State(h.Repo);
        var standards = h.Repo.Full(".agent-standards/standards");
        File.SetUnixFileMode(standards, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var result = await h.Run("update", "--templates", v2.Path);
            Assert.Equal(1, result.Code);
            Assert.Contains("reverted", result.Err);
        }
        finally
        {
            File.SetUnixFileMode(standards, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Assert.Equal(before, State(h.Repo));
        Assert.Null(OperationJournal.Read(h.Repo.Path));
    }

    // ---- Existing developer configuration --------------------------------------------------------------------

    [Fact]
    public async Task Works_alongside_existing_developer_configuration()
    {
        using var h = new Harness();
        var originals = new Dictionary<string, string>
        {
            ["AGENTS.md"] = "# Our agents guide\n\n## Architecture decisions\n- We use vertical slices, not layers.\n",
            ["CLAUDE.md"] = "# Claude notes\n\nAlways run `make check`.\n",
            [".github/copilot-instructions.md"] = "Prefer our internal HTTP client wrapper.\n",
            [".claude/skills/release-notes/SKILL.md"] = "---\nname: release-notes\ndescription: Write our release notes.\n---\nSteps.\n",
            [".claude/agents/db-expert.md"] = "---\nname: db-expert\ndescription: Our database expert.\n---\nBody.\n",
            [".github/instructions/sql.instructions.md"] = "---\napplyTo: \"**/*.sql\"\n---\nUse snake_case.\n",
            [".mcp.json"] = "{\n  \"mcpServers\": { \"internal\": { \"type\": \"http\", \"url\": \"${INTERNAL_MCP}\" } }\n}\n",
            ["docs/standards/team.md"] = "# Team standards\n"
        };
        foreach (var (path, content) in originals)
            h.Repo.Write(path, content);
        var initial = Fixtures.Snapshot(h.Repo.Path);

        var init = await h.Run("init", "--profile", "generic");
        Assert.True(init.Code == 0, init.ToString());

        foreach (var path in new[] { "AGENTS.md", "CLAUDE.md", ".github/copilot-instructions.md" })
            Assert.StartsWith(originals[path], h.Repo.Read(path)); // user content first, block appended after
        foreach (var path in originals.Keys.Where(p => p is not ("AGENTS.md" or "CLAUDE.md" or ".github/copilot-instructions.md")))
            Assert.Equal(originals[path], h.Repo.Read(path)); // untouched
        Assert.Contains("documented\narchitecture decisions take precedence", h.Repo.Read("AGENTS.md"));
        Assert.Contains("It cannot relax the approval rules", h.Repo.Read("AGENTS.md"));

        var status = await h.Run("status", "--json");
        var unmanaged = status.Json["unmanagedFiles"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains(".claude/agents/db-expert.md", unmanaged);
        Assert.Contains(".mcp.json", unmanaged);
        Assert.Contains("Nothing to do", (await h.Run("update")).Out);

        // Rolling back the install restores the repository exactly.
        Assert.Equal(0, (await h.Run("rollback", "--yes")).Code);
        var restored = Fixtures.Snapshot(h.Repo.Path).Where(kv => !kv.Key.Contains(".agent-standards")).ToDictionary();
        Assert.Equal(initial.Keys.Order(), restored.Keys.Order());
        foreach (var (k, v) in initial.Where(kv => kv.Value != "dir"))
            Assert.Equal(v.Split(':')[2], restored[k].Split(':')[2]);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FaultInjection
{
    public const string Name = "fault-injection";
}
