using System.Globalization;
using System.Text.RegularExpressions;

namespace SimklJellyseerrSync;

/// <summary>
/// Parses a SIMKL memo's first line into a set of seasons to request.
/// Directive only if the trimmed first line starts with a digit, or
/// First / Last / Latest / All (case-insensitive). Otherwise the default
/// (first available season) is used. Fully whitespace-insensitive.
/// </summary>
public static class SeasonSelector
{
    public static (List<int> Seasons, string Description) Select(string? memo, IReadOnlyList<int> available)
    {
        var avail = available.Where(n => n > 0).Distinct().OrderBy(n => n).ToList();
        if (avail.Count == 0) return (new List<int>(), "none (no seasons)");

        var firstLine = FirstLine(memo);
        if (!IsDirective(firstLine))
            return (FirstN(avail, 1), "default (first season)");

        var chosen = new SortedSet<int>();
        foreach (var token in firstLine.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            ApplyToken(token, avail, chosen);

        var result = chosen.Where(avail.Contains).ToList();
        if (result.Count == 0)
            return (FirstN(avail, 1), "fallback first season (directive matched no existing seasons)");

        return (result, firstLine.Trim());
    }

    public static bool IsDirective(string firstLineTrimmed)
    {
        if (string.IsNullOrEmpty(firstLineTrimmed)) return false;
        if (char.IsDigit(firstLineTrimmed[0])) return true;
        var lower = firstLineTrimmed.ToLowerInvariant();
        return lower.StartsWith("first") || lower.StartsWith("last") ||
               lower.StartsWith("latest") || lower.StartsWith("all");
    }

    private static void ApplyToken(string token, List<int> avail, SortedSet<int> chosen)
    {
        token = token.Trim();
        if (token.Length == 0) return;

        var range = Regex.Match(token, @"^(\d+)\s*-\s*(\d+)$");
        if (range.Success)
        {
            var a = int.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture);
            var b = int.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture);
            if (a > b) (a, b) = (b, a);
            for (var n = a; n <= b; n++) chosen.Add(n);
            return;
        }

        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
        {
            chosen.Add(num);
            return;
        }

        var lower = token.ToLowerInvariant();

        if (lower == "all")
        {
            foreach (var n in avail) chosen.Add(n);
            return;
        }
        if (lower == "latest")
        {
            foreach (var n in LastN(avail, 1)) chosen.Add(n);
            return;
        }

        var first = Regex.Match(lower, @"^first\s*(\d+)?$");
        if (first.Success)
        {
            var n = first.Groups[1].Success ? int.Parse(first.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
            foreach (var s in FirstN(avail, n)) chosen.Add(s);
            return;
        }

        var last = Regex.Match(lower, @"^last\s*(\d+)?$");
        if (last.Success)
        {
            var n = last.Groups[1].Success ? int.Parse(last.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
            foreach (var s in LastN(avail, n)) chosen.Add(s);
        }
    }

    private static List<int> FirstN(List<int> avail, int n) => avail.Take(Math.Max(0, n)).ToList();

    private static List<int> LastN(List<int> avail, int n) =>
        avail.Skip(Math.Max(0, avail.Count - Math.Max(0, n))).ToList();

    private static string FirstLine(string? memo)
    {
        if (string.IsNullOrEmpty(memo)) return "";
        var idx = memo.IndexOfAny(['\n', '\r']);
        return (idx >= 0 ? memo[..idx] : memo).Trim();
    }
}
