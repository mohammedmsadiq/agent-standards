using AgentStandards.Core.Installation;

namespace AgentStandards.Tests;

/// <summary>Changes a process-wide environment variable, so it runs in the non-parallel collection.</summary>
[Collection(FaultInjection.Name)]
public class GlobalPackagesFolderTests
{
    [Fact]
    public async Task Offline_install_from_nuget_global_packages_folder()
    {
        using var h = new Harness();
        using var templates = new TempDir("tpl");
        Fixtures.CopyTemplates(templates.Path);
        var gpf = Path.Combine(h.Repo.Path, ".nuget-gpf");
        var dir = Path.Combine(gpf, "agentstandards.templates", "92.0.0");
        Fixtures.BuildPackage(dir, "agentstandards.templates", "92.0.0", templates.Path);
        File.WriteAllText(Path.Combine(dir, ".nupkg.metadata"), "{}");
        h.Repo.Write("nuget.config", """
            <configuration>
              <config><add key="globalPackagesFolder" value=".nuget-gpf" /></config>
              <packageSources><clear /></packageSources>
            </configuration>
            """);

        // NuGet lets the NUGET_PACKAGES variable override nuget.config, so pin it for this test; the collection is
        // not run in parallel with other tests.
        var previous = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", gpf);
        CliResult result;
        try
        {
            result = await h.Run("init", "--profile", "generic", "--version", "92.0.0", "--offline");
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previous);
        }

        Assert.True(result.Code == 0, result.ToString());
        Assert.Equal("92.0.0", ManifestStore.Load(h.Repo.Path)!.Templates.Version);
    }
}
