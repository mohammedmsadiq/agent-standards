using AgentStandards.Core.Installation;
using AgentStandards.Core.IO;
using AgentStandards.Core.Rendering;
using AgentStandards.Core.Templates;
using NuGet.Configuration;

namespace AgentStandards.Core.Diagnostics;

public enum CheckStatus
{
    Ok,
    Info,
    Warning,
    Error
}

public sealed record DoctorCheck(string Name, CheckStatus Status, string Detail);

/// <summary>Read-only environment and installation diagnostics. Never writes and never contacts the network.</summary>
public static class Doctor
{
    public static IReadOnlyList<DoctorCheck> Run(ToolEnvironment env, TemplateResolver resolver, string root, InstallScope scope)
    {
        var checks = new List<DoctorCheck>
        {
            new(".NET runtime", CheckStatus.Ok, Environment.Version.ToString()),
            new("Tool version", CheckStatus.Ok, env.ToolVersion)
        };

        try
        {
            var bundled = resolver.LoadBundled();
            var errors = TemplateValidator.Validate(bundled).Where(i => i.Severity == Severity.Error).ToList();
            checks.Add(errors.Count == 0
                ? new("Bundled templates", CheckStatus.Ok, $"{bundled.PackageId} {bundled.Version}: {bundled.Profiles.Count} profiles, {bundled.Skills.Count} skills, {bundled.Agents.Count} agents")
                : new("Bundled templates", CheckStatus.Error, $"{errors.Count} validation error(s); first: {errors[0]}"));
        }
        catch (TemplateException ex)
        {
            checks.Add(new("Bundled templates", CheckStatus.Error, ex.Message));
        }

        checks.Add(Directory.Exists(env.HomeDirectory)
            ? new("Home directory", CheckStatus.Ok, env.HomeDirectory)
            : new("Home directory", CheckStatus.Error, $"{env.HomeDirectory} does not exist"));

        try
        {
            var settings = Settings.LoadDefaultSettings(Directory.Exists(root) ? root : env.HomeDirectory);
            var sources = new PackageSourceProvider(settings).LoadPackageSources().Where(s => s.IsEnabled).Select(s => s.Name).ToList();
            checks.Add(new("NuGet sources", sources.Count > 0 ? CheckStatus.Ok : CheckStatus.Warning,
                sources.Count > 0 ? string.Join(", ", sources) : "none enabled; only bundled, cached or --source templates are available"));
            checks.Add(new("NuGet global packages folder", CheckStatus.Info, SettingsUtility.GetGlobalPackagesFolder(settings)));
        }
        catch (Exception ex) when (ex is NuGetConfigurationException or IOException or UnauthorizedAccessException)
        {
            checks.Add(new("NuGet configuration", CheckStatus.Warning, ex.Message));
        }

        checks.Add(new("Template cache", CheckStatus.Info, env.PackageCacheDirectory));

        if (!Directory.Exists(root))
        {
            checks.Add(new("Target", CheckStatus.Error, $"{root} does not exist"));
            return checks;
        }

        checks.Add(new("Target", CheckStatus.Ok, $"{root} ({scope.ToString().ToLowerInvariant()} scope)"));
        if (scope == InstallScope.Project)
            checks.Add(Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git"))
                ? new("Version control", CheckStatus.Ok, "Git repository detected; review generated changes with 'git diff' before committing")
                : new("Version control", CheckStatus.Warning, "not a Git repository root; rollback relies on the toolkit's own backups"));

        if (SafePaths.IsLink(Path.Combine(root, RenderContext.StateFolder)))
        {
            checks.Add(new("State folder", CheckStatus.Error, $"{RenderContext.StateFolder} is a symbolic link; the toolkit refuses to write through it"));
            return checks;
        }

        Manifest? manifest = null;
        try
        {
            manifest = ManifestStore.Load(root);
            checks.Add(manifest is null
                ? new("Manifest", CheckStatus.Info, "not installed here; run 'agent-standards init'")
                : new("Manifest", CheckStatus.Ok, $"templates {manifest.Templates.Version}{(manifest.Templates.Pinned ? " (pinned)" : "")}, profiles {string.Join(", ", manifest.Profiles)}, assistants {string.Join(", ", manifest.Assistants)}"));
        }
        catch (ManifestException ex)
        {
            checks.Add(new("Manifest", CheckStatus.Error, ex.Message));
        }

        if (manifest is not null)
        {
            var states = manifest.Files.Select(f =>
            {
                try { return StatusService.GetState(root, f); }
                catch (UnsafePathException) { return FileState.Malformed; }
            }).ToList();
            var modified = states.Count(s => s == FileState.Modified);
            var missing = states.Count(s => s is FileState.Missing or FileState.BlockMissing);
            var malformed = states.Count(s => s == FileState.Malformed);
            checks.Add(new("Managed files", malformed > 0 ? CheckStatus.Error : modified + missing > 0 ? CheckStatus.Info : CheckStatus.Ok,
                $"{manifest.Files.Count} tracked, {modified} modified locally, {missing} missing, {malformed} malformed or unsafe"));

            var conflicts = StatusService.PendingConflicts(root);
            if (conflicts.Count > 0)
                checks.Add(new("Pending conflicts", CheckStatus.Warning, $"{conflicts.Count} file(s) under {InstallPlanner.ConflictsFolder}/ await manual merge"));

            if (NuGet.Versioning.NuGetVersion.TryParse(resolver.BundledVersion, out var bundledVersion) &&
                NuGet.Versioning.NuGetVersion.TryParse(manifest.Templates.Version, out var installed) && bundledVersion > installed && !manifest.Templates.Pinned)
                checks.Add(new("Templates", CheckStatus.Info, $"newer bundled templates {bundledVersion} available; run 'agent-standards update'"));
        }

        if (scope == InstallScope.Project)
        {
            var claude = Path.Combine(root, "CLAUDE.md");
            if (File.Exists(claude) && File.Exists(Path.Combine(root, "AGENTS.md")) &&
                !FileValidators.StripCode(File.ReadAllText(claude)).Contains("@AGENTS.md", StringComparison.Ordinal))
                checks.Add(new("Claude Code", CheckStatus.Warning, "CLAUDE.md exists but does not import @AGENTS.md, so Claude Code will not see AGENTS.md"));
        }

        return checks;
    }
}
