using AgentStandards.Core.IO;

namespace AgentStandards.Core.Installation;

public enum RollbackActionKind
{
    Restore,
    Delete,
    SkipModified,
    RestoreManifest,
    DeleteManifest
}

public sealed record RollbackAction(RollbackActionKind Kind, string Path, string Reason);

public sealed record RollbackResult(BackupRecord Backup, IReadOnlyList<RollbackAction> Actions, bool Applied);

/// <summary>Reverts the most recent (or a named) install/update using its backup snapshot.</summary>
public static class RollbackService
{
    public static RollbackResult Rollback(string root, string? backupId, bool dryRun, bool force)
    {
        var backups = BackupStore.List(root);
        if (backups.Count == 0)
            throw new InvalidOperationException("No backups are available to roll back to.");
        var backup = backupId is null
            ? backups[0]
            : backups.FirstOrDefault(b => b.Id == backupId) ?? throw new InvalidOperationException($"Backup '{backupId}' was not found. Use 'agent-standards rollback --list'.");

        var actions = new List<RollbackAction>();
        foreach (var file in backup.Files)
        {
            var path = SafePaths.NormalizeRelative(file.Path);
            var current = AtomicFile.ReadTextOrNull(root, path);
            var changedSince = file.WrittenHash is null
                ? current is not null
                : current is null || ContentHash.Compute(current) != file.WrittenHash;
            if (changedSince && !force)
            {
                actions.Add(new RollbackAction(RollbackActionKind.SkipModified, path, "changed since the operation; left as is (use --force to revert anyway)"));
                continue;
            }

            actions.Add(file.Existed
                ? new RollbackAction(RollbackActionKind.Restore, path, "restore previous content")
                : new RollbackAction(RollbackActionKind.Delete, path, "created by the operation; removing"));
        }

        actions.Add(backup.HadManifest
            ? new RollbackAction(RollbackActionKind.RestoreManifest, Manifest.RelativePath, $"restore manifest (templates {backup.TemplatesVersionBefore})")
            : new RollbackAction(RollbackActionKind.DeleteManifest, Manifest.RelativePath, "no manifest existed before the operation"));

        if (dryRun)
            return new RollbackResult(backup, actions, false);

        foreach (var action in actions)
        {
            switch (action.Kind)
            {
                case RollbackActionKind.Restore:
                    AtomicFile.WriteText(root, action.Path, BackupStore.ReadFile(root, backup.Id, action.Path)
                                                            ?? throw new InvalidOperationException($"Backup copy of '{action.Path}' is missing."));
                    break;
                case RollbackActionKind.Delete:
                    AtomicFile.Delete(root, action.Path);
                    break;
                case RollbackActionKind.RestoreManifest:
                    AtomicFile.WriteText(root, Manifest.RelativePath, BackupStore.ReadManifest(root, backup.Id)
                                                                     ?? throw new InvalidOperationException("Backup manifest is missing."));
                    break;
                case RollbackActionKind.DeleteManifest:
                    AtomicFile.Delete(root, Manifest.RelativePath);
                    break;
            }
        }

        BackupStore.Delete(root, backup.Id);
        return new RollbackResult(backup, actions, true);
    }
}
