using AgentStandards.Core.Diagnostics;
using AgentStandards.Core.IO;

namespace AgentStandards.Core.Installation;

public sealed record CompletionCheck(string Path, bool Matches, string Detail);

public sealed record CompletionResult(OperationJournal Journal, IReadOnlyList<CompletionCheck> Checks, bool Verified, bool Applied)
{
    /// <summary>
    /// True when the interrupted operation's backup is missing or unreadable. Its changes then cannot be verified;
    /// <see cref="Checks"/> instead compares the managed files with the manifest as a diagnostic.
    /// </summary>
    public bool BackupMissing { get; init; }
}

/// <summary>
/// Finishes an interrupted operation whose changes were all written (for example when only removing the journal
/// failed). Every file the operation changed must have exactly the content it wrote, and the manifest must load;
/// only then is the journal cleared. Otherwise nothing changes and <c>rollback</c> is the way back.
/// <para>
/// When the operation's backup is gone, nothing can be verified or rolled back. The result then carries a diagnostic
/// of the managed files against the manifest, and the interrupted state is only cleared when the caller explicitly
/// asks for it (<c>unverified</c>), after the developer has reviewed or restored the files.
/// </para>
/// </summary>
public static class CompletionService
{
    public static CompletionResult Complete(string root, bool dryRun, bool unverified = false)
    {
        if (dryRun)
            return CompleteCore(null, root, unverified);
        using var held = OperationLock.Acquire(root, "complete");
        return CompleteCore(held, root, unverified);
    }

    private static CompletionResult CompleteCore(OperationLock? held, string root, bool unverified)
    {
        var journal = OperationJournal.Read(root)
                      ?? throw new InvalidOperationException("There is no interrupted operation to complete.");
        var backup = BackupStore.Get(root, journal.BackupId);
        if (backup is null)
        {
            var diagnostic = DiagnoseAgainstManifest(root);
            if (held is null || !unverified)
                return new CompletionResult(journal, diagnostic, Verified: false, Applied: false) { BackupMissing = true };
            ClearJournal(root, journal);
            return new CompletionResult(journal, diagnostic, Verified: false, Applied: true) { BackupMissing = true };
        }

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

        ClearJournal(root, journal);
        BackupStore.Trim(root);
        return new CompletionResult(journal, checks, verified, Applied: true);
    }

    private static void ClearJournal(string root, OperationJournal journal)
    {
        if (journal.Previous is { } previous)
            OperationJournal.Write(root, previous); // the operation it was recovering is still pending
        else
            AtomicFile.Delete(root, OperationJournal.RelativePath);
    }

    /// <summary>
    /// Without the backup the only reference left is the manifest: report every managed file whose content does not
    /// match it. A difference is either a local edit or a change the interrupted operation wrote before it stopped
    /// (the manifest is written last), so the developer has to judge each one.
    /// </summary>
    private static List<CompletionCheck> DiagnoseAgainstManifest(string root)
    {
        var checks = new List<CompletionCheck>();
        Manifest? manifest;
        try
        {
            manifest = ManifestStore.Load(root);
        }
        catch (ManifestException ex)
        {
            checks.Add(new CompletionCheck(Manifest.RelativePath, false, ex.Message));
            return checks;
        }

        if (manifest is null)
        {
            checks.Add(new CompletionCheck(Manifest.RelativePath, false, "manifest is missing"));
            return checks;
        }

        foreach (var file in manifest.Files)
        {
            FileState state;
            try
            {
                state = StatusService.GetState(root, file, ignorePending: true);
            }
            catch (UnsafePathException ex)
            {
                checks.Add(new CompletionCheck(file.Path, false, ex.Message));
                continue;
            }

            checks.Add(state switch
            {
                FileState.Unmodified => new CompletionCheck(file.Path, true, "matches the manifest"),
                FileState.Modified => new CompletionCheck(file.Path, false, "differs from the manifest (your own edit, or a change the interrupted operation wrote)"),
                FileState.Missing => new CompletionCheck(file.Path, false, "missing"),
                FileState.BlockMissing => new CompletionCheck(file.Path, false, "managed block is missing"),
                _ => new CompletionCheck(file.Path, false, "managed block markers or JSON are malformed")
            });
        }
        return checks;
    }
}
