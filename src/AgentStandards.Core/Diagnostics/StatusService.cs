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
    Malformed
}

public sealed record FileStatus(string Path, ManagedMode Mode, FileState State, string Source);

public sealed record StatusReport(
    string Root,
    Manifest Manifest,
    IReadOnlyList<FileStatus> Files,
    IReadOnlyList<string> PendingConflicts,
    string BundledVersion,
    bool NewerBundledTemplates,
    IReadOnlyList<BackupRecord> Backups);

public static class StatusService
{
    public static StatusReport Get(string root, string bundledVersion)
    {
        var manifest = ManifestStore.Load(root) ?? throw new ManifestException($"No installation found in '{root}'. Run 'agent-standards init' first.");
        var files = manifest.Files.Select(f => new FileStatus(f.Path, f.Mode, GetState(root, f), f.Source)).ToList();

        var newer = string.Equals(manifest.Templates.Origin, "bundled", StringComparison.Ordinal) || manifest.Templates.Origin == "package"
            ? NuGetVersion.TryParse(bundledVersion, out var b) && NuGetVersion.TryParse(manifest.Templates.Version, out var i) && b > i && !manifest.Templates.Pinned
            : false;

        return new StatusReport(root, manifest, files, PendingConflicts(root), bundledVersion, newer, BackupStore.List(root));
    }

    public static FileState GetState(string root, ManifestFile file)
    {
        var current = AtomicFile.ReadTextOrNull(root, file.Path);
        if (current is null)
            return FileState.Missing;

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
