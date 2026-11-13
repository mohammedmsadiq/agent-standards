using System.Text.Json;
using AgentStandards.Core.IO;

namespace AgentStandards.Core.Installation;

/// <param name="Path">Root-relative path.</param>
/// <param name="After">Content to write, or null to delete.</param>
/// <param name="ExpectedBeforeHash">When set, the operation aborts if the file changed since it was planned
/// (<see cref="Transaction.Absent"/> means "expected not to exist").</param>
public sealed record FileChange(string Path, string? After, string? ExpectedBeforeHash = null);

public sealed class OperationJournal
{
    public const string RelativePath = ".agent-standards/operation.json";

    public string BackupId { get; set; } = "";
    public string Operation { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    /// <summary>
    /// The interrupted operation this one was recovering, if any. If this operation is interrupted too and later
    /// rolled back, that journal is put back so the original interruption stays detectable.
    /// </summary>
    public OperationJournal? Previous { get; set; }

    public static OperationJournal? Read(string root)
    {
        var text = AtomicFile.ReadTextOrNull(root, RelativePath);
        if (text is null)
            return null;
        try
        {
            return JsonSerializer.Deserialize<OperationJournal>(text, ManifestStore.Options) ?? new OperationJournal();
        }
        catch (JsonException)
        {
            return new OperationJournal(); // present but unreadable: still an interrupted operation
        }
    }

    internal static void Write(string root, OperationJournal journal) =>
        AtomicFile.WriteText(root, RelativePath, JsonSerializer.Serialize(journal, ManifestStore.Options) + "\n");
}

public sealed class OperationFailedException(string message, bool restored, Exception? inner) : Exception(message, inner)
{
    /// <summary>True when the installation is in its pre-operation state (nothing was modified or everything was reverted).</summary>
    public bool Restored { get; } = restored;
}

public sealed class InterruptedOperationException(string message) : Exception(message);

public sealed class OperationInProgressException(string message) : Exception(message);

/// <summary>
/// Applies a set of file changes as one recoverable unit while holding the installation's <see cref="OperationLock"/>:
/// verify → complete backup → journal → changes → journal removed. Every stage either leaves the installation
/// untouched, reverts it automatically, or leaves the journal and backup so <c>rollback</c> can recover.
/// </summary>
public static class Transaction
{
    public const string Absent = "absent";

    /// <summary>Transaction stages at which tests can inject failures.</summary>
    internal enum Stage { BackupFile, BackupRecord, Journal, Change, Restore, JournalCleanup }

    /// <summary>Test seam: invoked with (root, stage, path) before each step; throwing simulates an I/O failure.</summary>
    internal static Action<string, Stage, string?>? Fault { get; set; }

    /// <param name="recoveringBackupId">Backup of the interrupted operation this run recovers; any other journal blocks the run.</param>
    public static string Run(OperationLock held, string root, string operation, IReadOnlyList<FileChange> changes, BackupRecord template,
        DateTimeOffset now, string? recoveringBackupId = null)
    {
        held.EnsureFor(root);
        var duplicate = changes.GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Internal error: '{duplicate.Key}' is changed twice in one operation.");

        var existingJournal = OperationJournal.Read(root);
        if (existingJournal is not null && existingJournal.BackupId != recoveringBackupId)
            throw new InterruptedOperationException(
                $"An interrupted '{existingJournal.Operation}' operation (backup {existingJournal.BackupId}) was found. Run 'agent-standards rollback' to undo it, or 'agent-standards complete' if all its changes were written.");

        // 1. Verify nothing changed since planning, and capture originals.
        var originals = new Dictionary<string, string>(StringComparer.Ordinal);
        var record = new BackupRecord
        {
            Id = BackupStore.NewId(root, operation, now),
            Operation = operation,
            CreatedAt = now,
            TemplatesVersionBefore = template.TemplatesVersionBefore,
            TemplatesVersionAfter = template.TemplatesVersionAfter,
            Internal = template.Internal
        };
        foreach (var change in changes)
        {
            var current = AtomicFile.ReadTextOrNull(root, change.Path);
            if (change.ExpectedBeforeHash is not null &&
                change.ExpectedBeforeHash != (current is null ? Absent : ContentHash.Compute(current)))
                throw new InvalidOperationException($"'{change.Path}' changed after the plan was made; nothing was modified. Re-run the command.");
            if (current is not null)
                originals[change.Path] = current;
            record.Files.Add(new BackupFile
            {
                Path = change.Path,
                Existed = current is not null,
                WrittenHash = change.After is null ? null : ContentHash.Compute(change.After)
            });
        }
        record.HadManifest = originals.ContainsKey(Manifest.RelativePath);
        record.CreatedDirectories = MissingDirectories(root, changes.Where(c => c.After is not null).Select(c => c.Path));

        // 2. Backup: originals first, backup.json last. A failure removes the partial backup; nothing was modified.
        try
        {
            BackupStore.Save(root, record, originals,
                beforeFile: path => Fault?.Invoke(root, Stage.BackupFile, path),
                beforeRecord: () => Fault?.Invoke(root, Stage.BackupRecord, null));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TryDeleteBackup(root, record.Id);
            throw new OperationFailedException($"{operation} could not create its backup ({ex.Message}); nothing was modified.", restored: true, ex);
        }

        // 3. Journal. Until it exists nothing has been modified; a failure here removes the unused backup.
        try
        {
            Fault?.Invoke(root, Stage.Journal, OperationJournal.RelativePath);
            OperationJournal.Write(root, new OperationJournal { BackupId = record.Id, Operation = operation, StartedAt = now, Previous = existingJournal });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TryDeleteBackup(root, record.Id);
            throw new OperationFailedException($"{operation} could not write its journal ({ex.Message}); nothing was modified.", restored: true, ex);
        }

        // 4. Apply; on failure put everything back.
        try
        {
            foreach (var change in changes)
            {
                Fault?.Invoke(root, Stage.Change, change.Path);
                if (change.After is null)
                    AtomicFile.Delete(root, change.Path);
                else
                    AtomicFile.WriteText(root, change.Path, change.After);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            try
            {
                Restore(root, record);
                if (existingJournal is null)
                    AtomicFile.Delete(root, OperationJournal.RelativePath);
                else
                    OperationJournal.Write(root, existingJournal);
            }
            catch (Exception restoreEx) when (restoreEx is not OutOfMemoryException)
            {
                // The journal and backup stay in place, so the installation is detectably interrupted and recoverable.
                throw new OperationFailedException(
                    $"{operation} failed ({ex.Message}) and the automatic restore also failed ({restoreEx.Message}). " +
                    $"The backup {record.Id} is intact: run 'agent-standards rollback' to restore it.", restored: false, ex);
            }
            TryDeleteBackup(root, record.Id);
            throw new OperationFailedException($"{operation} failed while writing files ({ex.Message}). All changes were reverted; nothing was modified.", restored: true, ex);
        }

        // 5. Commit. If the journal cannot be removed the run is not reported as successful.
        try
        {
            Fault?.Invoke(root, Stage.JournalCleanup, OperationJournal.RelativePath);
            AtomicFile.Delete(root, OperationJournal.RelativePath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new OperationFailedException(
                $"{operation} wrote all its changes, but could not remove {OperationJournal.RelativePath} ({ex.Message}), so the installation is marked as interrupted. " +
                "Run 'agent-standards complete' to verify every change and keep them, or 'agent-standards rollback' to undo the operation.", restored: false, ex);
        }
        return record.Id;
    }

    /// <summary>
    /// Puts every file in the record back to its pre-operation state, skipping files that already have it (so a
    /// failure early in an operation never needs to touch files it did not reach), then removes directories the
    /// operation created if they are empty.
    /// </summary>
    internal static void Restore(string root, BackupRecord record)
    {
        foreach (var file in record.Files)
        {
            var current = AtomicFile.ReadTextOrNull(root, file.Path);
            if (file.Existed)
            {
                var original = BackupStore.ReadFile(root, record.Id, file.Path)
                               ?? throw new InvalidOperationException($"Backup copy of '{file.Path}' is missing.");
                if (current == original)
                    continue;
                Fault?.Invoke(root, Stage.Restore, file.Path);
                AtomicFile.WriteText(root, file.Path, original);
            }
            else if (current is not null)
            {
                Fault?.Invoke(root, Stage.Restore, file.Path);
                AtomicFile.Delete(root, file.Path);
            }
        }
        RemoveEmptyDirectories(root, record.CreatedDirectories);
    }

    /// <summary>Directories (deepest first) that do not exist yet and would be created to write the given files.</summary>
    private static List<string> MissingDirectories(string root, IEnumerable<string> files)
    {
        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var parts = file.Split('/');
            for (var i = parts.Length - 1; i >= 1; i--)
            {
                var dir = string.Join('/', parts[..i]);
                if (Directory.Exists(SafePaths.Resolve(root, dir)))
                    break;
                missing.Add(dir);
            }
        }
        return missing.OrderByDescending(d => d.Count(c => c == '/')).ThenBy(d => d, StringComparer.Ordinal).ToList();
    }

    /// <summary>Removes the listed directories, deepest first, only when they are empty.</summary>
    internal static void RemoveEmptyDirectories(string root, IEnumerable<string>? directories)
    {
        foreach (var dir in (directories ?? []).OrderByDescending(d => d.Count(c => c == '/')))
        {
            try
            {
                var full = SafePaths.ResolveSafe(root, dir);
                if (Directory.Exists(full) && !Directory.EnumerateFileSystemEntries(full).Any())
                    Directory.Delete(full);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnsafePathException)
            {
                // An empty directory left behind is harmless.
            }
        }
    }

    /// <summary>Best-effort removal of a backup that is no longer needed; leftovers are harmless and trimmed later.</summary>
    internal static bool TryDeleteBackup(string root, string id)
    {
        try
        {
            BackupStore.Delete(root, id);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnsafePathException)
        {
            return false;
        }
    }
}
