using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentStandards.Core.Templates;

public sealed class TemplateCatalog
{
    public const int SupportedSchemaVersion = 1;

    public int SchemaVersion { get; set; }
    public string? Description { get; set; }
    /// <summary>Optional version used when templates are loaded from a plain directory.</summary>
    public string? Version { get; set; }
}

public sealed class ProfileDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Extends { get; set; } = [];
    public DetectionRules? Detect { get; set; }
    public List<string> Standards { get; set; } = [];
    public List<string> Rules { get; set; } = [];
    public List<string> Skills { get; set; } = [];
    public List<string> Agents { get; set; } = [];
    public Dictionary<string, JsonElement> McpServers { get; set; } = [];
}

public sealed class DetectionRules
{
    /// <summary>Every condition must match.</summary>
    public List<DetectCondition> All { get; set; } = [];
    /// <summary>At least one condition must match (ignored when empty).</summary>
    public List<DetectCondition> Any { get; set; } = [];
    /// <summary>No condition may match.</summary>
    public List<DetectCondition> None { get; set; } = [];
}

public sealed class DetectCondition
{
    /// <summary>Glob relative to the repository root, e.g. <c>**/*.csproj</c>.</summary>
    public string File { get; set; } = "";
    /// <summary>Optional case-insensitive text that a matching file must contain.</summary>
    public string? Contains { get; set; }

    public override string ToString() => Contains is null ? File : $"{File} containing '{Contains}'";
}

public enum AgentAccess
{
    ReadOnly,
    ReadWrite
}

public sealed record AgentTemplate(string Id, string Name, string Description, AgentAccess Access, string Body);

public sealed record RuleTemplate(string Id, string Description, IReadOnlyList<string> Globs, string Body);

public sealed record SkillTemplate(string Name, string Description, IReadOnlyDictionary<string, string> Files);

internal static class TemplateJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };
}
