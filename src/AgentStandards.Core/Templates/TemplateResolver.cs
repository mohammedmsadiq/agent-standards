using System.Text.Json;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace AgentStandards.Core.Templates;

public sealed class TemplateRequest
{
    public string PackageId { get; init; } = TemplateSet.DefaultPackageId;
    /// <summary>null = bundled (or latest for custom package IDs); "latest"; or an exact version.</summary>
    public string? Version { get; init; }
    /// <summary>Use a plain directory of templates instead of a package.</summary>
    public string? TemplatesDirectory { get; init; }
    /// <summary>Explicit package sources (folder paths or feed URLs). When empty, nuget.config sources are used.</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];
    public bool Offline { get; init; }
    public bool IncludePrerelease { get; init; }
    /// <summary>Directory whose nuget.config hierarchy supplies sources (usually the project root).</summary>
    public string? ConfigRoot { get; init; }
    /// <summary>When false (dry-run), downloaded packages are kept in memory only.</summary>
    public bool AllowCacheWrite { get; init; } = true;
}

/// <summary>
/// Acquires a template set. Resolution order: explicit directory → bundled templates → tool package cache →
/// NuGet global packages folder → package sources (nuget.config or --source). Packages are read as zip archives
/// in memory with path validation; nothing is extracted into the target repository.
/// </summary>
public sealed class TemplateResolver(ToolEnvironment environment)
{
    public const string Latest = "latest";

    public TemplateSet LoadBundled()
    {
        var bundlePath = Path.Combine(environment.BundledTemplatesRoot, "bundle.json");
        if (!File.Exists(bundlePath))
            throw new TemplateException($"Bundled templates are missing ({bundlePath}). Reinstall the tool.");
        var bundle = JsonSerializer.Deserialize<BundleInfo>(File.ReadAllText(bundlePath), TemplateJson.Options)
                     ?? throw new TemplateException("bundle.json is empty.");
        var files = TemplateLoader.ReadDirectory(Path.Combine(environment.BundledTemplatesRoot, TemplateLoader.PackageFolder));
        return TemplateSet.Create(files, bundle.PackageId, bundle.Version, "bundled");
    }

    public string BundledVersion
    {
        get
        {
            var bundlePath = Path.Combine(environment.BundledTemplatesRoot, "bundle.json");
            return File.Exists(bundlePath)
                ? JsonSerializer.Deserialize<BundleInfo>(File.ReadAllText(bundlePath), TemplateJson.Options)?.Version ?? "unknown"
                : "unknown";
        }
    }

    public async Task<TemplateSet> ResolveAsync(TemplateRequest request, CancellationToken cancellationToken = default)
    {
        if (request.TemplatesDirectory is not null)
        {
            var files = TemplateLoader.ReadDirectory(request.TemplatesDirectory);
            return TemplateSet.Create(files, request.PackageId, null, Path.GetFullPath(request.TemplatesDirectory));
        }

        var isDefaultPackage = string.Equals(request.PackageId, TemplateSet.DefaultPackageId, StringComparison.OrdinalIgnoreCase);
        var bundledVersion = isDefaultPackage ? TryParse(BundledVersion) : null;

        if (request.Version is null && isDefaultPackage)
            return LoadBundled();

        NuGetVersion target;
        if (request.Version is null || string.Equals(request.Version, Latest, StringComparison.OrdinalIgnoreCase))
        {
            var latest = await FindLatestAsync(request, cancellationToken);
            target = latest is not null && (bundledVersion is null || latest > bundledVersion)
                ? latest
                : bundledVersion ?? throw new TemplateException($"No versions of '{request.PackageId}' were found in the configured sources.");
        }
        else
        {
            target = TryParse(request.Version) ?? throw new TemplateException($"'{request.Version}' is not a valid version.");
        }

        if (bundledVersion is not null && target == bundledVersion)
            return LoadBundled();

        return await LoadVersionAsync(request, target, cancellationToken);
    }

    /// <summary>Loads an exact package version, bypassing the bundled shortcut (used by tests and validation).</summary>
    internal async Task<TemplateSet> LoadVersionAsync(TemplateRequest request, NuGetVersion version, CancellationToken ct)
    {
        var id = request.PackageId;
        var cachePath = CachePath(id, version);
        if (File.Exists(cachePath))
        {
            await using var stream = File.OpenRead(cachePath);
            return FromPackage(stream, id, version, cachePath);
        }

        var globalFolder = GetGlobalPackagesFolder(request);
        if (globalFolder is not null)
        {
            var dir = Path.Combine(globalFolder, id.ToLowerInvariant(), version.ToNormalizedString().ToLowerInvariant());
            var nupkg = Path.Combine(dir, $"{id.ToLowerInvariant()}.{version.ToNormalizedString().ToLowerInvariant()}.nupkg");
            if (File.Exists(nupkg) && File.Exists(Path.Combine(dir, ".nupkg.metadata")))
            {
                await using var stream = File.OpenRead(nupkg);
                return FromPackage(stream, id, version, nupkg);
            }
        }

        var sources = GetSources(request).ToList();
        if (sources.Count == 0)
            throw new TemplateException(
                request.Offline
                    ? $"{id} {version} is not available offline. Looked in the tool cache ({cachePath}) and the NuGet global packages folder. Use --source <folder> containing the .nupkg, or run without --offline."
                    : $"{id} {version} is not cached and no package sources are configured. Use --source <folder-or-feed>.");

        var errors = new List<string>();
        using var cache = new SourceCacheContext { NoCache = true, DirectDownload = true };
        foreach (var source in sources)
        {
            try
            {
                var repository = Repository.Factory.GetCoreV3(source);
                var finder = await repository.GetResourceAsync<FindPackageByIdResource>(ct);
                if (finder is null)
                    continue;
                using var buffer = new MemoryStream();
                if (!await finder.CopyNupkgToStreamAsync(id, version, buffer, cache, NullLogger.Instance, ct))
                    continue;

                buffer.Position = 0;
                var set = FromPackage(buffer, id, version, source.Source);
                if (request.AllowCacheWrite)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    var temp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await File.WriteAllBytesAsync(temp, buffer.ToArray(), ct);
                    File.Move(temp, cachePath, overwrite: true);
                }
                return set;
            }
            catch (Exception ex) when (ex is not TemplateException and not OperationCanceledException)
            {
                errors.Add($"{source.Source}: {ex.Message}");
            }
        }

        var detail = errors.Count > 0 ? " Errors: " + string.Join("; ", errors) : "";
        throw new TemplateException($"Package {id} {version} was not found in any configured source.{detail}");
    }

    private async Task<NuGetVersion?> FindLatestAsync(TemplateRequest request, CancellationToken ct)
    {
        var candidates = new List<NuGetVersion>();

        var cacheDir = Path.Combine(environment.PackageCacheDirectory, request.PackageId.ToLowerInvariant());
        if (Directory.Exists(cacheDir))
            candidates.AddRange(Directory.GetDirectories(cacheDir).Select(d => TryParse(Path.GetFileName(d))).OfType<NuGetVersion>());

        var globalFolder = GetGlobalPackagesFolder(request);
        if (globalFolder is not null)
        {
            var dir = Path.Combine(globalFolder, request.PackageId.ToLowerInvariant());
            if (Directory.Exists(dir))
                candidates.AddRange(Directory.GetDirectories(dir).Select(d => TryParse(Path.GetFileName(d))).OfType<NuGetVersion>());
        }

        {
            using var cache = new SourceCacheContext { NoCache = true, DirectDownload = true };
            foreach (var source in GetSources(request))
            {
                try
                {
                    var repository = Repository.Factory.GetCoreV3(source);
                    var finder = await repository.GetResourceAsync<FindPackageByIdResource>(ct);
                    if (finder is null)
                        continue;
                    candidates.AddRange(await finder.GetAllVersionsAsync(request.PackageId, cache, NullLogger.Instance, ct));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // An unreachable source is not fatal while other sources or the bundled templates remain.
                }
            }
        }

        return candidates.Where(v => request.IncludePrerelease || !v.IsPrerelease).DefaultIfEmpty().Max();
    }

    private TemplateSet FromPackage(Stream stream, string id, NuGetVersion version, string source)
    {
        NuGet.Packaging.Core.PackageIdentity identity;
        try
        {
            using var reader = new PackageArchiveReader(stream, leaveStreamOpen: true);
            identity = reader.GetIdentity();
        }
        catch (Exception ex) when (ex is not TemplateException and not OperationCanceledException)
        {
            throw new TemplateException($"Package at {source} is not a valid NuGet package: {ex.Message}", ex);
        }
        if (!string.Equals(identity.Id, id, StringComparison.OrdinalIgnoreCase) || identity.Version != version)
            throw new TemplateException($"Package at {source} identifies as {identity.Id} {identity.Version}, expected {id} {version}.");

        stream.Position = 0;
        var files = TemplateLoader.ReadPackage(stream);
        return TemplateSet.Create(files, id, version.ToNormalizedString(), source);
    }

    private string CachePath(string id, NuGetVersion version) =>
        Path.Combine(environment.PackageCacheDirectory, id.ToLowerInvariant(), version.ToNormalizedString().ToLowerInvariant(),
            $"{id.ToLowerInvariant()}.{version.ToNormalizedString().ToLowerInvariant()}.nupkg");

    private static string? GetGlobalPackagesFolder(TemplateRequest request)
    {
        try
        {
            return SettingsUtility.GetGlobalPackagesFolder(Settings.LoadDefaultSettings(request.ConfigRoot ?? Environment.CurrentDirectory));
        }
        catch (Exception ex) when (ex is NuGetConfigurationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Explicit --source values, else enabled nuget.config sources. Offline mode keeps local folder sources only.</summary>
    private static IEnumerable<PackageSource> GetSources(TemplateRequest request)
    {
        IEnumerable<PackageSource> sources;
        if (request.Sources.Count > 0)
        {
            sources = request.Sources.Select((s, i) =>
                Directory.Exists(s) ? new PackageSource(Path.GetFullPath(s), $"cli-{i}") : new PackageSource(s, $"cli-{i}"));
        }
        else
        {
            var settings = Settings.LoadDefaultSettings(request.ConfigRoot ?? Environment.CurrentDirectory);
            sources = new PackageSourceProvider(settings).LoadPackageSources().Where(s => s.IsEnabled).ToList();
        }

        return request.Offline ? sources.Where(s => s.IsLocal).ToList() : sources;
    }

    private static NuGetVersion? TryParse(string? value) =>
        NuGetVersion.TryParse(value, out var v) ? v : null;

    private sealed class BundleInfo
    {
        public string PackageId { get; set; } = TemplateSet.DefaultPackageId;
        public string Version { get; set; } = "";
    }
}
