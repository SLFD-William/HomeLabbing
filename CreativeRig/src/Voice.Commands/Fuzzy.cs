using System.Text.RegularExpressions;

namespace Voice.Commands;

/// <summary>Matches a spoken name ("super classic crunch") to the closest real name on the device.</summary>
public static partial class Fuzzy
{
    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlnum();

    public static string Normalize(string s) => NonAlnum().Replace(s.ToLowerInvariant(), " ").Trim();

    public static string? Best(string query, IEnumerable<string> candidates, double threshold = 0.6)
    {
        var q = Normalize(query);
        if (q.Length == 0)
        {
            return null;
        }

        string? best = null;
        var bestScore = 0.0;
        foreach (var candidate in candidates)
        {
            var score = Score(q, Normalize(candidate));
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return bestScore >= threshold ? best : null;
    }

    private static double Score(string q, string c)
    {
        if (c.Length == 0)
        {
            return 0;
        }

        if (q == c)
        {
            return 1;
        }

        // A spoken query that is fully contained in a longer name ("crunch" in "01 super classic crunch").
        if (q.Length >= 3 && c.Contains(q, StringComparison.Ordinal))
        {
            return 0.85 + 0.1 * q.Length / c.Length;
        }

        var qt = q.Split(' ').ToHashSet();
        var ct = c.Split(' ').ToHashSet();
        var overlap = (double)qt.Intersect(ct).Count() / qt.Union(ct).Count();
        var edit = 1.0 - (double)Levenshtein(q, c) / Math.Max(q.Length, c.Length);
        return Math.Max(overlap, edit);
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1];
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            prev = cur;
        }

        return prev[b.Length];
    }
}

/// <summary>A failure whose message is safe and useful to say out loud.</summary>
public sealed class CommandException : Exception
{
    public CommandException(string message)
        : base(message)
    {
    }
}
