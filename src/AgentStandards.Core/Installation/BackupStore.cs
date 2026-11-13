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
    public List<BackupFile> Files { get; set; } = [];
}

public sealed class BackupFile
{
    public string Path { get; set; } = "";
    /// <summary>Whether the file existed before the operation (and so has a saved copy).</summary>
    public bool Existed { get; set; }
    /// <summary>Hash of what the operation wrote, or null when it deleted the file.</summary>
    public string? WrittenHash { get; set; }
}

/// <summary>Snapshots files before an operation so that <c>rollback</c> can restore them.</summary>
public static class BackupStore
{
    public const string Folder = ".agent-standards/backups";
    public const int Retain = 10;

    public static string NewId(string operation, DateTimeOffset now) =>
        now.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "-" + operation;

    public static void Save(string root, BackupRecord record, IReadOnlyDictionary<string, string> originals, string? manifestText)
    {
        var dir = $"{Folder}/{record.Id}";
        foreach (var (path, content) in originals)
            AtomicFile.WriteText(root, $"{dir}/files/{path}", content);
        if (manifestText is not null)
            AtomicFile.WriteText(root, $"{dir}/manifest.json", manifestText);
        AtomicFile.WriteText(root, $"{dir}/backup.json", JsonSerializer.Serialize(record, ManifestStore.Options) + "\n");
    }

    public static IReadOnlyList<BackupRecord> List(string root)
    {
        var dir = SafePaths.ResolveSafe(root, Folder);
        if (!Directory.Exists(dir))
            return [];
        var result = new List<BackupRecord>();
        foreach (var sub in Directory.GetDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            try
            {
                var text = AtomicFile.ReadTextOrNull(root, $"{Folder}/{name}/backup.json");
                var record = text is null ? null : JsonSerializer.Deserialize<BackupRecord>(text, ManifestStore.Options);
                if (record is not null && record.Id == name)
                    result.Add(record);
            }
            catch (Exception ex) when (ex is JsonException or UnsafePathException or IOException)
            {
                // Ignore unreadable backups; doctor reports them.
            }
        }
        return result.OrderByDescending(r => r.Id, StringComparer.Ordinal).ToList();
    }

    public static string? ReadFile(string root, string id, string path) =>
        AtomicFile.ReadTextOrNull(root, $"{Folder}/{id}/files/{path}");

    public static string? ReadManifest(string root, string id) =>
        AtomicFile.ReadTextOrNull(root, $"{Folder}/{id}/manifest.json");

    public static void Delete(string root, string id)
    {
        var dir = SafePaths.ResolveSafe(root, $"{Folder}/{SafePaths.NormalizeRelative(id)}");
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    public static void Trim(string root)
    {
        foreach (var old in List(root).Skip(Retain))
            Delete(root, old.Id);
    }
}
