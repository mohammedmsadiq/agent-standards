using System.Text;
using System.Text.RegularExpressions;

namespace AgentStandards.Core.IO;

/// <summary>Minimal glob matcher supporting <c>**</c>, <c>*</c>, <c>?</c> and <c>{a,b}</c> on forward-slash paths.</summary>
public static class Glob
{
    private static readonly Dictionary<string, Regex> Cache = new(StringComparer.Ordinal);
    private static readonly Lock CacheLock = new();

    public static bool IsMatch(string pattern, string relativePath)
    {
        Regex regex;
        lock (CacheLock)
        {
            if (!Cache.TryGetValue(pattern, out regex!))
            {
                regex = new Regex(ToRegex(pattern), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                Cache[pattern] = regex;
            }
        }
        return regex.IsMatch(relativePath.Replace('\\', '/'));
    }

    internal static string ToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        var p = pattern.Replace('\\', '/').TrimStart('/');
        for (var i = 0; i < p.Length; i++)
        {
            var c = p[i];
            switch (c)
            {
                case '*' when i + 1 < p.Length && p[i + 1] == '*':
                    i++;
                    if (i + 1 < p.Length && p[i + 1] == '/')
                    {
                        i++;
                        sb.Append("(?:.*/)?");
                    }
                    else
                    {
                        sb.Append(".*");
                    }
                    break;
                case '*':
                    sb.Append("[^/]*");
                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                case '{':
                    var close = p.IndexOf('}', i);
                    if (close < 0)
                    {
                        sb.Append(@"\{");
                        break;
                    }
                    var options = p[(i + 1)..close].Split(',').Select(Regex.Escape);
                    sb.Append("(?:").Append(string.Join('|', options)).Append(')');
                    i = close;
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        return sb.Append('$').ToString();
    }
}
