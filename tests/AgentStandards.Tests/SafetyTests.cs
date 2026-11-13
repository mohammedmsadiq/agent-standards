using AgentStandards.Core.Installation;
using AgentStandards.Core.IO;
using AgentStandards.Core.Templates;

namespace AgentStandards.Tests;

public class SafetyTests
{
    [Fact]
    public async Task Dry_run_and_read_only_commands_make_no_filesystem_changes()
    {
        using var h = new Harness();
        using var v2 = new TempDir("v2");
        Fixtures.CopyTemplates(v2.Path, "2.0.0");
        File.AppendAllText(v2.Full("standards/testing.md"), "\nchanged\n");
        h.Repo.Write("CLAUDE.md", "# mine\n");

        var repoBefore = Fixtures.Snapshot(h.Repo.Path);
        var homeBefore = Fixtures.Snapshot(h.Home.Path);
        Assert.Equal(0, (await h.Run("init", "--dry-run")).Code);
        Assert.Equal(0, (await h.Run("init", "--global", "--dry-run")).Code);
        Assert.Equal(0, (await h.Run("diff")).Code);
        Assert.Equal(repoBefore, Fixtures.Snapshot(h.Repo.Path));
        Assert.Equal(homeBefore, Fixtures.Snapshot(h.Home.Path));

        await h.Run("init");
        var installed = Fixtures.Snapshot(h.Repo.Path);
        homeBefore = Fixtures.Snapshot(h.Home.Path);
        foreach (var args in new[]
                 {
                     new[] { "update", "--dry-run", "--templates", v2.Path },
                     ["update", "--dry-run", "--force", "--prune", "--profile", "webapi"],
                     ["diff", "--templates", v2.Path],
                     ["status"], ["status", "--json"], ["validate"], ["doctor"], ["list"],
                     ["rollback", "--dry-run"], ["rollback", "--list"]
                 })
        {
            var result = await h.Run(args);
            Assert.True(result.Code is 0 or 2, $"{string.Join(' ', args)}: {result}");
            Assert.Equal(installed, Fixtures.Snapshot(h.Repo.Path));
        }
        Assert.Equal(homeBefore, Fixtures.Snapshot(h.Home.Path));
    }

    [Fact]
    public async Task Diff_shows_unified_changes()
    {
        using var h = new Harness();
        using var v2 = new TempDir("v2");
        Fixtures.CopyTemplates(v2.Path, "2.0.0");
        File.WriteAllText(v2.Full("standards/testing.md"), File.ReadAllText(v2.Full("standards/testing.md")).Replace("## Expectations", "## Expectations (v2)"));
        await h.Run("init", "--profile", "generic");

        var diff = await h.Run("diff", "--templates", v2.Path);

        Assert.Contains("--- a/.agent-standards/standards/testing.md", diff.Out);
        Assert.Contains("-## Expectations", diff.Out);
        Assert.Contains("+## Expectations (v2)", diff.Out);
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("a/../../outside.md")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\x.md")]
    [InlineData("a/./b.md")]
    [InlineData("a\0b.md")]
    [InlineData("")]
    public void Unsafe_relative_paths_are_rejected(string path)
    {
        using var root = new TempDir();
        Assert.Throws<UnsafePathException>(() => SafePaths.Resolve(root.Path, path));
    }

    [Fact]
    public void Safe_paths_are_normalised_to_forward_slashes()
    {
        Assert.Equal("a/b/c.md", SafePaths.NormalizeRelative("a\\b//c.md"));
    }

    [Fact]
    public async Task Manifest_with_traversal_path_is_rejected()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        var manifestPath = h.Repo.Full(".agent-standards/manifest.json");
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("\"path\": \"AGENTS.md\"", "\"path\": \"../../outside.md\""));

        var result = await h.Run("update");

        Assert.Equal(1, result.Code);
        Assert.Contains("unsafe path", result.Err);
    }

    [Fact]
    public async Task Writes_through_symbolic_links_are_refused()
    {
        using var h = new Harness();
        using var outside = new TempDir("outside");
        if (!Fixtures.TryCreateSymlink(h.Repo.Full(".claude"), outside.Path, directory: true))
            return; // symlinks unavailable (e.g. Windows without Developer Mode)

        var result = await h.Run("init", "--profile", "generic");

        Assert.Equal(1, result.Code);
        Assert.Contains("symbolic link", result.Err);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside.Path));
    }

    [Fact]
    public async Task Symlinked_managed_file_is_refused_and_target_untouched()
    {
        using var h = new Harness();
        using var outside = new TempDir("outside");
        var target = outside.Write("secret.md", "do not touch");
        if (!Fixtures.TryCreateSymlink(h.Repo.Full("AGENTS.md"), target, directory: false))
            return;

        var result = await h.Run("init", "--profile", "generic");

        Assert.Equal(1, result.Code);
        Assert.Equal("do not touch", File.ReadAllText(target));
    }

    [Fact]
    public void Template_directory_with_symlink_is_refused()
    {
        using var templates = new TempDir("tpl");
        using var outside = new TempDir("outside");
        Fixtures.CopyTemplates(templates.Path);
        var secret = outside.Write("secret.md", "x");
        if (!Fixtures.TryCreateSymlink(templates.Full("standards/linked.md"), secret, directory: false))
            return;

        Assert.Throws<TemplateException>(() => TemplateLoader.ReadDirectory(templates.Path));
    }

    [Fact]
    public async Task Corrupt_manifest_is_reported_and_recoverable_with_force()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        h.Repo.Write(".agent-standards/manifest.json", "{ not json");

        var update = await h.Run("update");
        Assert.Equal(1, update.Code);
        Assert.Contains("corrupt", update.Err);
        Assert.Equal(1, (await h.Run("status")).Code);
        Assert.Equal(1, (await h.Run("validate")).Code);
        var doctor = await h.Run("doctor");
        Assert.Equal(1, doctor.Code);
        Assert.Contains("[error  ] Manifest", doctor.Out);

        var init = await h.Run("init", "--force");
        Assert.True(init.Code == 0, init.ToString());
        Assert.NotNull(ManifestStore.Load(h.Repo.Path));
        Assert.Equal(0, (await h.Run("validate")).Code);
        var backup = BackupStore.List(h.Repo.Path)[0];
        Assert.Equal("{ not json", BackupStore.ReadManifest(h.Repo.Path, backup.Id));
    }

    [Fact]
    public async Task Newer_manifest_schema_is_rejected()
    {
        using var h = new Harness();
        await h.Run("init", "--profile", "generic");
        var path = h.Repo.Full(".agent-standards/manifest.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 7"));

        var result = await h.Run("status");

        Assert.Equal(1, result.Code);
        Assert.Contains("schema 7", result.Err);
    }

    [Fact]
    public async Task Mcp_servers_are_added_without_changing_existing_entries()
    {
        using var h = new Harness();
        using var templates = new TempDir("tpl");
        Fixtures.CopyTemplates(templates.Path);
        var generic = templates.Full("profiles/generic.json");
        File.WriteAllText(generic, File.ReadAllText(generic).TrimEnd().TrimEnd('}') +
            ", \"mcpServers\": { \"docs\": { \"type\": \"http\", \"url\": \"https://example.com/mcp\" }, \"tool\": { \"type\": \"stdio\", \"command\": \"npx\", \"args\": [\"tool\"], \"env\": { \"TOKEN\": \"${TOOL_TOKEN}\" } } } }");
        const string existing = "{ \"mcpServers\": { \"docs\": { \"type\": \"http\", \"url\": \"https://internal.example/mcp\" } }, \"other\": true }";
        h.Repo.Write(".mcp.json", existing);

        var withoutFlag = await h.Run("init", "--profile", "generic", "--templates", templates.Path, "--dry-run", "--json");
        Assert.DoesNotContain(withoutFlag.Json["actions"]!.AsArray(), a => a!["path"]!.GetValue<string>() == ".mcp.json");

        var result = await h.Run("init", "--profile", "generic", "--templates", templates.Path, "--mcp");
        Assert.True(result.Code == 0, result.ToString());
        var json = System.Text.Json.Nodes.JsonNode.Parse(h.Repo.Read(".mcp.json"))!;
        Assert.Equal("https://internal.example/mcp", json["mcpServers"]!["docs"]!["url"]!.GetValue<string>());
        Assert.Equal("npx", json["mcpServers"]!["tool"]!["command"]!.GetValue<string>());
        Assert.True(json["other"]!.GetValue<bool>());
        Assert.Equal(0, (await h.Run("validate", "--templates", templates.Path)).Code);
    }

    [Fact]
    public async Task Template_validation_rejects_literal_mcp_secrets_and_machine_paths()
    {
        using var h = new Harness();
        using var templates = new TempDir("tpl");
        Fixtures.CopyTemplates(templates.Path);
        var generic = templates.Full("profiles/generic.json");
        File.WriteAllText(generic, File.ReadAllText(generic).TrimEnd().TrimEnd('}') +
            ", \"mcpServers\": { \"bad\": { \"command\": \"/Users/me/bin/server\", \"env\": { \"API_KEY\": \"sk-live-123\" } } } }");

        var result = await h.Run("validate", "--templates", templates.Path);

        Assert.Equal(1, result.Code);
        Assert.Contains("environment variable", result.Err);
        Assert.Contains("absolute path", result.Err);
    }
}
