using System.Security.Cryptography;
using System.Text;

namespace AgentStandards.Core.IO;

/// <summary>Line-ending-insensitive content hashing, so Git autocrlf does not register as a local modification.</summary>
public static class ContentHash
{
    public static string NormalizeNewlines(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public static string Compute(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeNewlines(content))));
}
