using AgentStandards.Core.IO;

namespace AgentStandards.Core.Installation;

public sealed record CompletionCheck(string Path, bool Matches, string Detail);

public sealed record CompletionResult(OperationJournal Journal, IReadOnlyList<CompletionCheck> Checks, bool Verified, bool Applied);

/// <summary>
/// Finishes an interrupted operation whose changes were all written (for example when only removing the journal
/// failed). Every file the operation changed must have exactly the content it wrote, and the manifest must load;
/// only then is the journal cleared. Otherwise nothing changes and <c>rollback</c> is the way back.
/// </summary>
public static class CompletionService
{
    public static CompletionResult Complete(string root, bool dryRun)
    {
        if (dryRun)
            return CompleteCore(null, root);
        using var held = OperationLock.Acquire(root, "complete");
        return CompleteCore(held, root);
    }

    private static CompletionResult CompleteCore(OperationLock? held, string root)
    {
        var journal = OperationJournal.Read(root)
                      ?? throw new InvalidOperationException("There is no interrupted operation to complete.");
        var backup = BackupStore.Get(root, journal.BackupId)
                     ?? throw new InvalidOperationException(
                         $"The interrupted operation's backup '{journal.BackupId}' is missing or unreadable, so its changes cannot be verified. " +
                         $"Check the managed files (for example with 'git status'), restore them from version control if needed, then delete {OperationJournal.RelativePath}.");

        var checks = new List<CompletionCheck>();
        foreach (var file in backup.Files)
        {
            var path = SafePaths.NormalizeRelative(file.Path);
            var current = AtomicFile.ReadTextOrNull(root, path);
            checks.Add(file.WrittenHash is null
                ? new CompletionCheck(path, current is null, current is null ? "deleted as planned" : "should have been deleted but still exists")
                : current is null
                    ? new CompletionCheck(path, false, "missing; the operation did not write it")
                    : ContentHash.Compute(current) == file.WrittenHash
                        ? new CompletionCheck(path, true, "written as planned")
                        : new CompletionCheck(path, false, "differs from what the operation wrote"));
        }

        try
        {
            if (ManifestStore.Load(root) is null)
                checks.Add(new CompletionCheck(Manifest.RelativePath, false, "manifest is missing"));
        }
        catch (ManifestException ex)
        {
            checks.Add(new CompletionCheck(Manifest.RelativePath, false, ex.Message));
        }

        var verified = checks.All(c => c.Matches);
        if (held is null || !verified)
            return new CompletionResult(journal, checks, verified, Applied: false);

        if (journal.Previous is { } previous)
            OperationJournal.Write(root, previous); // the operation it was recovering is still pending
        else
            AtomicFile.Delete(root, OperationJournal.RelativePath);
        BackupStore.Trim(root);
        return new CompletionResult(journal, checks, verified, Applied: true);
    }
}
