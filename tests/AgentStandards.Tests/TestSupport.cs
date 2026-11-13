using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AgentStandards.Core;
using AgentStandards.Tool;

namespace AgentStandards.Tests;

public sealed class TempDir : IDisposable
{
    public TempDir(string? name = null)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agent-standards-tests", (name ?? "t") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
        // Resolve /tmp → /private/tmp style links so path comparisons are stable.
        Path = new DirectoryInfo(Path).ResolveLinkTarget(true)?.FullName ?? Path;
    }

    public string Path { get; }

    public string Full(string relative) => System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public string Write(string relative, string content)
    {
        var full = Full(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public string Read(string relative) => File.ReadAllText(Full(relative));
    public bool Exists(string relative) => File.Exists(Full(relative));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A temporary repository + home directory and an in-process CLI.</summary>
public sealed class Harness : IDisposable
{
    public TempDir Repo { get; } = new("repo");
    public TempDir Home { get; } = new("home");

    public ToolEnvironment Env => new()
    {
        HomeDirectory = Home.Path,
        ToolVersion = "0.0.0-test",
        BundledTemplatesRoot = Path.Combine(AppContext.BaseDirectory, "templates"),
        IsInteractive = false
    };

    public StandardsEngine Engine => new(Env);

    public async Task<CliResult> Run(params string[] args)
    {
        var all = args.Contains("--global") || args.Contains("--project") ? args : [.. args, "--project", Repo.Path];
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await new Cli(Env, stdout, stderr, new StringReader("")).RunAsync(all);
        return new CliResult(code, stdout.ToString(), stderr.ToString());
    }

    public void Dispose()
    {
        Repo.Dispose();
        Home.Dispose();
    }
}

public sealed record CliResult(int Code, string Out, string Err)
{
    public JsonNode Json => JsonNode.Parse(Out)!;
    public override string ToString() => $"exit {Code}\nstdout:\n{Out}\nstderr:\n{Err}";
}

public static class Fixtures
{
    public static string BundledTemplates => Path.Combine(AppContext.BaseDirectory, "templates", "agent-standards");

    /// <summary>Copies the bundled templates to <paramref name="destination"/>, optionally setting a catalog version.</summary>
    public static string CopyTemplates(string destination, string? version = null)
    {
        foreach (var file in Directory.EnumerateFiles(BundledTemplates, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(BundledTemplates, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        if (version is not null)
        {
            var catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "catalog.json")))!;
            catalog["version"] = version;
            File.WriteAllText(Path.Combine(destination, "catalog.json"), catalog.ToJsonString());
        }
        return destination;
    }

    /// <summary>Builds a minimal but valid .nupkg containing a template tree under agent-standards/.</summary>
    public static string BuildPackage(string folder, string id, string version, string templatesDir, Action<ZipArchive>? extra = null)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{id}.{version}.nupkg");
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        AddText(zip, $"{id}.nuspec",
            $"""<?xml version="1.0" encoding="utf-8"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{id}</id><version>{version}</version><authors>test</authors><description>test</description></metadata></package>""");
        foreach (var file in Directory.EnumerateFiles(templatesDir, "*", SearchOption.AllDirectories))
            AddText(zip, "agent-standards/" + Path.GetRelativePath(templatesDir, file).Replace('\\', '/'), File.ReadAllText(file));
        extra?.Invoke(zip);
        return path;
    }

    public static void AddText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>Path, size, timestamp and hash of every file and directory under a root.</summary>
    public static SortedDictionary<string, string> Snapshot(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(root))
            return result;
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, entry.FullName);
            // The lock file cannot be read while another handle holds it; record its presence only.
            result[rel] = entry is FileInfo { Name: "lock" } lockFile && lockFile.Directory?.Name == ".agent-standards"
                ? "lock"
                : entry is FileInfo f
                    ? $"{f.Length}:{f.LastWriteTimeUtc.Ticks}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.FullName)))}"
                    : "dir";
        }
        return result;
    }

    public static bool TryCreateSymlink(string link, string target, bool directory)
    {
        try
        {
            if (directory) Directory.CreateSymbolicLink(link, target);
            else File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false; // e.g. Windows without Developer Mode
        }
    }
}
