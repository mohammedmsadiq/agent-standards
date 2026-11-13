using AgentStandards.Core.IO;
using AgentStandards.Core.Templates;

namespace AgentStandards.Core.Profiles;

public sealed record DetectionResult(IReadOnlyList<string> Profiles, IReadOnlyDictionary<string, IReadOnlyList<string>> Evidence, bool FellBackToGeneric);

/// <summary>Detects project profiles from real repository files using each profile's declarative <c>detect</c> rules.</summary>
public static class ProjectDetector
{
    public const string FallbackProfile = "generic";
    private const int MaxDepth = 6;
    private const int MaxFiles = 20000;
    private const int MaxContentReadsPerCondition = 300;
    private const long MaxContentBytes = 512 * 1024;

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", ".vs", ".idea", ".vscode", ".agent-standards", ".claude", "node_modules", "bin", "obj",
        "packages", "artifacts", "dist", "build", "out", "target", "vendor", ".venv", "venv", "__pycache__"
    };

    public static DetectionResult Detect(string root, TemplateSet templates)
    {
        var files = EnumerateFiles(root);
        var contentCache = new Dictionary<string, string?>(StringComparer.Ordinal);
        var matched = new List<string>();
        var evidence = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in templates.Profiles.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var rules = profile.Detect;
            if (rules is null || rules.All.Count + rules.Any.Count == 0)
                continue;

            var reasons = new List<string>();
            var ok = true;
            foreach (var condition in rules.All)
            {
                var hit = FindMatch(root, files, condition, contentCache);
                if (hit is null) { ok = false; break; }
                reasons.Add($"{condition} → {hit}");
            }

            if (ok && rules.Any.Count > 0)
            {
                string? anyHit = null;
                foreach (var condition in rules.Any)
                {
                    var hit = FindMatch(root, files, condition, contentCache);
                    if (hit is null) continue;
                    anyHit = $"{condition} → {hit}";
                    break;
                }
                if (anyHit is null) ok = false; else reasons.Add(anyHit);
            }

            if (ok)
            {
                foreach (var condition in rules.None)
                {
                    if (FindMatch(root, files, condition, contentCache) is not null) { ok = false; break; }
                }
            }

            if (ok)
            {
                matched.Add(profile.Id);
                evidence[profile.Id] = reasons;
            }
        }

        // Keep the most specific profiles: drop any matched profile that another matched profile extends.
        var specific = matched.Where(m => !matched.Any(other => other != m && ProfileResolver.Extends(templates, other, m))).ToList();
        if (specific.Count == 0)
            return new DetectionResult([FallbackProfile], evidence, true);
        return new DetectionResult(specific, evidence, false);
    }

    private static string? FindMatch(string root, IReadOnlyList<string> files, DetectCondition condition, Dictionary<string, string?> cache)
    {
        var reads = 0;
        foreach (var file in files)
        {
            if (!Glob.IsMatch(condition.File, file))
                continue;
            if (condition.Contains is null)
                return file;
            if (reads++ >= MaxContentReadsPerCondition)
                return null;
            if (!cache.TryGetValue(file, out var content))
            {
                content = ReadSmall(Path.Combine(root, file));
                cache[file] = content;
            }
            if (content is not null && content.Contains(condition.Contains, StringComparison.OrdinalIgnoreCase))
                return file;
        }
        return null;
    }

    private static string? ReadSmall(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Length > MaxContentBytes ? null : File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<string> EnumerateFiles(string root)
    {
        var result = new List<string>();
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((Path.GetFullPath(root), 0));
        while (queue.Count > 0 && result.Count < MaxFiles)
        {
            var (dir, depth) = queue.Dequeue();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (entry.LinkTarget is not null)
                    continue; // never follow links during detection
                if (entry is DirectoryInfo)
                {
                    if (depth < MaxDepth && !SkippedDirectories.Contains(entry.Name))
                        queue.Enqueue((entry.FullName, depth + 1));
                }
                else
                {
                    result.Add(Path.GetRelativePath(root, entry.FullName).Replace(Path.DirectorySeparatorChar, '/'));
                    if (result.Count >= MaxFiles) break;
                }
            }
        }
        return result;
    }
}
