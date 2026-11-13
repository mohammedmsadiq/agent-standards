using System.Reflection;

namespace AgentStandards.Core;

/// <summary>Process-level settings: home directory, tool version and bundled templates location.</summary>
public sealed class ToolEnvironment
{
    /// <summary>Overrides the home directory used for global scope and the template cache.</summary>
    public const string HomeOverrideVariable = "AGENT_STANDARDS_HOME";

    public required string HomeDirectory { get; init; }
    public required string ToolVersion { get; init; }
    /// <summary>Directory containing <c>bundle.json</c> and the bundled <c>agent-standards/</c> template tree.</summary>
    public required string BundledTemplatesRoot { get; init; }
    public bool IsInteractive { get; init; }

    public string StateDirectory => Path.Combine(HomeDirectory, ".agent-standards");
    public string PackageCacheDirectory => Path.Combine(StateDirectory, "cache", "packages");

    public static ToolEnvironment FromProcess()
    {
        var home = Environment.GetEnvironmentVariable(HomeOverrideVariable);
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected &&
                          string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) &&
                          string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD"));

        return new ToolEnvironment
        {
            HomeDirectory = Path.GetFullPath(home),
            ToolVersion = GetToolVersion(),
            BundledTemplatesRoot = Path.Combine(AppContext.BaseDirectory, "templates"),
            IsInteractive = interactive
        };
    }

    public static string GetToolVersion()
    {
        var info = typeof(ToolEnvironment).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? typeof(ToolEnvironment).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }
}
