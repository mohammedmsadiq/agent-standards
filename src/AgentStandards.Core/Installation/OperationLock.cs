using AgentStandards.Core.IO;

namespace AgentStandards.Core.Installation;

/// <summary>
/// Exclusive, cross-process lock for one installation (project root or home directory), held through an open
/// <c>FileShare.None</c> handle on <c>.agent-standards/lock</c>. Windows enforces this as a sharing violation; on
/// Linux and macOS .NET takes an exclusive <c>flock</c>. The operating system releases the lock when the handle
/// closes or the process dies, so a crashed process never leaves a stale lock; the file's existence means nothing.
/// Acquisition never waits, so two processes cannot deadlock.
/// </summary>
public sealed class OperationLock : IDisposable
{
    public const string RelativePath = ".agent-standards/lock";

    private readonly FileStream _stream;

    private OperationLock(string root, FileStream stream)
    {
        Root = root;
        _stream = stream;
    }

    public string Root { get; }

    public static OperationLock Acquire(string root, string operation)
    {
        var fullRoot = Path.GetFullPath(root);
        var full = SafePaths.ResolveSafe(fullRoot, RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        SafePaths.EnsureNoLinks(fullRoot, full);

        FileStream stream;
        try
        {
            stream = new FileStream(full, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            var holder = ReadHolder(full);
            throw new OperationInProgressException(
                $"Another agent-standards operation is running on '{fullRoot}'{(holder is null ? "" : $" ({holder})")}. " +
                "Wait for it to finish, then retry. A lock left by a process that has exited is released automatically.");
        }

        try
        {
            stream.SetLength(0);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write($"pid {Environment.ProcessId}, {operation}, started {DateTimeOffset.UtcNow:O}");
        }
        catch (IOException)
        {
            // Holder details are informational only.
        }

        return new OperationLock(fullRoot, stream);
    }

    /// <summary>True when another handle currently holds the lock. Never creates the lock file.</summary>
    public static bool IsHeld(string root)
    {
        var full = SafePaths.Resolve(root, RelativePath);
        if (!File.Exists(full) || SafePaths.IsLink(full))
            return false;
        try
        {
            using var probe = new FileStream(full, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal void EnsureFor(string root)
    {
        if (!string.Equals(Root, Path.GetFullPath(root), SafePaths.PathComparison))
            throw new InvalidOperationException("Internal error: the operation lock belongs to a different root.");
    }

    private static string? ReadHolder(string full)
    {
        try
        {
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd().Trim();
            return text.Length is > 0 and < 300 ? text : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // Windows denies reading a file locked with FileShare.None.
        }
    }

    public void Dispose() => _stream.Dispose();
}
