using AgentStandards.Core.Adapters;
using AgentStandards.Core.Installation;
using AgentStandards.Core.IO;
using AgentStandards.Core.Profiles;
using AgentStandards.Core.Rendering;
using AgentStandards.Core.Templates;
using NuGet.Versioning;

namespace AgentStandards.Core;

public sealed class InstallRequest
{
    public required string Root { get; init; }
    public InstallScope Scope { get; init; } = InstallScope.Project;
    public bool IsUpdate { get; init; }
    public IReadOnlyList<string>? Profiles { get; init; }
    public IReadOnlyList<string>? Assistants { get; init; }
    public IReadOnlyList<string>? Exclude { get; init; }
    public bool? Mcp { get; init; }
    public string? Version { get; init; }
    public string? PackageId { get; init; }
    public string? TemplatesDirectory { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
    public bool Offline { get; init; }
    public bool Prerelease { get; init; }
    public bool Force { get; init; }
    public bool Prune { get; init; }
    public bool DryRun { get; init; }
}

public sealed class PreparedOperation
{
    public required InstallPlan Plan { get; init; }
    public required TemplateSet Templates { get; init; }
    public required ResolvedProfiles Profiles { get; init; }
    public DetectionResult? Detection { get; init; }
    public Manifest? Previous { get; init; }
    public List<string> Messages { get; } = [];
}

public sealed class OperationResult
{
    public required PreparedOperation Prepared { get; init; }
    public ExecutionResult? Execution { get; init; }
    public bool Applied => Execution is not null;
}

/// <summary>High-level entry point that ties together template resolution, rendering, planning and execution.</summary>
public sealed class StandardsEngine(ToolEnvironment environment)
{
    public ToolEnvironment Environment => environment;
    public TemplateResolver Resolver { get; } = new(environment);

    public string ResolveRoot(InstallScope scope, string? project) => scope == InstallScope.Global
        ? environment.HomeDirectory
        : Path.GetFullPath(project ?? System.Environment.CurrentDirectory);

    public async Task<PreparedOperation> PrepareAsync(InstallRequest request, CancellationToken ct = default)
    {
        var root = Path.GetFullPath(request.Root);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Directory '{root}' does not exist.");
        if (SafePaths.IsLink(Path.Combine(root, RenderContext.StateFolder)))
            throw new UnsafePathException(RenderContext.StateFolder, "the state folder is a symbolic link");
        if (request.Scope == InstallScope.Project)
            EnsureNotGlobalConfiguration(root);
        if (OperationJournal.Read(root) is not null && OperationLock.IsHeld(root))
            throw new OperationInProgressException($"Another agent-standards operation is running on '{root}'. Wait for it to finish, then retry.");
        if (OperationJournal.Read(root) is { } journal)
            throw new InterruptedOperationException(
                $"An interrupted '{journal.Operation}' operation (backup {journal.BackupId}) was found in '{root}'. " +
                "Run 'agent-standards rollback' to restore the state before it, then retry.");

        Manifest? previous = null;
        string? previousText = null;
        var messages = new List<string>();
        try
        {
            previous = ManifestStore.Load(root);
            previousText = AtomicFile.ReadTextOrNull(root, Manifest.RelativePath);
        }
        catch (ManifestException) when (!request.IsUpdate && request.Force)
        {
            previousText = AtomicFile.ReadTextOrNull(root, Manifest.RelativePath);
            messages.Add("The existing manifest is corrupt and will be replaced (--force). It is kept in the backup. Previously managed files are treated as unmanaged.");
        }

        if (request.IsUpdate && previous is null)
            throw new ManifestException($"No installation found in '{root}'. Run 'agent-standards init' first.");
        if (!request.IsUpdate && previous is not null)
            messages.Add("Standards are already installed here; applying as an update with the given options.");
        if (previous is not null && previous.Scope != request.Scope)
            throw new ManifestException($"The manifest in '{root}' is for {previous.Scope} scope, not {request.Scope}.");

        if (request.Scope == InstallScope.Global)
            messages.AddRange(GlobalLocationWarnings());

        var templateRequest = BuildTemplateRequest(request, previous, root, messages, out var pinned);
        var templates = await Resolver.ResolveAsync(templateRequest, ct);

        DetectionResult? detection = null;
        var profiles = request.Profiles?.Count > 0 ? request.Profiles : previous?.Profiles.Count > 0 ? previous.Profiles : null;
        if (profiles is null)
        {
            if (request.Scope == InstallScope.Global)
            {
                profiles = [ProjectDetector.FallbackProfile];
            }
            else
            {
                detection = ProjectDetector.Detect(root, templates);
                profiles = detection.Profiles;
            }
        }

        var assistants = (request.Assistants?.Count > 0 ? request.Assistants : previous?.Assistants.Count > 0 ? previous.Assistants : AdapterRegistry.DefaultIds)
            .Select(a => AdapterRegistry.Get(a).Id).Distinct().ToList();
        var exclude = (request.Exclude ?? previous?.Exclude ?? []).ToList();
        var mcp = request.Mcp ?? previous?.Mcp ?? false;
        if (mcp && request.Scope == InstallScope.Global)
            throw new InvalidOperationException("MCP configuration is project-scoped only; global assistant MCP settings are never modified.");

        var resolved = ProfileResolver.Resolve(templates, profiles, exclude);
        var context = new RenderContext { Templates = templates, Profiles = resolved, Scope = request.Scope, Assistants = assistants, IncludeMcp = mcp };
        if (mcp && resolved.McpServers.Count == 0)
            messages.Add("--mcp was requested but the selected profiles declare no MCP servers; no MCP configuration was generated.");
        else if (mcp && !assistants.Contains("claude"))
            messages.Add("--mcp only generates Claude Code's .mcp.json; GitHub Copilot MCP configuration is not generated (see docs/assistants.md).");

        var rendered = ContentBuilder.RenderShared(context)
            .Concat(assistants.SelectMany(a => AdapterRegistry.Get(a).Render(context)))
            .ToList();

        var actions = InstallPlanner.Plan(root, rendered, previous, new PlanOptions(request.Force, request.Prune));
        var now = DateTimeOffset.UtcNow;
        var manifest = new Manifest
        {
            ToolVersion = environment.ToolVersion,
            Scope = request.Scope,
            Templates = new TemplatesInfo
            {
                PackageId = templates.PackageId,
                Version = templates.Version,
                Pinned = pinned,
                Origin = templateRequest.TemplatesDirectory is not null ? "directory" : templates.Source == "bundled" ? "bundled" : "package",
                Directory = templateRequest.TemplatesDirectory is null ? null : StoreDirectory(root, templateRequest.TemplatesDirectory)
            },
            Profiles = resolved.Requested.ToList(),
            ResolvedProfiles = resolved.Ordered.Select(p => p.Id).ToList(),
            Assistants = assistants,
            Exclude = exclude,
            Mcp = mcp,
            InstalledAt = previous?.InstalledAt ?? now,
            UpdatedAt = previous?.UpdatedAt ?? now,
            Files = actions.Where(a => a.Entry is not null).Select(a => a.Entry!).OrderBy(e => e.Path, StringComparer.Ordinal).ToList()
        };

        if (previous is not null && actions.Any(a => a.Kind == PlanActionKind.Conflict && a.Entry is not null))
            await AddConflictBaselinesAsync(root, previous, actions, ct);

        var changeSet = InstallPlanner.BuildFileChanges(root, actions, previous?.ConflictCopies, request.Force);
        var changes = changeSet.Changes;
        messages.AddRange(changeSet.Warnings);
        manifest.ConflictCopies = changeSet.ConflictCopies.Count > 0 ? changeSet.ConflictCopies : null;
        if (changes.Count > 0)
            manifest.UpdatedAt = now;
        var manifestText = ManifestStore.Serialize(manifest);
        if (manifestText != previousText && previousText is not null && changes.Count == 0)
        {
            // Options or tracking changed without file changes: still record them, with a fresh timestamp.
            manifest.UpdatedAt = now;
            manifestText = ManifestStore.Serialize(manifest);
        }
        if (manifestText != previousText)
            changes.Add(new FileChange(Manifest.RelativePath, manifestText, previousText is null ? Transaction.Absent : ContentHash.Compute(previousText)));

        var prepared = new PreparedOperation
        {
            Plan = new InstallPlan { Root = root, Scope = request.Scope, Actions = actions, Manifest = manifest, PreviousManifestText = previousText, Changes = changes },
            Templates = templates,
            Profiles = resolved,
            Detection = detection,
            Previous = previous
        };
        prepared.Messages.AddRange(messages);
        return prepared;
    }

    public async Task<OperationResult> RunAsync(InstallRequest request, CancellationToken ct = default)
    {
        var prepared = await PrepareAsync(request, ct);
        if (request.DryRun || !prepared.Plan.HasChanges)
            return new OperationResult { Prepared = prepared };

        return new OperationResult { Prepared = prepared, Execution = Apply(prepared, request.IsUpdate) };
    }

    public static ExecutionResult Apply(PreparedOperation prepared, bool isUpdate) =>
        InstallExecutor.Apply(prepared.Plan, isUpdate ? "update" : "init", DateTimeOffset.UtcNow, prepared.Previous?.Templates.Version);

    private TemplateRequest BuildTemplateRequest(InstallRequest request, Manifest? previous, string root, List<string> messages, out bool pinned)
    {
        var packageId = request.PackageId ?? previous?.Templates.PackageId ?? TemplateSet.DefaultPackageId;
        var directory = request.TemplatesDirectory ??
                        (request.Version is null && request.PackageId is null && previous?.Templates.Directory is { } stored
                            ? Path.GetFullPath(Path.Combine(root, stored))
                            : null);

        string? version;
        if (request.Version is not null)
        {
            pinned = !string.Equals(request.Version, TemplateResolver.Latest, StringComparison.OrdinalIgnoreCase);
            version = request.Version;
        }
        else if (previous is { Templates.Pinned: true } && string.Equals(previous.Templates.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
        {
            pinned = true;
            version = previous.Templates.Version;
            if (request.IsUpdate)
                messages.Add($"Templates are pinned to {version}; use --version <x> or --version latest to change.");
        }
        else
        {
            pinned = false;
            var isDefault = string.Equals(packageId, TemplateSet.DefaultPackageId, StringComparison.OrdinalIgnoreCase);
            version = isDefault ? null : TemplateResolver.Latest;
            if (isDefault && previous is not null &&
                NuGetVersion.TryParse(previous.Templates.Version, out var installed) &&
                NuGetVersion.TryParse(Resolver.BundledVersion, out var bundled) && installed > bundled)
            {
                version = previous.Templates.Version;
                messages.Add($"Installed templates ({installed}) are newer than this tool's bundled templates ({bundled}); keeping {installed}.");
            }
        }

        return new TemplateRequest
        {
            PackageId = packageId,
            Version = directory is null ? version : null,
            TemplatesDirectory = directory,
            Sources = request.Sources,
            Offline = request.Offline,
            IncludePrerelease = request.Prerelease || (version is null && Resolver.BundledVersion.Contains('-')),
            ConfigRoot = root,
            AllowCacheWrite = !request.DryRun
        };
    }

    /// <summary>
    /// Documented overrides that move an assistant's configuration elsewhere. They are not followed (see
    /// docs/assistants.md), so warn that the global files may not be where that assistant looks.
    /// </summary>
    public static IEnumerable<string> GlobalLocationWarnings()
    {
        if (System.Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } claude)
            yield return $"CLAUDE_CONFIG_DIR is set ({claude}). Claude Code documents it for settings, history and plugins, not for CLAUDE.md, rules, skills or agents, so personal standards are still written to ~/.claude. Check they load with /context in Claude Code.";
        if (System.Environment.GetEnvironmentVariable("COPILOT_HOME") is { Length: > 0 } copilot)
            yield return $"COPILOT_HOME is set ({copilot}). Copilot CLI reads its configuration there, but VS Code reads personal instructions, skills and agents from ~/.copilot, where they are written.";
    }

    /// <summary>
    /// A project install must never write into an assistant's global configuration. That happens when the project
    /// root is the home directory (its .claude/.copilot folders are the global ones).
    /// </summary>
    private void EnsureNotGlobalConfiguration(string root)
    {
        var home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.HomeDirectory));
        if (string.Equals(Path.TrimEndingDirectorySeparator(root), home, SafePaths.PathComparison))
            throw new InvalidOperationException(
                $"'{root}' is your home directory. A project install here would modify the global Claude Code and Copilot configuration " +
                "(~/.claude, ~/.copilot). Use --global for personal standards, or run in a repository folder.");
    }

    /// <summary>
    /// Best effort: re-render the previously applied templates (offline only) so each conflict also gets a
    /// <c>.base</c> copy for three-way merging. Skipped silently when the previous version is not available locally.
    /// </summary>
    private async Task AddConflictBaselinesAsync(string root, Manifest previous, IReadOnlyList<PlanAction> actions, CancellationToken ct)
    {
        Dictionary<string, RenderedFile> rendered;
        try
        {
            var info = previous.Templates;
            var bundled = Resolver.BundledVersion;
            var request = new TemplateRequest
            {
                PackageId = info.PackageId,
                Version = info.Origin == "directory" ? null : string.Equals(info.Version, bundled, StringComparison.OrdinalIgnoreCase) && info.PackageId == TemplateSet.DefaultPackageId ? null : info.Version,
                TemplatesDirectory = info.Origin == "directory" && info.Directory is not null ? Path.GetFullPath(Path.Combine(root, info.Directory)) : null,
                Offline = true,
                AllowCacheWrite = false,
                ConfigRoot = root
            };
            var templates = await Resolver.ResolveAsync(request, ct);
            var profiles = ProfileResolver.Resolve(templates, previous.Profiles, previous.Exclude);
            var context = new RenderContext { Templates = templates, Profiles = profiles, Scope = previous.Scope, Assistants = previous.Assistants, IncludeMcp = previous.Mcp };
            rendered = ContentBuilder.RenderShared(context)
                .Concat(previous.Assistants.SelectMany(a => AdapterRegistry.Get(a).Render(context)))
                .ToDictionary(r => SafePaths.NormalizeRelative(r.Path), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return;
        }

        foreach (var action in actions.Where(a => a.Kind == PlanActionKind.Conflict && a.Entry is not null && a.Before is not null))
        {
            if (!rendered.TryGetValue(action.Path, out var baseFile))
                continue;
            var inner = baseFile.Content.Trim('\n');
            if (baseFile.Mode == ManagedMode.Whole && ContentHash.Compute(baseFile.Content) == action.Entry!.Hash)
                action.BaseContent = baseFile.Content;
            else if (baseFile.Mode == ManagedMode.Block && ContentHash.Compute(inner) == action.Entry!.Hash &&
                     ManagedBlock.Extract(action.Before!).State == ManagedBlock.BlockState.Present)
                action.BaseContent = ManagedBlock.Replace(action.Before!, inner);
        }
    }

    private static string StoreDirectory(string root, string directory)
    {
        var full = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(root, full);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? full : relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}
