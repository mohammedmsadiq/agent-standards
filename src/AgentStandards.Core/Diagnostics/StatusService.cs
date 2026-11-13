using System.Text.Json.Nodes;
using AgentStandards.Core.Installation;
using AgentStandards.Core.IO;
using AgentStandards.Core.Rendering;
using NuGet.Versioning;

namespace AgentStandards.Core.Diagnostics;

public enum FileState
{
    Unmodified,
    Modified,
    Missing,
    BlockMissing,
    Malformed,
    /// <summary>Unresolved conflict: local version kept, incoming version awaiting manual merge.</summary>
    Conflict
}

public sealed record FileStatus(string Path, ManagedMode Mode, FileState State, string Source);

public sealed record StatusReport(
    string Root,
    Manifest Manifest,
    IReadOnlyList<FileStatus> Files,
    IReadOnlyList<string> PendingConflicts,
    string BundledVersion,
    bool NewerBundledTemplates,
    IReadOnlyList<BackupRecord> Backups,
    OperationJournal? InterruptedOperation,
    IReadOnlyList<string> UnmanagedFiles,
    bool OperationRunning = false);

public static class StatusService
{
    public static StatusReport Get(string root, string bundledVersion)
    {
        var manifest = ManifestStore.Load(root) ?? throw new ManifestException($"No installation found in '{root}'. Run 'agent-standards init' first.");
        var files = manifest.Files.Select(f => new FileStatus(f.Path, f.Mode, GetState(root, f), f.Source)).ToList();

        var newer = string.Equals(manifest.Templates.Origin, "bundled", StringComparison.Ordinal) || manifest.Templates.Origin == "package"
            ? NuGetVersion.TryParse(bundledVersion, out var b) && NuGetVersion.TryParse(manifest.Templates.Version, out var i) && b > i && !manifest.Templates.Pinned
            : false;

        return new StatusReport(root, manifest, files, PendingConflicts(root), bundledVersion, newer, BackupStore.List(root),
            OperationJournal.Read(root), UnmanagedFiles(root, manifest), OperationLock.IsHeld(root));
    }

    public static FileState GetState(string root, ManifestFile file, bool ignorePending = false)
    {
        var current = AtomicFile.ReadTextOrNull(root, file.Path);
        if (current is null)
            return FileState.Missing;
        if (file.PendingHash is not null && !ignorePending)
            return FileState.Conflict;

        switch (file.Mode)
        {
            case ManagedMode.Whole:
                return ContentHash.Compute(current) == file.Hash ? FileState.Unmodified : FileState.Modified;
            case ManagedMode.Block:
                var (state, inner) = ManagedBlock.Extract(current);
                return state switch
                {
                    ManagedBlock.BlockState.Absent => FileState.BlockMissing,
                    ManagedBlock.BlockState.Malformed => FileState.Malformed,
                    _ => ContentHash.Compute(inner!) == file.Hash ? FileState.Unmodified : FileState.Modified
                };
            default:
                try
                {
                    var servers = JsonNode.Parse(current)?["mcpServers"] as JsonObject;
                    return servers is not null && (file.JsonKeys ?? []).All(servers.ContainsKey) ? FileState.Unmodified : FileState.Modified;
                }
                catch (System.Text.Json.JsonException)
                {
                    return FileState.Malformed;
                }
        }
    }

    private static readonly string[] AssistantFiles = ["AGENTS.md", "CLAUDE.md", ".mcp.json", ".github/copilot-instructions.md", ".claude/CLAUDE.md", ".copilot/copilot-instructions.md"];
    private static readonly string[] AssistantFolders = [".claude/rules", ".claude/skills", ".claude/agents", ".github/instructions", ".github/skills", ".github/agents",
        ".copilot/instructions", ".copilot/skills", ".copilot/agents"];

    /// <summary>Assistant configuration files present under the root that agent-standards does not manage.</summary>
    public static IReadOnlyList<string> UnmanagedFiles(string root, Manifest manifest)
    {
        var managed = manifest.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var file in AssistantFiles)
            if (!managed.Contains(file) && File.Exists(SafePaths.Resolve(root, file)))
                result.Add(file);
        foreach (var folder in AssistantFolders)
        {
            var dir = SafePaths.Resolve(root, folder);
            if (!Directory.Exists(dir) || SafePaths.IsLink(dir))
                continue;
            foreach (var path in Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                var rel = folder + "/" + Path.GetRelativePath(dir, path).Replace(Path.DirectorySeparatorChar, '/');
                if (!managed.Contains(rel))
                    result.Add(rel);
            }
        }
        return result.Order(StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<string> PendingConflicts(string root)
    {
        var dir = SafePaths.Resolve(root, InstallPlanner.ConflictsFolder);
        if (!Directory.Exists(dir) || SafePaths.IsLink(dir))
            return [];
        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => InstallPlanner.ConflictsFolder + "/" + Path.GetRelativePath(dir, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal).ToList();
    }
}
