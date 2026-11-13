namespace AgentStandards.Core.IO;

public sealed class UnsafePathException(string path, string reason)
    : Exception($"Refusing unsafe path '{path}': {reason}.")
{
    public string PathValue { get; } = path;
    public string Reason { get; } = reason;
}
