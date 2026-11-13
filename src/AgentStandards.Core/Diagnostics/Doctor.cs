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

        // Documented overrides that agent-standards deliberately does not follow (see docs/assistants.md).
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")))
            checks.Add(new("CLAUDE_CONFIG_DIR", CheckStatus.Warning,
                "is set. Claude Code documents it for settings, history and plugins, not for CLAUDE.md, rules, skills or agents; global installs still target ~/.claude. Verify with /context in Claude Code."));
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COPILOT_HOME")))
            checks.Add(new("COPILOT_HOME", CheckStatus.Warning,
                "is set. It relocates Copilot CLI's configuration folder, but VS Code reads personal instructions, skills and agents from ~/.copilot; global installs still target ~/.copilot."));

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

        if (OperationLock.IsHeld(root))
            checks.Add(new("Operation in progress", CheckStatus.Info, "another agent-standards process is changing this installation; results may change when it finishes"));
        else if (OperationJournal.Read(root) is { } journal)
            checks.Add(new("Interrupted operation", CheckStatus.Error,
                $"'{journal.Operation}' (backup {journal.BackupId}) did not finish. Run 'agent-standards rollback' to restore the previous state, or 'agent-standards complete' to keep it if every change was written."));

        if (scope == InstallScope.Project)
        {
            try
            {
                var detection = Profiles.ProjectDetector.Detect(root, resolver.LoadBundled());
                checks.Add(new("Detection", detection.Truncated ? CheckStatus.Warning : CheckStatus.Info,
                    $"{string.Join(", ", detection.Profiles)}{(detection.FellBackToGeneric ? " (no framework detected)" : "")}; {detection.FilesScanned} files scanned{(detection.Truncated ? " — file limit reached, results may be incomplete; use --profile" : "")}. Detection {detection.Limits}."));
            }
            catch (TemplateException)
            {
                // Already reported under "Bundled templates".
            }
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

            var pending = ConflictService.Pending(manifest);
            if (pending.Count > 0)
                checks.Add(new("Unresolved conflicts", CheckStatus.Warning,
                    $"{string.Join(", ", pending.Select(p => p.Path))}: merge the copies under {InstallPlanner.ConflictsFolder}/, then run 'agent-standards resolve <path>'"));

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
