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
}

public sealed class OperationFailedException(string message, bool restored, Exception inner) : Exception(message, inner)
{
    /// <summary>True when every change was reverted automatically.</summary>
    public bool Restored { get; } = restored;
}

public sealed class InterruptedOperationException(string message) : Exception(message);

/// <summary>
/// Applies a set of file changes as one recoverable unit: complete backup → journal → changes → journal removed.
/// If a change fails, every change is reverted from the backup. If the process dies, the journal remains and
/// <c>agent-standards rollback</c> restores the backup.
/// </summary>
public static class Transaction
{
    public const string Absent = "absent";

    /// <summary>Test seam: invoked with (root, path) before each file change; throwing simulates an I/O failure.</summary>
    internal static Action<string, string>? BeforeChange { get; set; }

    public static string Run(string root, string operation, IReadOnlyList<FileChange> changes, BackupRecord template, DateTimeOffset now)
    {
        var duplicate = changes.GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Internal error: '{duplicate.Key}' is changed twice in one operation.");

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

        // 2. Backup (backup.json last), then the journal. Until the journal exists, nothing has been modified.
        BackupStore.Save(root, record, originals);
        var previousJournal = AtomicFile.ReadTextOrNull(root, OperationJournal.RelativePath);
        AtomicFile.WriteText(root, OperationJournal.RelativePath,
            JsonSerializer.Serialize(new OperationJournal { BackupId = record.Id, Operation = operation, StartedAt = now }, ManifestStore.Options) + "\n");

        // 3. Apply.
        try
        {
            foreach (var change in changes)
            {
                BeforeChange?.Invoke(root, change.Path);
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
                if (previousJournal is null)
                    AtomicFile.Delete(root, OperationJournal.RelativePath);
                else
                    AtomicFile.WriteText(root, OperationJournal.RelativePath, previousJournal);
                BackupStore.Delete(root, record.Id);
            }
            catch (Exception restoreEx) when (restoreEx is not OutOfMemoryException)
            {
                throw new OperationFailedException(
                    $"{operation} failed ({ex.Message}) and the automatic restore also failed ({restoreEx.Message}). " +
                    $"The backup {record.Id} is intact: run 'agent-standards rollback' to restore it.", restored: false, ex);
            }
            throw new OperationFailedException($"{operation} failed while writing files ({ex.Message}). All changes were reverted; nothing was modified.", restored: true, ex);
        }

        // 4. Commit. A journal left by an interrupted operation that this run recovered is resolved too.
        AtomicFile.Delete(root, OperationJournal.RelativePath);
        return record.Id;
    }

    /// <summary>Unconditionally puts every file in the record back to its pre-operation state.</summary>
    internal static void Restore(string root, BackupRecord record)
    {
        foreach (var file in record.Files)
        {
            if (file.Existed)
                AtomicFile.WriteText(root, file.Path, BackupStore.ReadFile(root, record.Id, file.Path)
                                                      ?? throw new InvalidOperationException($"Backup copy of '{file.Path}' is missing."));
            else
                AtomicFile.Delete(root, file.Path);
        }
    }
}
