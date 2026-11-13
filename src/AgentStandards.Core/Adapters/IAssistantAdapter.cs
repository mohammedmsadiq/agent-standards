using AgentStandards.Core.Rendering;

namespace AgentStandards.Core.Adapters;

/// <summary>
/// Translates the canonical, assistant-neutral templates into one assistant's native file layout.
/// To support a new assistant, implement this interface and register it in <see cref="AdapterRegistry"/>.
/// </summary>
public interface IAssistantAdapter
{
    /// <summary>Stable identifier used on the command line and in manifests, e.g. <c>claude</c>.</summary>
    string Id { get; }
    string DisplayName { get; }
    /// <summary>One-line description of the files this adapter manages.</summary>
    string Summary { get; }
    IEnumerable<RenderedFile> Render(RenderContext context);
}

public static class AdapterRegistry
{
    public static IReadOnlyList<IAssistantAdapter> All { get; } = [new ClaudeCodeAdapter(), new CopilotAdapter()];

    public static IReadOnlyList<string> DefaultIds => All.Select(a => a.Id).ToList();

    public static IAssistantAdapter Get(string id) =>
        All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown assistant '{id}'. Supported: {string.Join(", ", All.Select(a => a.Id))}.");
}
