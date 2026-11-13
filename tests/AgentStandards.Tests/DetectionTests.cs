using AgentStandards.Core.Profiles;

namespace AgentStandards.Tests;

public class DetectionTests
{
    private const string Maui = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><UseMaui>true</UseMaui><OutputType>Exe</OutputType></PropertyGroup></Project>";
    private const string Web = "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>";
    private const string WebApi = "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><ItemGroup><PackageReference Include=\"Microsoft.AspNetCore.OpenApi\" /></ItemGroup></Project>";
    private const string Lib = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
    private const string Console = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>";

    public static TheoryData<string, string[], string[]> Cases => new()
    {
        { "maui", ["src/App/App.csproj"], [Maui] },
        { "aspnetcore", ["Site/Site.csproj"], [Web] },
        { "webapi", ["Api/Api.csproj"], [WebApi] },
        { "classlib", ["Lib/Lib.csproj"], [Lib] },
        { "dotnet", ["Tool/Tool.csproj", "Lib/Lib.csproj"], [Console, Lib] },
        { "blazor", ["Ui/Ui.csproj", "Ui/Pages/Index.razor"], ["<Project Sdk=\"Microsoft.NET.Sdk.BlazorWebAssembly\"></Project>", "<h1>Hi</h1>"] },
        { "generic", ["package.json", "src/index.ts"], ["{}", "export {}"] },
        { "generic", ["README.md"], ["# empty"] }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Detects_most_specific_profile_from_repository_files(string expected, string[] files, string[] contents)
    {
        using var repo = new TempDir();
        for (var i = 0; i < files.Length; i++)
            repo.Write(files[i], contents[i]);

        var result = ProjectDetector.Detect(repo.Path, new Harness().Engine.Resolver.LoadBundled());

        Assert.Equal([expected], result.Profiles);
    }

    [Fact]
    public void Monorepo_detects_multiple_specific_profiles()
    {
        using var repo = new TempDir();
        repo.Write("mobile/App.csproj", Maui);
        repo.Write("api/Api.csproj", WebApi);

        var result = ProjectDetector.Detect(repo.Path, new Harness().Engine.Resolver.LoadBundled());

        Assert.Equal(["maui", "webapi"], result.Profiles.Order().ToArray());
    }

    [Fact]
    public void Ignores_build_output_and_dependency_folders()
    {
        using var repo = new TempDir();
        repo.Write("node_modules/pkg/x.csproj", Maui);
        repo.Write("bin/Debug/x.csproj", Maui);
        repo.Write("index.js", "");

        var result = ProjectDetector.Detect(repo.Path, new Harness().Engine.Resolver.LoadBundled());

        Assert.Equal(["generic"], result.Profiles);
        Assert.True(result.FellBackToGeneric);
    }

    [Fact]
    public async Task Explicit_profile_overrides_detection()
    {
        using var h = new Harness();
        h.Repo.Write("App/App.csproj", Maui);

        var result = await h.Run("init", "--profile", "generic", "--json", "--dry-run");

        Assert.Equal("generic", result.Json["profiles"]![0]!.GetValue<string>());
        Assert.Null(result.Json["detection"]);
    }
}
