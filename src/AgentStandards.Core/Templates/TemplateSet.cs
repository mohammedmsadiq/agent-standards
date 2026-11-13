using System.Text.Json;

namespace AgentStandards.Core.Templates;

/// <summary>An immutable, in-memory set of standards templates (profiles, standards, rules, skills, agents).</summary>
public sealed class TemplateSet
{
    public const string DefaultPackageId = "AgentStandards.Templates";

    private TemplateSet(string packageId, string version, string source, IReadOnlyDictionary<string, string> files)
    {
        PackageId = packageId;
        Version = version;
        Source = source;
        Files = files;
    }

    public string PackageId { get; }
    public string Version { get; }
    /// <summary>Human-readable origin, e.g. "bundled", a cache path or a feed URL.</summary>
    public string Source { get; }
    public IReadOnlyDictionary<string, string> Files { get; }
    public TemplateCatalog Catalog { get; private set; } = new();
    public IReadOnlyDictionary<string, ProfileDefinition> Profiles { get; private set; } = new Dictionary<string, ProfileDefinition>();
    public IReadOnlyDictionary<string, SkillTemplate> Skills { get; private set; } = new Dictionary<string, SkillTemplate>();
    public IReadOnlyDictionary<string, AgentTemplate> Agents { get; private set; } = new Dictionary<string, AgentTemplate>();
    public IReadOnlyDictionary<string, RuleTemplate> Rules { get; private set; } = new Dictionary<string, RuleTemplate>();
    public IReadOnlyList<string> Standards { get; private set; } = [];

    /// <summary>Non-fatal problems found while parsing (reported by <c>validate</c>).</summary>
    public IReadOnlyList<string> ParseWarnings { get; private set; } = [];

    public string GetFile(string path) =>
        Files.TryGetValue(path, out var content) ? content : throw new TemplateException($"Template file '{path}' is missing.");

    public static TemplateSet Create(IReadOnlyDictionary<string, string> files, string packageId, string? version, string source)
    {
        if (!files.TryGetValue("catalog.json", out var catalogJson))
            throw new TemplateException("Template source has no catalog.json; it is not an agent-standards template set.");

        TemplateCatalog catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<TemplateCatalog>(catalogJson, TemplateJson.Options) ?? new TemplateCatalog();
        }
        catch (JsonException ex)
        {
            throw new TemplateException($"catalog.json is not valid JSON: {ex.Message}", ex);
        }

        if (catalog.SchemaVersion != TemplateCatalog.SupportedSchemaVersion)
            throw new TemplateException(
                $"Template schema version {catalog.SchemaVersion} is not supported by this tool (supports {TemplateCatalog.SupportedSchemaVersion}). Update the agent-standards tool.");

        var set = new TemplateSet(packageId, version ?? catalog.Version ?? "local", source, files) { Catalog = catalog };
        var warnings = new List<string>();

        var profiles = new Dictionary<string, ProfileDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, content) in files.Where(f => f.Key.StartsWith("profiles/", StringComparison.Ordinal) && f.Key.EndsWith(".json", StringComparison.Ordinal)))
        {
            ProfileDefinition profile;
            try
            {
                profile = JsonSerializer.Deserialize<ProfileDefinition>(content, TemplateJson.Options)
                          ?? throw new TemplateException($"{path} is empty.");
            }
            catch (JsonException ex)
            {
                throw new TemplateException($"{path} is not valid JSON: {ex.Message}", ex);
            }

            var expectedId = Path.GetFileNameWithoutExtension(path);
            if (!string.Equals(profile.Id, expectedId, StringComparison.Ordinal))
                throw new TemplateException($"{path}: profile id '{profile.Id}' must match the file name '{expectedId}'.");
            profiles[profile.Id] = profile;
        }

        var skills = new Dictionary<string, SkillTemplate>(StringComparer.Ordinal);
        foreach (var skillFile in files.Keys.Where(k => k.StartsWith("skills/", StringComparison.Ordinal) && k.EndsWith("/SKILL.md", StringComparison.Ordinal)))
        {
            var parts = skillFile.Split('/');
            if (parts.Length != 3)
                continue;
            var name = parts[1];
            var fm = Frontmatter.Parse(files[skillFile]);
            var prefix = $"skills/{name}/";
            var skillFiles = files.Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(f => f.Key[prefix.Length..], f => f.Value, StringComparer.Ordinal);
            skills[name] = new SkillTemplate(name, fm.GetString("description") ?? "", skillFiles);
        }

        var agents = new Dictionary<string, AgentTemplate>(StringComparer.Ordinal);
        foreach (var (path, content) in files.Where(f => IsDirectChild(f.Key, "agents/", ".md")))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            var fm = Frontmatter.Parse(content);
            var accessText = fm.GetString("access") ?? "read-only";
            var access = accessText switch
            {
                "read-only" => AgentAccess.ReadOnly,
                "read-write" => AgentAccess.ReadWrite,
                _ => throw new TemplateException($"{path}: access must be 'read-only' or 'read-write', not '{accessText}'.")
            };
            agents[id] = new AgentTemplate(id, fm.GetString("name") ?? id, fm.GetString("description") ?? "", access, fm.Body);
        }

        var rules = new Dictionary<string, RuleTemplate>(StringComparer.Ordinal);
        foreach (var (path, content) in files.Where(f => IsDirectChild(f.Key, "rules/", ".md")))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            var fm = Frontmatter.Parse(content);
            rules[id] = new RuleTemplate(id, fm.GetString("description") ?? "", fm.GetList("globs"), fm.Body);
        }

        set.Profiles = profiles;
        set.Skills = skills;
        set.Agents = agents;
        set.Rules = rules;
        set.Standards = files.Keys.Where(k => IsDirectChild(k, "standards/", ".md"))
            .Select(k => k["standards/".Length..^3]).Order(StringComparer.Ordinal).ToList();
        set.ParseWarnings = warnings;
        return set;
    }

    private static bool IsDirectChild(string path, string folder, string extension) =>
        path.StartsWith(folder, StringComparison.Ordinal) && path.EndsWith(extension, StringComparison.Ordinal) &&
        path.IndexOf('/', folder.Length) < 0;
}
