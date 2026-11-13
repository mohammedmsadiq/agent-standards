using System.Text.Json;
using System.Text.Json.Nodes;
using AgentStandards.Core.IO;
using AgentStandards.Core.Rendering;

namespace AgentStandards.Core.Installation;

public sealed record PlanOptions(bool Force, bool Prune);

/// <summary>
/// Computes a three-way plan (previous manifest hash, current disk content, new template content) for every
/// managed path. The planner never writes; <see cref="InstallExecutor"/> applies the result.
/// </summary>
public static class InstallPlanner
{
    public const string ConflictsFolder = ".agent-standards/conflicts";

    public static IReadOnlyList<PlanAction> Plan(string root, IReadOnlyList<RenderedFile> rendered, Manifest? previous, PlanOptions options)
    {
        var duplicates = rendered.GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new InvalidOperationException($"Multiple templates render to the same path: {string.Join(", ", duplicates)}.");

        var oldEntries = (previous?.Files ?? []).ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var actions = new List<PlanAction>();

        foreach (var file in rendered)
        {
            var path = SafePaths.NormalizeRelative(file.Path);
            if (string.Equals(path, Manifest.RelativePath, StringComparison.OrdinalIgnoreCase) || path.StartsWith(ConflictsFolder + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Templates may not write to reserved path '{path}'.");

            var current = AtomicFile.ReadTextOrNull(root, path);
            oldEntries.TryGetValue(path, out var old);
            actions.Add(file.Mode switch
            {
                ManagedMode.Whole => PlanWhole(path, file, current, old, options),
                ManagedMode.Block => PlanBlock(path, file, current, old, options),
                ManagedMode.JsonMerge => PlanJson(path, file, current, old),
                _ => throw new ArgumentOutOfRangeException(nameof(rendered))
            });
        }

        var renderedPaths = rendered.Select(r => SafePaths.NormalizeRelative(r.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var old in oldEntries.Values.Where(e => !renderedPaths.Contains(e.Path)))
            actions.Add(PlanObsolete(root, old, options));

        return actions;
    }

    private static ManifestFile Entry(string path, RenderedFile file, string hash, List<string>? keys = null) =>
        new() { Path = path, Mode = file.Mode, Hash = hash, Source = file.Source, JsonKeys = keys };

    private static PlanAction PlanWhole(string path, RenderedFile file, string? current, ManifestFile? old, PlanOptions options)
    {
        var content = file.Content;
        var newHash = ContentHash.Compute(content);
        PlanAction Make(PlanActionKind kind, string reason, string? after = null, ManifestFile? entry = null, string? incoming = null) =>
            new() { Kind = kind, Path = path, Mode = file.Mode, Source = file.Source, Reason = reason, Before = current, After = after, Entry = entry, Incoming = incoming };

        if (current is null)
        {
            if (old is null)
                return Make(PlanActionKind.Create, "new file", content, Entry(path, file, newHash));
            return options.Force
                ? Make(PlanActionKind.Restore, "managed file was deleted locally; restoring because --force was given", content, Entry(path, file, newHash))
                : Make(PlanActionKind.SkipDeleted, "managed file was deleted locally; leaving it deleted (use --force to restore)", entry: old);
        }

        var currentHash = ContentHash.Compute(current);
        if (old is null)
        {
            if (currentHash == newHash)
                return Make(PlanActionKind.Adopt, "existing file already matches the template", entry: Entry(path, file, newHash));
            return options.Force
                ? Make(PlanActionKind.Overwrite, "existing unmanaged file replaced because --force was given (backup kept)", content, Entry(path, file, newHash))
                : Make(PlanActionKind.Conflict, "file exists and is not managed by agent-standards; left untouched", incoming: content);
        }

        return ThreeWay(Make, currentHash, old, newHash, content, content, options, path, file);
    }

    /// <summary>
    /// Three-way decision with baseline B (last applied template), local L, new template N and pending P (an
    /// unresolved incoming version). Local changes always win unless --force is given; a conflict keeps B and
    /// records N as pending, so it stays visible until resolved.
    /// </summary>
    private static PlanAction ThreeWay(Func<PlanActionKind, string, string?, ManifestFile?, string?, PlanAction> make,
        string currentHash, ManifestFile old, string newHash, string newFileContent, string incoming, PlanOptions options, string path, RenderedFile file)
    {
        var pending = old.PendingHash is not null;
        if (currentHash == newHash)
        {
            return currentHash == old.Hash && !pending
                ? make(PlanActionKind.Unchanged, "up to date", null, old, null)
                : make(PlanActionKind.Adopt, pending ? "local content now matches the incoming template; conflict resolved" : "local content already matches the new template",
                    null, Entry(path, file, newHash, old.JsonKeys), null);
        }

        if (currentHash == old.Hash)
            return make(PlanActionKind.Update, pending ? "local edits were reverted; updating to the new template and clearing the conflict" : "unmodified locally; updating to the new template",
                newFileContent, Entry(path, file, newHash, old.JsonKeys), null);
        if (newHash == old.Hash)
            return make(PlanActionKind.KeepLocal, pending ? "template reverted to the applied version; conflict cleared, keeping local version" : "modified locally; template unchanged, keeping local version",
                null, WithPending(old, null), null);
        if (options.Force)
            return make(PlanActionKind.Overwrite, "modified locally; replaced because --force was given (backup kept)", newFileContent, Entry(path, file, newHash, old.JsonKeys), null);

        var reason = old.PendingHash == newHash
            ? $"unresolved conflict: local version kept; merge {ConflictsFolder}/{path}.incoming, then run 'agent-standards resolve {path}'"
            : $"modified locally and changed upstream: local version kept, incoming saved to {ConflictsFolder}/{path}.incoming; merge it, then run 'agent-standards resolve {path}'";
        return make(PlanActionKind.Conflict, reason, null, WithPending(old, newHash), incoming);
    }

    private static ManifestFile WithPending(ManifestFile f, string? pending) => new()
    {
        Path = f.Path, Mode = f.Mode, Hash = f.Hash, Source = f.Source, JsonKeys = f.JsonKeys, PendingHash = pending
    };

    public static (string Incoming, string Base) ConflictCopyPaths(string path) =>
        ($"{ConflictsFolder}/{path}.incoming", $"{ConflictsFolder}/{path}.base");

    public sealed record FileChangeSet(List<FileChange> Changes, Dictionary<string, string> ConflictCopies, List<string> Warnings);

    /// <summary>
    /// Translates plan actions into concrete file changes: managed files plus conflict copies (written for
    /// conflicts, removed once a path is no longer in conflict). Changes that would not alter a file are dropped.
    /// Conflict copies the developer edited are kept unless <paramref name="force"/> is set.
    /// </summary>
    public static FileChangeSet BuildFileChanges(string root, IReadOnlyList<PlanAction> actions, IReadOnlyDictionary<string, string>? previousCopies, bool force)
    {
        var changes = new List<FileChange>();
        var copies = new Dictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var action in actions)
        {
            if (action.Writes)
                changes.Add(new FileChange(action.Path, action.After, Expected(action.Before)));
            else if (action.Deletes)
                changes.Add(new FileChange(action.Path, null, Expected(action.Before)));

            var (incomingPath, basePath) = ConflictCopyPaths(action.Path);
            visited.Add(incomingPath);
            visited.Add(basePath);
            if (action.Kind == PlanActionKind.Conflict)
            {
                WriteCopy(root, incomingPath, action.Incoming, previousCopies, force, changes, copies, warnings);
                // The baseline can only be rebuilt on the first report (the manifest then moves to the new template
                // version), so an existing .base copy is kept while the conflict stays pending.
                if (action.BaseContent is not null)
                    WriteCopy(root, basePath, action.BaseContent, previousCopies, force, changes, copies, warnings);
                else if (previousCopies?.TryGetValue(basePath, out var hash) == true && AtomicFile.ReadTextOrNull(root, basePath) is not null)
                    copies[basePath] = hash;
            }
            else
            {
                RemoveCopy(root, incomingPath, previousCopies, force, changes, copies, warnings);
                RemoveCopy(root, basePath, previousCopies, force, changes, copies, warnings);
            }
        }

        // Copies recorded for paths that are no longer planned at all (for example an excluded item).
        foreach (var path in previousCopies?.Keys.Where(k => !visited.Contains(k)) ?? [])
            RemoveCopy(root, path, previousCopies, force, changes, copies, warnings);

        return new FileChangeSet(changes, copies, warnings);
    }

    /// <summary>True when the copy still has exactly the content the toolkit wrote.</summary>
    internal static bool IsUnmodifiedCopy(string current, string path, IReadOnlyDictionary<string, string>? recorded) =>
        recorded?.TryGetValue(path, out var hash) == true && hash == ContentHash.Compute(current);

    private static void WriteCopy(string root, string path, string? content, IReadOnlyDictionary<string, string>? previous, bool force,
        List<FileChange> changes, Dictionary<string, string> copies, List<string> warnings)
    {
        if (content is null)
            return;
        var current = AtomicFile.ReadTextOrNull(root, path);
        if (current == content)
        {
            copies[path] = ContentHash.Compute(content);
            return;
        }
        if (current is null || force || IsUnmodifiedCopy(current, path, previous))
        {
            changes.Add(new FileChange(path, content, current is null ? Transaction.Absent : ContentHash.Compute(current)));
            copies[path] = ContentHash.Compute(content);
            return;
        }
        if (previous?.TryGetValue(path, out var hash) == true)
            copies[path] = hash;
        warnings.Add($"Kept your edited conflict copy {path}; the latest incoming version was not written. Delete the copy (or use --force) and re-run update to refresh it.");
    }

    private static void RemoveCopy(string root, string path, IReadOnlyDictionary<string, string>? previous, bool force,
        List<FileChange> changes, Dictionary<string, string> copies, List<string> warnings)
    {
        var current = AtomicFile.ReadTextOrNull(root, path);
        if (current is null)
            return;
        if (force || IsUnmodifiedCopy(current, path, previous))
        {
            changes.Add(new FileChange(path, null, ContentHash.Compute(current)));
            return;
        }
        if (previous?.TryGetValue(path, out var hash) == true)
            copies[path] = hash;
        warnings.Add($"Kept {path} because it was edited; delete it yourself once your merge is done (or use --force).");
    }

    private static string Expected(string? before) => before is null ? Transaction.Absent : ContentHash.Compute(before);

    private static PlanAction PlanBlock(string path, RenderedFile file, string? current, ManifestFile? old, PlanOptions options)
    {
        var inner = file.Content.Trim('\n');
        var newHash = ContentHash.Compute(inner);
        PlanAction Make(PlanActionKind kind, string reason, string? after = null, ManifestFile? entry = null, string? incoming = null) =>
            new() { Kind = kind, Path = path, Mode = file.Mode, Source = file.Source, Reason = reason, Before = current, After = after, Entry = entry, Incoming = incoming };

        if (current is null)
        {
            if (old is null)
                return Make(PlanActionKind.Create, "new file", ManagedBlock.CreateFile(inner, file.Seed), Entry(path, file, newHash));
            return options.Force
                ? Make(PlanActionKind.Restore, "file was deleted locally; recreating because --force was given", ManagedBlock.CreateFile(inner, file.Seed), Entry(path, file, newHash))
                : Make(PlanActionKind.SkipDeleted, "file was deleted locally; leaving it deleted (use --force to recreate)", entry: old);
        }

        var (state, existingInner) = ManagedBlock.Extract(current);
        switch (state)
        {
            case ManagedBlock.BlockState.Malformed:
                return Make(PlanActionKind.Conflict, "managed block markers are malformed; fix or remove them, then re-run", entry: old,
                    incoming: ManagedBlock.Wrap(inner));
            case ManagedBlock.BlockState.Absent when old is null:
                return Make(PlanActionKind.Merge, "existing file kept; managed block appended", ManagedBlock.Append(current, inner), Entry(path, file, newHash));
            case ManagedBlock.BlockState.Absent:
                return options.Force
                    ? Make(PlanActionKind.Merge, "managed block was removed locally; re-adding because --force was given", ManagedBlock.Append(current, inner), Entry(path, file, newHash))
                    : Make(PlanActionKind.SkipDeleted, "managed block was removed locally; leaving it out (use --force to re-add)", entry: old);
        }

        var currentHash = ContentHash.Compute(existingInner!);
        var replaced = ManagedBlock.Replace(current, inner);
        if (old is null)
        {
            if (currentHash == newHash)
                return Make(PlanActionKind.Adopt, "managed block already matches the template", entry: Entry(path, file, newHash));
            return options.Force
                ? Make(PlanActionKind.Overwrite, "untracked managed block replaced because --force was given (backup kept)", replaced, Entry(path, file, newHash))
                : Make(PlanActionKind.Conflict, "file has an untracked managed block that differs from the template; left untouched", incoming: replaced);
        }

        return ThreeWay((k, r, a, e, i) => Make(k, r, a, e, i), currentHash, old, newHash, replaced, replaced, options, path, file);
    }

    private static PlanAction PlanJson(string path, RenderedFile file, string? current, ManifestFile? old)
    {
        var desired = JsonNode.Parse(file.Content)!.AsObject();
        var hash = ContentHash.Compute(file.Content);
        PlanAction Make(PlanActionKind kind, string reason, string? after = null, ManifestFile? entry = null) =>
            new() { Kind = kind, Path = path, Mode = file.Mode, Source = file.Source, Reason = reason, Before = current, After = after, Entry = entry };
        var writeOptions = new JsonSerializerOptions { WriteIndented = true };

        if (current is null)
        {
            var created = new JsonObject { ["mcpServers"] = desired.DeepClone() };
            return Make(PlanActionKind.Create, "new MCP configuration", created.ToJsonString(writeOptions) + "\n",
                Entry(path, file, hash, desired.Select(p => p.Key).ToList()));
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(current, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?.AsObject()
                   ?? throw new InvalidOperationException();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Make(PlanActionKind.Conflict, "existing file is not a JSON object; left untouched", entry: old);
        }

        if (root["mcpServers"] is not JsonObject servers)
        {
            if (root.ContainsKey("mcpServers"))
                return Make(PlanActionKind.Conflict, "existing 'mcpServers' is not an object; left untouched", entry: old);
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }

        var added = new List<string>();
        var kept = new List<string>();
        foreach (var (name, server) in desired)
        {
            if (servers.ContainsKey(name))
            {
                if (!JsonNode.DeepEquals(servers[name], server))
                    kept.Add(name);
                continue;
            }
            servers[name] = server!.DeepClone();
            added.Add(name);
        }

        var keys = (old?.JsonKeys ?? []).Concat(added).Distinct(StringComparer.Ordinal).ToList();
        var keptNote = kept.Count == 0 ? "" : $"; kept your existing definition of {string.Join(", ", kept)} (differs from the template)";
        return added.Count == 0
            ? Make(PlanActionKind.Unchanged, "all MCP servers already present (existing entries are never modified)" + keptNote, entry: Entry(path, file, hash, keys))
            : Make(PlanActionKind.Merge, $"adding MCP servers: {string.Join(", ", added)} (existing entries untouched){keptNote}", root.ToJsonString(writeOptions) + "\n",
                Entry(path, file, hash, keys));
    }

    private static PlanAction PlanObsolete(string root, ManifestFile old, PlanOptions options)
    {
        var current = AtomicFile.ReadTextOrNull(root, old.Path);
        PlanAction Make(PlanActionKind kind, string reason, string? after = null, ManifestFile? entry = null) =>
            new() { Kind = kind, Path = old.Path, Mode = old.Mode, Source = old.Source, Reason = reason, Before = current, After = after, Entry = entry };

        if (current is null)
            return Make(PlanActionKind.Untrack, "no longer provided and already removed");

        switch (old.Mode)
        {
            case ManagedMode.Whole:
                var unmodified = ContentHash.Compute(current) == old.Hash;
                if (!options.Prune)
                    return Make(PlanActionKind.Obsolete, "no longer provided by the selected templates; kept (use --prune to remove)", entry: old);
                return unmodified
                    ? Make(PlanActionKind.Delete, "no longer provided and unmodified; deleting (--prune)")
                    : Make(PlanActionKind.Untrack, "no longer provided but modified locally; kept and no longer tracked (--prune)");

            case ManagedMode.Block:
                var (state, inner) = ManagedBlock.Extract(current);
                if (state != ManagedBlock.BlockState.Present)
                    return Make(PlanActionKind.Untrack, "no longer provided and managed block already removed");
                if (!options.Prune)
                    return Make(PlanActionKind.Obsolete, "no longer provided by the selected templates; kept (use --prune to remove the block)", entry: old);
                return ContentHash.Compute(inner!) == old.Hash
                    ? Make(PlanActionKind.RemoveBlock, "no longer provided; removing the managed block, keeping the rest of the file (--prune)", ManagedBlock.Remove(current))
                    : Make(PlanActionKind.Untrack, "no longer provided but block modified locally; kept and no longer tracked (--prune)");

            default:
                return options.Prune
                    ? Make(PlanActionKind.Untrack, "no longer provided; JSON entries are never removed automatically (--prune stops tracking)")
                    : Make(PlanActionKind.Obsolete, "no longer provided; JSON entries are never removed automatically", entry: old);
        }
    }
}
