using AgentStandards.Core.Profiles;
using AgentStandards.Core.Templates;

namespace AgentStandards.Core.Rendering;

public enum InstallScope
{
    Project,
    Global
}

public enum ManagedMode
{
    /// <summary>The whole file is owned by the toolkit.</summary>
    Whole,
    /// <summary>Only a marked block is owned; content outside the markers belongs to the user.</summary>
    Block,
    /// <summary>JSON servers are added to <c>mcpServers</c> if absent; existing entries are never changed.</summary>
    JsonMerge
}

/// <param name="Path">Forward-slash path relative to the scope root.</param>
/// <param name="Content">Whole: file content. Block: block body. JsonMerge: JSON object of servers.</param>
/// <param name="Seed">Block only: user-owned content written after the block when the file is first created.</param>
public sealed record RenderedFile(string Path, ManagedMode Mode, string Content, string Source, string? Seed = null);

public sealed class RenderContext
{
    public required TemplateSet Templates { get; init; }
    public required ResolvedProfiles Profiles { get; init; }
    public required InstallScope Scope { get; init; }
    public required IReadOnlyList<string> Assistants { get; init; }
    public bool IncludeMcp { get; init; }

    public const string StateFolder = ".agent-standards";
    public const string StandardsFolder = ".agent-standards/standards";

    /// <summary>
    /// How a generated file should link to an installed standard. Project scope uses relative links;
    /// global scope uses <c>~/</c> paths because global files live in assistant-specific folders.
    /// </summary>
    public string LinkToStandard(string fromFile, string standard) => Scope == InstallScope.Global
        ? $"~/{StandardsFolder}/{standard}.md"
        : RelativeLink(fromFile, $"{StandardsFolder}/{standard}.md");

    public static string RelativeLink(string fromFile, string toFile)
    {
        var depth = fromFile.Count(c => c == '/');
        return string.Concat(Enumerable.Repeat("../", depth)) + toFile;
    }
}
