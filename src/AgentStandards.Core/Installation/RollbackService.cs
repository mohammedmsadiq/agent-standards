using System.Text.Json;
using AgentStandards.Core.IO;

namespace AgentStandards.Core.Installation;

public enum RollbackActionKind
{
    /// <summary>Restore the content the file had before the operation.</summary>
    Restore,
    /// <summary>Delete a file the operation created.</summary>
    Delete,
    /// <summary>The file already has its pre-operation content (for example, an interrupted operation never reached it).</summary>
    AlreadyRestored,
    /// <summary>The file changed after the operation; it is kept and stays tracked against its current baseline.</summary>
    SkipModified,
    /// <summary>The manifest is restored to match the resulting files.</summary>
    Manifest
}

public sealed record RollbackAction(RollbackActionKind Kind, string Path, string Reason, bool Forced = false);

public sealed record RollbackResult(BackupRecord Backup, IReadOnlyList<RollbackAction> Actions, bool Applied, bool Partial, bool RecoversInterruptedOperation)
{
    /// <summary>Non-fatal follow-ups, for example a consumed backup that could not be removed.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Reverts the most recent (or a named) operation from its backup. The rollback itself runs as a
/// <see cref="Transaction"/>, so a failure part-way through is reverted automatically.
/// </summary>
public static class RollbackService
{
    public static RollbackResult Rollback(string root, string? backupId, bool dryRun, bool force)
    {
        if (dryRun)
            return RollbackCore(null, root, backupId, force);
        using var held = OperationLock.Acquire(root, "rollback");
        return RollbackCore(held, root, backupId, force);
    }

    private static RollbackResult RollbackCore(OperationLock? held, string root, string? backupId, bool force)
    {
        var journal = OperationJournal.Read(root);
        BackupRecord backup;
        if (backupId is not null)
        {
            backup = BackupStore.Get(root, backupId)
                     ?? throw new InvalidOperationException($"Backup '{backupId}' was not found. Use 'agent-standards rollback --list'.");
        }
        else if (journal is not null)
        {
            backup = BackupStore.Get(root, journal.BackupId)
                     ?? throw new InvalidOperationException(
                         $"An interrupted operation was detected, but its backup '{journal.BackupId}' is missing or unreadable. " +
                         $"Check the managed files (for example with 'git status'), restore them from version control if needed, then delete {OperationJournal.RelativePath}.");
        }
        else
        {
            backup = BackupStore.List(root).FirstOrDefault() ?? throw new InvalidOperationException("No backups are available to roll back to.");
        }

        var recovering = journal is not null && journal.BackupId == backup.Id;
        var actions = new List<RollbackAction>();
        var changes = new List<FileChange>();
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in backup.Files.Where(f => !string.Equals(f.Path, Manifest.RelativePath, StringComparison.OrdinalIgnoreCase)))
        {
            var path = SafePaths.NormalizeRelative(file.Path);
            var current = AtomicFile.ReadTextOrNull(root, path);
            var original = file.Existed
                ? BackupStore.ReadFile(root, backup.Id, path) ?? throw new InvalidOperationException($"Backup copy of '{path}' is missing from {backup.Id}.")
                : null;

            if (SameContent(current, original))
            {
                actions.Add(new(RollbackActionKind.AlreadyRestored, path, "already has its pre-operation content"));
                continue;
            }

            var asWritten = file.WrittenHash is null ? current is null : current is not null && ContentHash.Compute(current) == file.WrittenHash;
            if (!asWritten && !force)
            {
                skipped.Add(path);
                actions.Add(new(RollbackActionKind.SkipModified, path, "changed after the operation; kept as is (use --force to revert it, a safety backup is taken first)"));
                continue;
            }

            var kind = original is null ? RollbackActionKind.Delete : RollbackActionKind.Restore;
            var reason = original is null ? "created by the operation; removing" : "restoring the previous content";
            actions.Add(new(kind, path, asWritten ? reason : reason + " (changed after the operation; reverted because --force was given)", Forced: !asWritten));
            changes.Add(new FileChange(path, original, current is null ? Transaction.Absent : ContentHash.Compute(current)));
        }

        var currentManifest = AtomicFile.ReadTextOrNull(root, Manifest.RelativePath);
        var targetManifest = backup.HadManifest
            ? BackupStore.ReadManifest(root, backup.Id) ?? throw new InvalidOperationException($"Backup manifest is missing from {backup.Id}.")
            : null;
        if (skipped.Count > 0)
            targetManifest = MergeManifest(targetManifest, currentManifest, skipped);
        if (targetManifest != currentManifest)
        {
            actions.Add(new(RollbackActionKind.Manifest, Manifest.RelativePath, targetManifest is null
                ? "removing the manifest (nothing was installed before the operation)"
                : skipped.Count > 0
                    ? $"restoring the manifest (templates {backup.TemplatesVersionBefore}); kept files stay tracked against their current baseline"
                    : $"restoring the manifest (templates {backup.TemplatesVersionBefore})"));
            changes.Add(new FileChange(Manifest.RelativePath, targetManifest, currentManifest is null ? Transaction.Absent : ContentHash.Compute(currentManifest)));
        }

        var result = new RollbackResult(backup, actions, Applied: held is not null, Partial: skipped.Count > 0, recovering);
        if (held is null)
            return result;

        var warnings = new List<string>();
        if (changes.Count > 0)
        {
            var safety = Transaction.Run(held, root, "rollback", changes, new BackupRecord
            {
                Internal = true,
                TemplatesVersionBefore = backup.TemplatesVersionAfter,
                TemplatesVersionAfter = backup.TemplatesVersionBefore
            }, DateTimeOffset.UtcNow, recoveringBackupId: recovering ? backup.Id : null);
            if (!Transaction.TryDeleteBackup(root, safety))
                warnings.Add($"Could not remove the internal safety backup {safety}; it is harmless and will be trimmed later.");
        }
        else if (recovering)
        {
            AtomicFile.Delete(root, OperationJournal.RelativePath);
        }

        Transaction.RemoveEmptyDirectories(root, backup.CreatedDirectories);

        // Recovering a rollback that was itself interrupted puts back the journal of the operation it was recovering,
        // so that original interruption stays visible and can be recovered next.
        if (recovering && journal!.Previous is { } previous)
        {
            OperationJournal.Write(root, previous);
            warnings.Add($"The interrupted '{previous.Operation}' operation it was recovering is still pending; run 'agent-standards rollback' again.");
        }

        if (!Transaction.TryDeleteBackup(root, backup.Id))
            warnings.Add($"Could not remove the consumed backup {backup.Id}; rolling back again is safe and will only remove it.");
        return result with { Warnings = warnings };
    }

    private static bool SameContent(string? a, string? b) =>
        a is null || b is null ? a is null && b is null : ContentHash.Compute(a) == ContentHash.Compute(b);

    /// <summary>
    /// Previous manifest, except that every file kept by a partial rollback keeps the entry describing its current
    /// state, so tracking matches the files on disk.
    /// </summary>
    internal static string? MergeManifest(string? priorText, string? currentText, IReadOnlySet<string> keptPaths)
    {
        var prior = TryParse(priorText);
        var current = TryParse(currentText);
        var currentEntries = (current?.Files ?? []).Where(f => f is not null).ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);

        if (prior is null)
        {
            if (current is null)
                return null;
            current.Files = current.Files.Where(f => f is not null && keptPaths.Contains(f.Path)).ToList();
            current.ConflictCopies = current.ConflictCopies?.Where(c => keptPaths.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value) is { Count: > 0 } kept ? kept : null;
            return current.Files.Count == 0 && current.ConflictCopies is null ? null : ManifestStore.Serialize(current);
        }

        prior.Files = prior.Files.Where(f => f is not null && !keptPaths.Contains(f.Path)).ToList();
        foreach (var path in keptPaths)
            if (currentEntries.TryGetValue(path, out var entry))
                prior.Files.Add(entry);
        prior.Files = prior.Files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();

        // Kept conflict copies keep the hash describing their current state.
        var copies = new Dictionary<string, string>(prior.ConflictCopies ?? [], StringComparer.Ordinal);
        foreach (var path in keptPaths)
        {
            copies.Remove(path);
            if (current?.ConflictCopies?.TryGetValue(path, out var hash) == true)
                copies[path] = hash;
        }
        prior.ConflictCopies = copies.Count > 0 ? copies : null;
        return ManifestStore.Serialize(prior);
    }

    private static Manifest? TryParse(string? text)
    {
        if (text is null)
            return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<Manifest>(text, ManifestStore.Options);
            return manifest?.Files is null ? null : manifest;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
