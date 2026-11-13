using System.Globalization;
using System.Text.Json;
using AgentStandards.Core.IO;

namespace AgentStandards.Core.Installation;

public sealed class BackupRecord
{
    public string Id { get; set; } = "";
    public string Operation { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? TemplatesVersionBefore { get; set; }
    public string? TemplatesVersionAfter { get; set; }
    public bool HadManifest { get; set; }
    /// <summary>True for the safety snapshot a rollback or resolve takes of itself; hidden from the default rollback target.</summary>
    public bool Internal { get; set; }
    public List<BackupFile> Files { get; set; } = [];
    /// <summary>Directories the operation created (deepest first); rollback removes them when empty.</summary>
    public List<string>? CreatedDirectories { get; set; }
}

public sealed class BackupFile
{
    public string Path { get; set; } = "";
    /// <summary>Whether the file existed before the operation (and so has a saved copy).</summary>
    public bool Existed { get; set; }
    /// <summary>Hash of what the operation wrote, or null when it deleted the file.</summary>
    public string? WrittenHash { get; set; }
}

/// <summary>Snapshots files before an operation so that <c>rollback</c> (and automatic recovery) can restore them.</summary>
public static class BackupStore
{
    public const string Folder = ".agent-standards/backups";
    public const int Retain = 10;

    public static string NewId(string root, string operation, DateTimeOffset now)
    {
        var stamp = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var id = $"{stamp}-{operation}";
        for (var n = 2; Directory.Exists(SafePaths.Resolve(root, $"{Folder}/{id}")); n++)
            id = $"{stamp}-{operation}-{n}";
        return id;
    }

    /// <summary>Writes the saved originals first and <c>backup.json</c> last, so a record only exists once complete.</summary>
    public static void Save(string root, BackupRecord record, IReadOnlyDictionary<string, string> originals,
        Action<string>? beforeFile = null, Action? beforeRecord = null)
    {
        var dir = $"{Folder}/{record.Id}";
        foreach (var (path, content) in originals)
        {
            beforeFile?.Invoke(path);
            AtomicFile.WriteText(root, $"{dir}/files/{path}", content);
        }
        beforeRecord?.Invoke();
        AtomicFile.WriteText(root, $"{dir}/backup.json", JsonSerializer.Serialize(record, ManifestStore.Options) + "\n");
    }

    public static BackupRecord? Get(string root, string id)
    {
        if (!SafePaths.TryNormalizeRelative(id, out var normalized) || normalized.Contains('/'))
            return null;
        try
        {
            var text = AtomicFile.ReadTextOrNull(root, $"{Folder}/{normalized}/backup.json");
            var record = text is null ? null : JsonSerializer.Deserialize<BackupRecord>(text, ManifestStore.Options);
            return record is not null && record.Id == normalized && record.Files is not null ? record : null;
        }
        catch (Exception ex) when (ex is JsonException or UnsafePathException or IOException)
        {
            return null;
        }
    }

    /// <summary>Complete backups, newest first. Internal safety snapshots are excluded unless requested.</summary>
    public static IReadOnlyList<BackupRecord> List(string root, bool includeInternal = false)
    {
        var dir = SafePaths.ResolveSafe(root, Folder);
        if (!Directory.Exists(dir))
            return [];
        return Directory.GetDirectories(dir)
            .Select(d => Get(root, Path.GetFileName(d)))
            .OfType<BackupRecord>()
            .Where(r => includeInternal || !r.Internal)
            .OrderByDescending(r => r.Id, StringComparer.Ordinal)
            .ToList();
    }

    public static string? ReadFile(string root, string id, string path) =>
        AtomicFile.ReadTextOrNull(root, $"{Folder}/{id}/files/{path}");

    public static string? ReadManifest(string root, string id) => ReadFile(root, id, Manifest.RelativePath);

    public static void Delete(string root, string id)
    {
        var normalized = SafePaths.NormalizeRelative(id);
        if (normalized.Contains('/'))
            throw new UnsafePathException(id, "backup IDs are single folder names");
        var dir = SafePaths.ResolveSafe(root, $"{Folder}/{normalized}");
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// Best effort: keeps the newest <see cref="Retain"/> complete backups and removes incomplete leftovers. Never
    /// removes the backup an interrupted operation still needs. Failures are ignored; trimming is retried next time.
    /// </summary>
    public static void Trim(string root)
    {
        try
        {
            var needed = OperationJournal.Read(root)?.BackupId;
            foreach (var old in List(root).Skip(Retain).Where(b => b.Id != needed))
                Transaction.TryDeleteBackup(root, old.Id);

            var dir = SafePaths.ResolveSafe(root, Folder);
            foreach (var sub in Directory.Exists(dir) ? Directory.GetDirectories(dir) : [])
            {
                var name = Path.GetFileName(sub);
                if (name != needed && !File.Exists(Path.Combine(sub, "backup.json")))
                    Transaction.TryDeleteBackup(root, name);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnsafePathException)
        {
        }
    }
}
