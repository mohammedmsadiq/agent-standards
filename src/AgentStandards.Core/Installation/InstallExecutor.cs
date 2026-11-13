namespace AgentStandards.Core.Installation;

public sealed record ExecutionResult(string? BackupId, IReadOnlyList<string> Written, IReadOnlyList<string> Deleted, IReadOnlyList<string> ConflictCopies);

/// <summary>Applies an <see cref="InstallPlan"/> as one <see cref="Transaction"/>, then trims old backups.</summary>
public static class InstallExecutor
{
    public static ExecutionResult Apply(InstallPlan plan, string operation, DateTimeOffset now, string? previousTemplatesVersion)
    {
        if (plan.Changes.Count == 0)
            return new ExecutionResult(null, [], [], []);

        var id = Transaction.Run(plan.Root, operation, plan.Changes, new BackupRecord
        {
            TemplatesVersionBefore = previousTemplatesVersion,
            TemplatesVersionAfter = plan.Manifest.Templates.Version
        }, now);
        BackupStore.Trim(plan.Root);

        bool IsCopy(FileChange c) => c.Path.StartsWith(InstallPlanner.ConflictsFolder + "/", StringComparison.Ordinal);
        return new ExecutionResult(id,
            plan.Changes.Where(c => c.After is not null && !IsCopy(c) && c.Path != Manifest.RelativePath).Select(c => c.Path).ToList(),
            plan.Changes.Where(c => c.After is null && !IsCopy(c)).Select(c => c.Path).ToList(),
            plan.Changes.Where(c => c.After is not null && IsCopy(c)).Select(c => c.Path).ToList());
    }
}
