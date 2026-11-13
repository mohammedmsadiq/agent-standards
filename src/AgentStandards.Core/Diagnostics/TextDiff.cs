using System.Text;

namespace AgentStandards.Core.Diagnostics;

/// <summary>Small LCS-based unified diff for previewing Markdown/JSON changes.</summary>
public static class TextDiff
{
    private const int MaxCells = 4_000_000;

    public static string Unified(string? before, string? after, string path, int context = 3)
    {
        var a = Split(before);
        var b = Split(after);
        var header = $"--- {(before is null ? "/dev/null" : "a/" + path)}\n+++ {(after is null ? "/dev/null" : "b/" + path)}\n";
        if ((long)a.Length * b.Length > MaxCells)
            return header + "@@ file too large to diff; showing summary @@\n" + $"-{a.Length} lines\n+{b.Length} lines\n";

        var ops = Compute(a, b);
        if (ops.All(o => o.Kind == ' '))
            return "";

        var sb = new StringBuilder(header);
        var i = 0;
        while (i < ops.Count)
        {
            if (ops[i].Kind == ' ') { i++; continue; }
            var start = Math.Max(0, i - context);
            var end = i;
            var lastChange = i;
            while (end < ops.Count && (ops[end].Kind != ' ' || end - lastChange <= context * 2))
            {
                if (ops[end].Kind != ' ') lastChange = end;
                end++;
            }
            end = Math.Min(ops.Count, lastChange + context + 1);

            var aStart = ops[start].A;
            var bStart = ops[start].B;
            var aCount = ops.Skip(start).Take(end - start).Count(o => o.Kind != '+');
            var bCount = ops.Skip(start).Take(end - start).Count(o => o.Kind != '-');
            sb.Append($"@@ -{aStart + (aCount > 0 ? 1 : 0)},{aCount} +{bStart + (bCount > 0 ? 1 : 0)},{bCount} @@\n");
            for (var k = start; k < end; k++)
                sb.Append(ops[k].Kind).Append(ops[k].Text).Append('\n');
            i = end;
        }
        return sb.ToString();
    }

    private static string[] Split(string? text) =>
        text is null ? [] : text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');

    private readonly record struct Op(char Kind, string Text, int A, int B);

    private static List<Op> Compute(string[] a, string[] b)
    {
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var ops = new List<Op>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y]) { ops.Add(new Op(' ', a[x], x, y)); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) { ops.Add(new Op('-', a[x], x, y)); x++; }
            else { ops.Add(new Op('+', b[y], x, y)); y++; }
        }
        while (x < a.Length) { ops.Add(new Op('-', a[x], x, y)); x++; }
        while (y < b.Length) { ops.Add(new Op('+', b[y], x, y)); y++; }
        return ops;
    }
}
