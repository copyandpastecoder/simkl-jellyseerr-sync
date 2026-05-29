namespace SimklJellyseerrSync;

/// <summary>
/// Builds the "ReadyToWatch" memo, merging the marker into an existing memo.
/// Priority within SIMKL's 140-char cap: line 1 (season directive) and the
/// marker are never trimmed; only the user's free-note text in between is trimmed.
/// </summary>
public static class ReadyMemo
{
    public const int MaxLen = 140;

    public static bool AlreadyMarked(string? memo, string prefix) =>
        !string.IsNullOrEmpty(memo) && memo.Contains(prefix, StringComparison.OrdinalIgnoreCase);

    public static string Merge(string? originalMemo, string prefix, string date)
    {
        var marker = $"{prefix} {date}";
        var lines = (originalMemo ?? "")
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n');

        var firstLine = lines.Length > 0 ? lines[0].Trim() : "";

        var middle = lines.Skip(1)
            .Where(l => !l.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var middleText = string.Join("\n", middle).Trim();

        var core = string.IsNullOrEmpty(firstLine) ? marker : $"{firstLine}\n{marker}";

        if (string.IsNullOrEmpty(middleText))
            return Cap(core, marker, firstLine);

        var full = string.IsNullOrEmpty(firstLine)
            ? $"{middleText}\n{marker}"
            : $"{firstLine}\n{middleText}\n{marker}";
        if (full.Length <= MaxLen) return full;

        var room = MaxLen - core.Length - 1; // -1 for the newline before the note block
        if (room <= 0) return Cap(core, marker, firstLine);

        var trimmed = middleText[..Math.Min(middleText.Length, room)].TrimEnd();
        if (trimmed.Length == 0) return Cap(core, marker, firstLine);

        return string.IsNullOrEmpty(firstLine)
            ? $"{trimmed}\n{marker}"
            : $"{firstLine}\n{trimmed}\n{marker}";
    }

    private static string Cap(string core, string marker, string firstLine)
    {
        if (core.Length <= MaxLen) return core;
        var room = MaxLen - marker.Length - 1;
        if (room <= 0) return marker.Length <= MaxLen ? marker : marker[..MaxLen];
        return $"{firstLine[..Math.Min(firstLine.Length, room)]}\n{marker}";
    }
}
