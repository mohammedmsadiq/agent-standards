using AgentStandards.Core.Rendering;

namespace AgentStandards.Core.Installation;

public enum PlanActionKind
{
    /// <summary>New file.</summary>
    Create,
    /// <summary>Unmodified managed content replaced with the new version.</summary>
    Update,
    /// <summary>Managed block appended to an existing user file, or MCP servers added to existing JSON.</summary>
    Merge,
    /// <summary>Local or unmanaged content replaced because --force was given (a backup is kept).</summary>
    Overwrite,
    /// <summary>A managed file the user deleted is recreated because --force was given.</summary>
    Restore,
    Unchanged,
    /// <summary>Existing content already matches; start (or continue) tracking it.</summary>
    Adopt,
    /// <summary>Local modification kept; the template did not change.</summary>
    KeepLocal,
    /// <summary>Local modification kept; the new template version was saved under .agent-standards/conflicts/.</summary>
    Conflict,
    /// <summary>Managed file deleted by the user; left deleted.</summary>
    SkipDeleted,
    /// <summary>No longer provided by the templates; kept until --prune.</summary>
    Obsolete,
    /// <summary>Obsolete, unmodified file deleted (--prune).</summary>
    Delete,
    /// <summary>Obsolete managed block removed from a user file (--prune).</summary>
    RemoveBlock,
    /// <summary>Stop tracking without touching the file.</summary>
    Untrack
}

public sealed class PlanAction
{
    public required PlanActionKind Kind { get; init; }
    public required string Path { get; init; }
    public required ManagedMode Mode { get; init; }
    public required string Reason { get; init; }
    public string Source { get; init; } = "";
    /// <summary>Current file content (null when the file does not exist).</summary>
    public string? Before { get; init; }
    /// <summary>Full file content to write; null when nothing is written.</summary>
    public string? After { get; init; }
    /// <summary>Incoming content saved for manual merge when <see cref="Kind"/> is Conflict.</summary>
    public string? Incoming { get; init; }
    /// <summary>Manifest entry to record after the operation; null means the path is not tracked.</summary>
    public ManifestFile? Entry { get; init; }

    public bool Writes => After is not null && Kind is PlanActionKind.Create or PlanActionKind.Update or PlanActionKind.Merge
        or PlanActionKind.Overwrite or PlanActionKind.Restore or PlanActionKind.RemoveBlock;

    public bool Deletes => Kind == PlanActionKind.Delete;
}

public sealed class InstallPlan
{
    public required string Root { get; init; }
    public required InstallScope Scope { get; init; }
    public required IReadOnlyList<PlanAction> Actions { get; init; }
    public required Manifest Manifest { get; init; }
    public string? PreviousManifestText { get; init; }

    public bool HasConflicts => Actions.Any(a => a.Kind == PlanActionKind.Conflict);
    public bool HasChanges => Actions.Any(a => a.Writes || a.Deletes || a.Kind == PlanActionKind.Conflict) ||
                              PreviousManifestText != ManifestStore.Serialize(Manifest);
}
