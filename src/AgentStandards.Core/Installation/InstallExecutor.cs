using AgentStandards.Core.IO;

namespace AgentStandards.Core.Installation;

public sealed record ExecutionResult(string? BackupId, IReadOnlyList<string> Written, IReadOnlyList<string> Deleted, IReadOnlyList<string> ConflictCopies);

/// <summary>Applies an <see cref="InstallPlan"/>: backup first, then atomic writes, then the manifest.</summary>
public static class InstallExecutor
{
    public static ExecutionResult Apply(InstallPlan plan, string operation, DateTimeOffset now, string? previousTemplatesVersion)
    {
        var root = plan.Root;
        var changes = plan.Actions.Where(a => a.Writes || a.Deletes).ToList();
        string? backupId = null;

        if (changes.Count > 0 || plan.PreviousManifestText is not null)
        {
            backupId = BackupStore.NewId(operation, now);
            var originals = new Dictionary<string, string>(StringComparer.Ordinal);
            var record = new BackupRecord
            {
                Id = backupId,
                Operation = operation,
                CreatedAt = now,
                TemplatesVersionBefore = previousTemplatesVersion,
                TemplatesVersionAfter = plan.Manifest.Templates.Version,
                HadManifest = plan.PreviousManifestText is not null
            };
            foreach (var action in changes)
            {
                if (action.Before is not null)
                    originals[action.Path] = action.Before;
                record.Files.Add(new BackupFile
                {
                    Path = action.Path,
                    Existed = action.Before is not null,
                    WrittenHash = action.Deletes ? null : ContentHash.Compute(action.After!)
                });
            }
            BackupStore.Save(root, record, originals, plan.PreviousManifestText);
        }

        var written = new List<string>();
        var deleted = new List<string>();
        foreach (var action in changes)
        {
            if (action.Deletes)
            {
                AtomicFile.Delete(root, action.Path);
                deleted.Add(action.Path);
            }
            else
            {
                AtomicFile.WriteText(root, action.Path, action.After!);
                written.Add(action.Path);
            }
        }

        var conflicts = new List<string>();
        foreach (var action in plan.Actions.Where(a => a.Kind == PlanActionKind.Conflict && a.Incoming is not null))
        {
            var target = $"{InstallPlanner.ConflictsFolder}/{action.Path}";
            AtomicFile.WriteText(root, target, action.Incoming!);
            conflicts.Add(target);
        }

        ManifestStore.Save(root, plan.Manifest);
        BackupStore.Trim(root);
        return new ExecutionResult(backupId, written, deleted, conflicts);
    }
}
