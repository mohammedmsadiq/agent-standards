using System.Text.Json;
using System.Text.Json.Serialization;
using AgentStandards.Core.IO;
using AgentStandards.Core.Rendering;

namespace AgentStandards.Core.Installation;

public sealed class Manifest
{
    public const int CurrentSchemaVersion = 1;
    public const string RelativePath = ".agent-standards/manifest.json";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string ToolVersion { get; set; } = "";
    public InstallScope Scope { get; set; }
    public TemplatesInfo Templates { get; set; } = new();
    /// <summary>Profiles as requested (or detected) — the inputs.</summary>
    public List<string> Profiles { get; set; } = [];
    /// <summary>All profiles after applying extends — informational.</summary>
    public List<string> ResolvedProfiles { get; set; } = [];
    public List<string> Assistants { get; set; } = [];
    public List<string> Exclude { get; set; } = [];
    public bool Mcp { get; set; }
    public DateTimeOffset InstalledAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<ManifestFile> Files { get; set; } = [];
}

public sealed class TemplatesInfo
{
    public string PackageId { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>True when the version was chosen explicitly; <c>update</c> keeps pinned versions.</summary>
    public bool Pinned { get; set; }
    /// <summary>"bundled", "package" or "directory".</summary>
    public string Origin { get; set; } = "bundled";
    /// <summary>Template directory for <c>--templates</c> installs (relative to the root when inside it).</summary>
    public string? Directory { get; set; }
}

public sealed class ManifestFile
{
    public string Path { get; set; } = "";
    public ManagedMode Mode { get; set; }
    /// <summary>SHA-256 of the managed content as last written (whole file, block body, or desired JSON).</summary>
    public string Hash { get; set; } = "";
    public string Source { get; set; } = "";
    /// <summary>JsonMerge only: server names this toolkit added.</summary>
    public List<string>? JsonKeys { get; set; }
    /// <summary>
    /// Set while a conflict is unresolved: the hash of the incoming template content. <see cref="Hash"/> keeps the
    /// last applied baseline until the conflict is resolved.
    /// </summary>
    public string? PendingHash { get; set; }
}

public sealed class ManifestException(string message, Exception? inner = null) : Exception(message, inner);

public static class ManifestStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static bool Exists(string root) => File.Exists(SafePaths.Resolve(root, Manifest.RelativePath));

    /// <summary>Loads the manifest, or returns null when none exists. Throws <see cref="ManifestException"/> when corrupt.</summary>
    public static Manifest? Load(string root)
    {
        var text = AtomicFile.ReadTextOrNull(root, Manifest.RelativePath);
        if (text is null)
            return null;

        Manifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<Manifest>(text, Options);
        }
        catch (JsonException ex)
        {
            throw new ManifestException($"{Manifest.RelativePath} is corrupt ({ex.Message}). Restore it from version control, run 'agent-standards rollback', or re-run 'agent-standards init --force'.", ex);
        }

        if (manifest is null)
            throw new ManifestException($"{Manifest.RelativePath} is empty.");
        if (manifest.Files is null || manifest.Templates is null || manifest.Profiles is null || manifest.Assistants is null || manifest.Exclude is null)
            throw new ManifestException($"{Manifest.RelativePath} is incomplete (missing files, templates, profiles, assistants or exclude). Restore it from version control or re-run 'agent-standards init --force'.");
        manifest.ResolvedProfiles ??= [];
        if (manifest.SchemaVersion > Manifest.CurrentSchemaVersion)
            throw new ManifestException($"{Manifest.RelativePath} uses schema {manifest.SchemaVersion}; this tool supports {Manifest.CurrentSchemaVersion}. Update the agent-standards tool.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (file is null)
                throw new ManifestException($"{Manifest.RelativePath} contains an empty file entry.");
            if (!Enum.IsDefined(file.Mode))
                throw new ManifestException($"{Manifest.RelativePath} entry '{file.Path}' has an unknown mode.");
            if (!SafePaths.TryNormalizeRelative(file.Path, out var normalized))
                throw new ManifestException($"{Manifest.RelativePath} contains an unsafe path '{file.Path}'.");
            if (string.IsNullOrEmpty(file.Hash))
                throw new ManifestException($"{Manifest.RelativePath} entry '{file.Path}' has no hash.");
            if (!seen.Add(normalized))
                throw new ManifestException($"{Manifest.RelativePath} lists '{file.Path}' more than once.");
            file.Path = normalized;
        }

        return manifest;
    }

    public static string Serialize(Manifest manifest) => JsonSerializer.Serialize(manifest, Options) + "\n";

    public static void Save(string root, Manifest manifest) => AtomicFile.WriteText(root, Manifest.RelativePath, Serialize(manifest));
}
