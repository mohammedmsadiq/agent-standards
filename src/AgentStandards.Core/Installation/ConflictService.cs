using AgentStandards.Core.IO;
using AgentStandards.Core.Rendering;

namespace AgentStandards.Core.Installation;

public enum ResolveState
{
    /// <summary>
    /// The local file changed after the conflict was reported. That shows the developer edited it; it does not show
    /// that the incoming changes were incorporated, which the toolkit cannot check.
    /// </summary>
    Edited,
    /// <summary>The local file is exactly as it was when the conflict was reported: nothing can have been merged.</summary>
    Unchanged,
    /// <summary>The conflict was recorded by an older version, so it cannot be told whether the file was edited since.</summary>
    Unknown,
    /// <summary>The managed file (or its managed block) is missing.</summary>
    Missing
}

public sealed record ResolveItem(string Path, ResolveState State);

public sealed record ResolveResult(IReadOnlyList<string> Resolved, IReadOnlyList<string> RemainingConflicts, bool Applied, string? BackupId)
{
    /// <summary>Conflict copies kept because the developer edited them.</summary>
    public IReadOnlyList<string> KeptCopies { get; init; } = [];
    /// <summary>What each selected file looks like compared with when its conflict was reported.</summary>
    public IReadOnlyList<ResolveItem> Items { get; init; } = [];
    /// <summary>Files not edited since their conflict was reported; resolving them needs --accept-local.</summary>
    public IReadOnlyList<string> Blocked { get; init; } = [];
}

/// <summary>
/// Marks pending conflicts as resolved: the current local content is accepted as the merge result, the incoming
/// template version becomes the new baseline, and the conflict copies are removed. A file left unchanged since its
/// conflict was reported is only accepted with <c>acceptLocal</c>, because that discards the incoming changes.
/// </summary>
public static class ConflictService
{
    public static IReadOnlyList<ManifestFile> Pending(Manifest manifest) => manifest.Files.Where(f => f.PendingHash is not null).ToList();

    public static ResolveResult Resolve(string root, IReadOnlyList<string> paths, bool all, bool dryRun, bool force = false, bool acceptLocal = false)
    {
        if (dryRun)
            return ResolveCore(null, root, paths, all, force, acceptLocal);
        using var held = OperationLock.Acquire(root, "resolve");
        return ResolveCore(held, root, paths, all, force, acceptLocal);
    }

    private static ResolveResult ResolveCore(OperationLock? held, string root, IReadOnlyList<string> paths, bool all, bool force, bool acceptLocal)
    {
        if (OperationJournal.Read(root) is { } journal)
            throw new InterruptedOperationException($"An interrupted '{journal.Operation}' operation was found. Run 'agent-standards rollback' first.");
        var manifestText = AtomicFile.ReadTextOrNull(root, Manifest.RelativePath);
        var manifest = ManifestStore.Load(root) ?? throw new ManifestException($"No installation found in '{root}'. Run 'agent-standards init' first.");
        var pending = Pending(manifest).ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);

        if (!all && paths.Count == 0)
            throw new ArgumentException("Specify the file(s) to mark as resolved, or --all.");
        var selected = new List<ManifestFile>();
        if (all)
        {
            selected.AddRange(pending.Values);
        }
        else
        {
            foreach (var raw in paths)
            {
                var path = SafePaths.NormalizeRelative(raw);
                if (!pending.TryGetValue(path, out var entry))
                    throw new ArgumentException(manifest.Files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))
                        ? $"'{path}' has no pending conflict."
                        : $"'{path}' is not managed by agent-standards. For an unmanaged file that blocks installation, rename or remove it, or re-run with --force.");
                selected.Add(entry);
            }
        }

        var resolved = selected.Select(f => f.Path).ToList();
        var remaining = pending.Keys.Except(resolved, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
        var items = selected.Select(f => new ResolveItem(f.Path, Classify(root, f))).ToList();
        var blocked = acceptLocal ? [] : items.Where(i => i.State is ResolveState.Unchanged or ResolveState.Missing).Select(i => i.Path).ToList();
        if (held is null || selected.Count == 0)
            return new ResolveResult(resolved, remaining, false, null) { Items = items, Blocked = blocked };
        if (blocked.Count > 0)
            throw new InvalidOperationException(
                $"Not resolved: {string.Join(", ", blocked)} {(blocked.Count == 1 ? "has" : "have")} not changed since the conflict was reported, so nothing was merged. " +
                $"Merge the changes you want from {InstallPlanner.ConflictsFolder}/<path>.incoming first, or pass --accept-local to keep your version and discard the incoming changes.");

        var changes = new List<FileChange>();
        var kept = new List<string>();
        var copies = new Dictionary<string, string>(manifest.ConflictCopies ?? [], StringComparer.Ordinal);
        foreach (var entry in selected)
        {
            entry.Hash = entry.PendingHash!;
            entry.PendingHash = null;
            entry.LocalHashAtConflict = null;
            var (incoming, basePath) = InstallPlanner.ConflictCopyPaths(entry.Path);
            foreach (var copy in new[] { incoming, basePath })
            {
                if (AtomicFile.ReadTextOrNull(root, copy) is not { } text)
                {
                    copies.Remove(copy);
                    continue;
                }
                if (force || InstallPlanner.IsUnmodifiedCopy(text, copy, manifest.ConflictCopies))
                {
                    changes.Add(new FileChange(copy, null, ContentHash.Compute(text)));
                    copies.Remove(copy);
                }
                else
                {
                    kept.Add(copy);
                }
            }
        }
        manifest.ConflictCopies = copies.Count > 0 ? copies : null;
        manifest.UpdatedAt = DateTimeOffset.UtcNow;
        changes.Add(new FileChange(Manifest.RelativePath, ManifestStore.Serialize(manifest), ContentHash.Compute(manifestText!)));

        var id = Transaction.Run(held, root, "resolve", changes, new BackupRecord
        {
            TemplatesVersionBefore = manifest.Templates.Version,
            TemplatesVersionAfter = manifest.Templates.Version
        }, DateTimeOffset.UtcNow);
        BackupStore.Trim(root);
        return new ResolveResult(resolved, remaining, true, id) { KeptCopies = kept, Items = items };
    }

    private static ResolveState Classify(string root, ManifestFile entry)
    {
        var text = AtomicFile.ReadTextOrNull(root, entry.Path);
        if (text is null)
            return ResolveState.Missing;
        string? hash = entry.Mode switch
        {
            ManagedMode.Block => ManagedBlock.Extract(text) is (ManagedBlock.BlockState.Present, { } inner) ? ContentHash.Compute(inner) : null,
            _ => ContentHash.Compute(text)
        };
        if (hash is null)
            return ResolveState.Missing;
        if (entry.LocalHashAtConflict is null)
            return ResolveState.Unknown;
        return hash == entry.LocalHashAtConflict ? ResolveState.Unchanged : ResolveState.Edited;
    }
}
