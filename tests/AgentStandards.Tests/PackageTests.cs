using System.IO.Compression;
using AgentStandards.Core;
using AgentStandards.Core.Installation;
using AgentStandards.Core.Templates;

namespace AgentStandards.Tests;

public class PackageTests
{
    private const string Id = "AgentStandards.Templates";

    private static (TempDir feed, TempDir templates) MakeFeed(params string[] versions)
    {
        var feed = new TempDir("feed");
        var templates = new TempDir("tpl");
        Fixtures.CopyTemplates(templates.Path);
        foreach (var version in versions)
        {
            var marker = templates.Full("standards/testing.md");
            File.WriteAllText(marker, File.ReadAllText(marker).Replace("# Testing", $"# Testing").TrimEnd() + $"\n\n<!-- templates {version} -->\n");
            Fixtures.BuildPackage(feed.Path, Id, version, templates.Path);
            File.WriteAllText(marker, File.ReadAllText(marker).Replace($"\n\n<!-- templates {version} -->\n", "\n"));
        }
        return (feed, templates);
    }

    [Fact]
    public async Task Version_pinning_from_a_local_feed_and_switching_to_latest()
    {
        using var h = new Harness();
        var (feed, templates) = MakeFeed("90.0.0", "91.0.0");
        using var _ = feed;
        using var __ = templates;

        var init = await h.Run("init", "--profile", "generic", "--version", "90.0.0", "--source", feed.Path);
        Assert.True(init.Code == 0, init.ToString());
        var manifest = ManifestStore.Load(h.Repo.Path)!;
        Assert.Equal("90.0.0", manifest.Templates.Version);
        Assert.True(manifest.Templates.Pinned);
        Assert.Equal("package", manifest.Templates.Origin);
        Assert.Contains("templates 90.0.0", h.Repo.Read(".agent-standards/standards/testing.md"));

        // Pinned: a plain update stays on 90.0.0 (served from the tool cache, offline).
        var stay = await h.Run("update", "--offline");
        Assert.True(stay.Code == 0, stay.ToString());
        Assert.Contains("pinned", stay.Out);
        Assert.Equal("90.0.0", ManifestStore.Load(h.Repo.Path)!.Templates.Version);

        var latest = await h.Run("update", "--version", "latest", "--source", feed.Path);
        Assert.True(latest.Code == 0, latest.ToString());
        manifest = ManifestStore.Load(h.Repo.Path)!;
        Assert.Equal("91.0.0", manifest.Templates.Version);
        Assert.False(manifest.Templates.Pinned);
        Assert.Contains("templates 91.0.0", h.Repo.Read(".agent-standards/standards/testing.md"));

        // Unpinned, newer than bundled: update does not downgrade.
        var again = await h.Run("update", "--offline");
        Assert.True(again.Code == 0, again.ToString());
        Assert.Equal("91.0.0", ManifestStore.Load(h.Repo.Path)!.Templates.Version);

        // Rollback returns to the pinned 90.0.0 install.
        Assert.Equal(0, (await h.Run("rollback", "--yes")).Code);
        manifest = ManifestStore.Load(h.Repo.Path)!;
        Assert.Equal("90.0.0", manifest.Templates.Version);
        Assert.True(manifest.Templates.Pinned);
    }

    [Fact]
    public async Task Offline_install_from_folder_source()
    {
        using var h = new Harness();
        var (feed, templates) = MakeFeed("90.1.0");
        using var _ = feed;
        using var __ = templates;

        // Without a usable offline source the error is explicit and nothing is written.
        var missing = await h.Run("init", "--profile", "generic", "--version", "90.1.0", "--source", "https://example.invalid/v3/index.json", "--offline");
        Assert.Equal(1, missing.Code);
        Assert.Contains("not available offline", missing.Err);
        Assert.False(h.Repo.Exists(".agent-standards/manifest.json"));

        // --offline still allows local folder sources.
        var result = await h.Run("init", "--profile", "generic", "--version", "90.1.0", "--source", feed.Path, "--offline");
        Assert.True(result.Code == 0, result.ToString());
        Assert.True(File.Exists(Path.Combine(h.Home.Path, ".agent-standards", "cache", "packages", "agentstandards.templates", "90.1.0", "agentstandards.templates.90.1.0.nupkg")));
    }

    [Fact]
    public async Task Bundled_templates_work_without_any_package_source()
    {
        using var h = new Harness();
        h.Repo.Write("nuget.config", "<configuration><packageSources><clear /></packageSources></configuration>");

        var result = await h.Run("init", "--profile", "dotnet", "--offline");

        Assert.Equal(0, result.Code);
        Assert.Equal("bundled", ManifestStore.Load(h.Repo.Path)!.Templates.Origin);
    }

    [Fact]
    public async Task Missing_version_reports_clear_error()
    {
        using var h = new Harness();
        var (feed, templates) = MakeFeed("90.0.0");
        using var _ = feed;
        using var __ = templates;

        var result = await h.Run("init", "--version", "99.9.9", "--source", feed.Path);

        Assert.Equal(1, result.Code);
        Assert.Contains("99.9.9 was not found", result.Err);
        Assert.False(h.Repo.Exists(".agent-standards/manifest.json"));
    }

    [Fact]
    public void Package_reader_rejects_zip_slip_symlinks_and_oversized_entries()
    {
        using var t = new TempDir();

        foreach (var (name, build) in new (string, Action<ZipArchive>)[]
                 {
                     ("slip", z => Fixtures.AddText(z, "agent-standards/../../evil.md", "x")),
                     ("abs", z => Fixtures.AddText(z, "agent-standards//etc/passwd", "x")),
                     ("link", z => { var e = z.CreateEntry("agent-standards/link.md"); e.ExternalAttributes = unchecked((int)(0xA1FFu << 16)); using var s = e.Open(); s.WriteByte(65); }),
                     ("big", z => Fixtures.AddText(z, "agent-standards/big.md", new string('x', (int)TemplateLoader.MaxFileBytes + 1)))
                 })
        {
            var path = Path.Combine(t.Path, name + ".zip");
            using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
            {
                Fixtures.AddText(zip, "agent-standards/catalog.json", "{\"schemaVersion\":1}");
                build(zip);
            }
            using var stream = File.OpenRead(path);
            Assert.Throws<TemplateException>(() => TemplateLoader.ReadPackage(stream));
        }
    }

    [Fact]
    public async Task Cached_package_with_wrong_identity_is_rejected()
    {
        using var h = new Harness();
        using var templates = new TempDir("tpl");
        Fixtures.CopyTemplates(templates.Path);
        var cacheDir = Path.Combine(h.Home.Path, ".agent-standards", "cache", "packages", "agentstandards.templates", "93.0.0");
        var built = Fixtures.BuildPackage(cacheDir, Id, "94.0.0", templates.Path);
        File.Move(built, Path.Combine(cacheDir, "agentstandards.templates.93.0.0.nupkg"));

        var result = await h.Run("init", "--version", "93.0.0", "--offline");

        Assert.Equal(1, result.Code);
        Assert.Contains("identifies as", result.Err);
    }

    [Fact]
    public void Unsupported_template_schema_is_rejected()
    {
        var files = new Dictionary<string, string> { ["catalog.json"] = "{\"schemaVersion\": 99}" };
        var ex = Assert.Throws<TemplateException>(() => TemplateSet.Create(files, Id, "1.0.0", "test"));
        Assert.Contains("Update the agent-standards tool", ex.Message);
    }

    [Fact]
    public async Task Real_packed_templates_package_is_consumable_when_available()
    {
        // CI packs before testing and sets AGENT_STANDARDS_PACKAGES to the output folder; locally this is skipped.
        var folder = Environment.GetEnvironmentVariable("AGENT_STANDARDS_PACKAGES");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return;
        var nupkg = Directory.GetFiles(folder, "AgentStandards.Templates.*.nupkg").Single(f => !f.EndsWith(".snupkg"));
        var version = Path.GetFileNameWithoutExtension(nupkg)["AgentStandards.Templates.".Length..];

        using var h = new Harness();
        await using (var stream = File.OpenRead(nupkg))
        {
            var reader = new NuGet.Packaging.PackageArchiveReader(stream);
            Assert.DoesNotContain(reader.GetFiles(), f => f.StartsWith("content/") || f.StartsWith("contentFiles/") || f.StartsWith("lib/") || f.StartsWith("build/"));
        }

        // Load through the package path explicitly (the same version is also bundled, which would short-circuit).
        var resolver = new TemplateResolver(h.Env);
        var set = await resolver.LoadVersionAsync(new TemplateRequest { Version = version, Sources = [folder], ConfigRoot = h.Repo.Path, AllowCacheWrite = false },
            NuGet.Versioning.NuGetVersion.Parse(version), CancellationToken.None);
        Assert.Equal(version, set.Version);
        Assert.DoesNotContain(AgentStandards.Core.Diagnostics.TemplateValidator.Validate(set), i => i.Severity == AgentStandards.Core.Diagnostics.Severity.Error);
    }
}
