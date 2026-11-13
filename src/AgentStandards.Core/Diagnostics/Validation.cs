using System.Text.Json;
using System.Text.RegularExpressions;
using AgentStandards.Core.Adapters;
using AgentStandards.Core.Installation;
using AgentStandards.Core.IO;
using AgentStandards.Core.Profiles;
using AgentStandards.Core.Rendering;
using AgentStandards.Core.Templates;

namespace AgentStandards.Core.Diagnostics;

public enum Severity
{
    Error,
    Warning
}

public sealed record ValidationIssue(Severity Severity, string Path, string Message)
{
    public override string ToString() => $"{Severity.ToString().ToLowerInvariant()}: {Path}: {Message}";
}

/// <summary>Format checks for assistant configuration files, shared by template and installation validation.</summary>
public static partial class FileValidators
{
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SkillName();

    [GeneratedRegex(@"(?<![\w`])@((?:~/|\.{1,2}/|/)?[A-Za-z0-9_][A-Za-z0-9_./\-]*)")]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"(?<!!)\[[^\]]*\]\(([^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex LinkPattern();

    public const int MaxSkillNameLength = 64;
    public const int MaxSkillDescriptionLength = 1024;

    /// <summary>Validates a file based on where it lives. Unknown locations are not checked.</summary>
    public static IEnumerable<ValidationIssue> ValidateByPath(string path, string content)
    {
        var parts = path.Split('/');
        if (path.EndsWith("/SKILL.md", StringComparison.Ordinal) && parts.Length >= 3 && parts[^3] == "skills")
            return ValidateSkill(path, content, parts[^2]);
        if (parts.Length >= 2 && parts[^2] == "agents" && path.EndsWith(".agent.md", StringComparison.Ordinal))
            return ValidateAgent(path, content, copilot: true);
        if (parts.Length >= 3 && parts[^3] == ".claude" && parts[^2] == "agents" && path.EndsWith(".md", StringComparison.Ordinal))
            return ValidateAgent(path, content, copilot: false);
        if (path.EndsWith(".instructions.md", StringComparison.Ordinal))
            return ValidateInstructions(path, content);
        if (parts.Length >= 3 && parts[^3] == ".claude" && parts[^2] == "rules")
            return ValidateClaudeRule(path, content);
        if (path.EndsWith(".json", StringComparison.Ordinal))
            return ValidateJson(path, content);
        return [];
    }

    public static IEnumerable<ValidationIssue> ValidateSkill(string path, string content, string directoryName)
    {
        var fm = Frontmatter.Parse(content);
        if (!fm.IsPresent)
        {
            yield return new(Severity.Error, path, fm.Error ?? "SKILL.md must start with YAML frontmatter");
            yield break;
        }
        if (fm.Error is not null)
            yield return new(Severity.Error, path, fm.Error);

        var name = fm.GetString("name");
        if (string.IsNullOrEmpty(name))
            yield return new(Severity.Error, path, "frontmatter 'name' is required");
        else
        {
            if (!SkillName().IsMatch(name) || name.Length > MaxSkillNameLength)
                yield return new(Severity.Error, path, $"skill name '{name}' must be lowercase letters, digits and single hyphens, at most {MaxSkillNameLength} characters");
            if (name != directoryName)
                yield return new(Severity.Error, path, $"skill name '{name}' must match its directory '{directoryName}'");
        }

        var description = fm.GetString("description");
        if (string.IsNullOrWhiteSpace(description))
            yield return new(Severity.Error, path, "frontmatter 'description' is required (what the skill does and when to use it)");
        else if (description.Length > MaxSkillDescriptionLength)
            yield return new(Severity.Error, path, $"description exceeds {MaxSkillDescriptionLength} characters");
    }

    public static IEnumerable<ValidationIssue> ValidateAgent(string path, string content, bool copilot)
    {
        var fm = Frontmatter.Parse(content);
        if (!fm.IsPresent)
        {
            yield return new(Severity.Error, path, fm.Error ?? "agent file must start with YAML frontmatter");
            yield break;
        }
        if (fm.Error is not null)
            yield return new(Severity.Error, path, fm.Error);
        if (string.IsNullOrWhiteSpace(fm.GetString("description")))
            yield return new(Severity.Error, path, "frontmatter 'description' is required");
        var name = fm.GetString("name");
        if (!copilot && string.IsNullOrWhiteSpace(name))
            yield return new(Severity.Error, path, "frontmatter 'name' is required for Claude Code subagents");
        if (name is not null && name.Contains(':'))
            yield return new(Severity.Error, path, "agent name may not contain ':'");
        if (string.IsNullOrWhiteSpace(fm.Body))
            yield return new(Severity.Warning, path, "agent has no instructions body");
    }

    public static IEnumerable<ValidationIssue> ValidateInstructions(string path, string content)
    {
        var fm = Frontmatter.Parse(content);
        if (!fm.IsPresent || string.IsNullOrWhiteSpace(fm.GetString("applyTo")))
            yield return new(Severity.Error, path, "path-specific instructions need frontmatter with a non-empty 'applyTo' glob");
        if (fm.Error is not null)
            yield return new(Severity.Error, path, fm.Error);
    }

    public static IEnumerable<ValidationIssue> ValidateClaudeRule(string path, string content)
    {
        var fm = Frontmatter.Parse(content);
        if (fm.Error is not null)
            yield return new(Severity.Error, path, fm.Error);
        if (fm.IsPresent && fm.Has("paths") && fm.GetList("paths").Count == 0)
            yield return new(Severity.Error, path, "'paths' is present but empty");
    }

    public static IEnumerable<ValidationIssue> ValidateJson(string path, string content)
    {
        string? error = null;
        try
        {
            using var _ = JsonDocument.Parse(content, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            error = ex.Message;
        }
        if (error is not null)
            yield return new(Severity.Error, path, $"invalid JSON: {error}");
    }

    /// <summary>Checks CLAUDE.md <c>@path</c> imports outside code spans/blocks.</summary>
    public static IEnumerable<ValidationIssue> CheckImports(string root, string home, string path, string content, Severity severity)
    {
        var dir = Path.GetDirectoryName(Path.Combine(root, path))!;
        foreach (var target in ImportPattern().Matches(StripCode(content)).Select(m => m.Groups[1].Value.TrimEnd('.', ',', ';', ':', ')')))
        {
            if (!target.Contains('.') && !target.Contains('/'))
                continue; // e.g. @mentions
            var resolved = target.StartsWith("~/", StringComparison.Ordinal)
                ? Path.Combine(home, target[2..])
                : Path.GetFullPath(Path.Combine(dir, target));
            if (!File.Exists(resolved))
                yield return new(severity, path, $"import '@{target}' does not resolve to an existing file");
        }
    }

    /// <summary>Checks relative Markdown links outside code spans/blocks.</summary>
    public static IEnumerable<ValidationIssue> CheckLinks(string root, string home, string path, string content, Severity severity)
    {
        var dir = Path.GetDirectoryName(Path.Combine(root, path))!;
        foreach (var raw in LinkPattern().Matches(StripCode(content)).Select(m => m.Groups[1].Value))
        {
            if (raw.StartsWith('#') || raw.Contains("://", StringComparison.Ordinal) || raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                continue;
            var target = raw.Split('#')[0];
            if (target.Length == 0)
                continue;
            var resolved = target.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, target[2..]) : Path.GetFullPath(Path.Combine(dir, Uri.UnescapeDataString(target)));
            if (!File.Exists(resolved) && !Directory.Exists(resolved))
                yield return new(severity, path, $"link '{raw}' does not resolve");
        }
    }

    public static string StripCode(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                lines[i] = "";
                continue;
            }
            lines[i] = inFence ? "" : Regex.Replace(lines[i], "`[^`]*`", "");
        }
        return string.Join('\n', lines);
    }
}

public static class InstallationValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(string root, string home)
    {
        var issues = new List<ValidationIssue>();
        Manifest? manifest;
        try
        {
            manifest = ManifestStore.Load(root);
        }
        catch (ManifestException ex)
        {
            return [new(Severity.Error, Manifest.RelativePath, ex.Message)];
        }

        if (manifest is null)
            return [new(Severity.Error, Manifest.RelativePath, "no installation found; run 'agent-standards init'")];

        foreach (var file in manifest.Files)
        {
            string? content;
            try
            {
                content = AtomicFile.ReadTextOrNull(root, file.Path);
            }
            catch (UnsafePathException ex)
            {
                issues.Add(new(Severity.Error, file.Path, ex.Message));
                continue;
            }

            if (content is null)
            {
                issues.Add(new(Severity.Warning, file.Path, "managed file is missing (deleted locally)"));
                continue;
            }

            var state = StatusService.GetState(root, file);
            if (state == FileState.Malformed)
                issues.Add(new(Severity.Error, file.Path, "managed block markers or JSON are malformed"));
            if (state == FileState.BlockMissing)
                issues.Add(new(Severity.Warning, file.Path, "managed block was removed from this file"));

            issues.AddRange(FileValidators.ValidateByPath(file.Path, content));
            if (file.Path.EndsWith(".md", StringComparison.Ordinal))
            {
                issues.AddRange(FileValidators.CheckLinks(root, home, file.Path, content, Severity.Error));
                foreach (var placeholder in Placeholders.FindUnresolved(content))
                    issues.Add(new(Severity.Error, file.Path, $"unresolved template placeholder {placeholder}"));
            }
            if (Path.GetFileName(file.Path) == "CLAUDE.md")
                issues.AddRange(FileValidators.CheckImports(root, home, file.Path, content, Severity.Error));
        }

        foreach (var conflict in StatusService.PendingConflicts(root))
            issues.Add(new(Severity.Warning, conflict, "pending conflict copy: merge it into the original file and delete the copy"));

        if (manifest.Scope == InstallScope.Project && manifest.Assistants.Contains("claude"))
        {
            var claude = AtomicFile.ReadTextOrNull(root, "CLAUDE.md");
            if (claude is not null && !FileValidators.StripCode(claude).Contains("@AGENTS.md", StringComparison.Ordinal))
                issues.Add(new(Severity.Warning, "CLAUDE.md", "does not import @AGENTS.md; Claude Code does not read AGENTS.md when CLAUDE.md exists"));
        }

        return issues;
    }
}

public static class TemplateValidator
{
    public static readonly string[] RequiredCoreFiles =
        ["core/policy.md", "core/agents.md", "core/agents-seed.md", "core/claude.md", "core/copilot.md", "core/global.md"];

    public static IReadOnlyList<ValidationIssue> Validate(TemplateSet templates)
    {
        var issues = new List<ValidationIssue>();
        foreach (var core in RequiredCoreFiles.Where(c => !templates.Files.ContainsKey(c)))
            issues.Add(new(Severity.Error, core, "required core template is missing"));

        foreach (var (path, content) in templates.Files)
        {
            if (!SafePaths.TryNormalizeRelative(path, out _))
                issues.Add(new(Severity.Error, path, "unsafe template path"));
            if (path.EndsWith(".json", StringComparison.Ordinal))
                issues.AddRange(FileValidators.ValidateJson(path, content));
        }

        foreach (var standard in templates.Standards)
        {
            var path = $"standards/{standard}.md";
            if (ContentBuilder.TitleOf(templates.Files[path], "") == "")
                issues.Add(new(Severity.Error, path, "standard must start with a '# Title' heading"));
            foreach (var issue in FileValidators.CheckLinks("/__templates__", "/__home__", path, templates.Files[path], Severity.Error))
            {
                // Links between standards are resolved against the template set rather than the file system.
                var target = issue.Message.Split('\'')[1].Split('#')[0];
                if (!templates.Files.ContainsKey(NormalizeJoin("standards", target)))
                    issues.Add(issue);
            }
        }

        foreach (var (name, skill) in templates.Skills)
            issues.AddRange(FileValidators.ValidateSkill($"skills/{name}/SKILL.md", skill.Files["SKILL.md"], name));

        foreach (var (id, agent) in templates.Agents)
        {
            var path = $"agents/{id}.md";
            if (string.IsNullOrWhiteSpace(agent.Description))
                issues.Add(new(Severity.Error, path, "frontmatter 'description' is required"));
            if (agent.Name != id)
                issues.Add(new(Severity.Error, path, $"frontmatter 'name' ({agent.Name}) must match the file name ({id})"));
            if (string.IsNullOrWhiteSpace(agent.Body))
                issues.Add(new(Severity.Error, path, "agent has no instructions body"));
        }

        foreach (var (id, rule) in templates.Rules)
        {
            var path = $"rules/{id}.md";
            if (rule.Globs.Count == 0)
                issues.Add(new(Severity.Error, path, "rule needs at least one glob in 'globs'"));
            foreach (var glob in rule.Globs.Where(g => g.Contains(',') || g.Contains('{')))
                issues.Add(new(Severity.Error, path, $"glob '{glob}' may not contain ',' or braces: Copilot's applyTo is a comma-separated list; use separate globs"));
            if (string.IsNullOrWhiteSpace(rule.Description))
                issues.Add(new(Severity.Error, path, "rule needs a 'description'"));
        }

        foreach (var profile in templates.Profiles.Values)
        {
            var path = $"profiles/{profile.Id}.json";
            if (string.IsNullOrWhiteSpace(profile.DisplayName) || string.IsNullOrWhiteSpace(profile.Description))
                issues.Add(new(Severity.Error, path, "profile needs displayName and description"));
            if (profile.SchemaVersion != 1)
                issues.Add(new(Severity.Error, path, $"unsupported profile schemaVersion {profile.SchemaVersion}"));
            foreach (var condition in (profile.Detect?.All ?? []).Concat(profile.Detect?.Any ?? []).Concat(profile.Detect?.None ?? []))
                if (string.IsNullOrWhiteSpace(condition.File))
                    issues.Add(new(Severity.Error, path, "detection condition needs a 'file' glob"));
            foreach (var (server, definition) in profile.McpServers)
                issues.AddRange(CheckMcpServer(path, server, definition));

            ResolvedProfiles resolved;
            try
            {
                resolved = ProfileResolver.Resolve(templates, [profile.Id]);
            }
            catch (TemplateException ex)
            {
                issues.Add(new(Severity.Error, path, ex.Message));
                continue;
            }

            if (issues.Any(i => i.Severity == Severity.Error && RequiredCoreFiles.Contains(i.Path)))
                continue;

            foreach (var scope in new[] { InstallScope.Project, InstallScope.Global })
            {
                var ctx = new RenderContext { Templates = templates, Profiles = resolved, Scope = scope, Assistants = AdapterRegistry.DefaultIds, IncludeMcp = scope == InstallScope.Project };
                var rendered = ContentBuilder.RenderShared(ctx).Concat(AdapterRegistry.All.SelectMany(a => a.Render(ctx))).ToList();
                foreach (var file in rendered)
                {
                    var label = $"{path} → {scope.ToString().ToLowerInvariant()}:{file.Path}";
                    foreach (var placeholder in Placeholders.FindUnresolved(file.Content))
                        issues.Add(new(Severity.Error, label, $"unresolved template placeholder {placeholder}"));
                    issues.AddRange(FileValidators.ValidateByPath(file.Path, file.Content).Select(i => i with { Path = label }));
                }
                var duplicates = rendered.GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1);
                foreach (var dup in duplicates)
                    issues.Add(new(Severity.Error, path, $"multiple templates render to '{dup.Key}'"));
            }
        }

        return issues.DistinctBy(i => (i.Severity, i.Path, i.Message)).ToList();
    }

    private static IEnumerable<ValidationIssue> CheckMcpServer(string path, string server, JsonElement definition)
    {
        if (definition.ValueKind != JsonValueKind.Object)
        {
            yield return new(Severity.Error, path, $"MCP server '{server}' must be an object");
            yield break;
        }
        foreach (var section in new[] { "env", "headers" })
        {
            if (!definition.TryGetProperty(section, out var values) || values.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var value in values.EnumerateObject())
                if (value.Value.ValueKind == JsonValueKind.String && !value.Value.GetString()!.Contains("${", StringComparison.Ordinal))
                    yield return new(Severity.Error, path, $"MCP server '{server}' {section}.{value.Name} must reference an environment variable (${{VAR}}), not a literal value");
        }
        if (definition.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String &&
            (Path.IsPathRooted(command.GetString()!) || command.GetString()!.Contains('\\')))
            yield return new(Severity.Error, path, $"MCP server '{server}' command must not be a machine-specific absolute path");
    }

    private static string NormalizeJoin(string folder, string relative)
    {
        var parts = new List<string>(folder.Split('/'));
        foreach (var part in relative.Split('/'))
        {
            if (part is "." or "") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(part);
        }
        return string.Join('/', parts);
    }
}
