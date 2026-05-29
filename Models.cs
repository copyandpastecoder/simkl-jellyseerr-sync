namespace SimklJellyseerrSync;

public record SimklItem(string Title, int Year, int TmdbId, string MediaType, string Status);

public class SimklActivities
{
    private readonly Dictionary<string, DateTime?> _all = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string type, DateTime? all) => _all[type] = all;

    public DateTime? AllFor(string type) => _all.TryGetValue(type, out var all) ? all : null;
}

public static class MediaStatus
{
    public const int Unknown = 1;
    public const int Pending = 2;
    public const int Processing = 3;
    public const int PartiallyAvailable = 4;
    public const int Available = 5;

    public static string Name(int status) => status switch
    {
        Pending => "pending",
        Processing => "processing",
        PartiallyAvailable => "partially available",
        Available => "available",
        _ => "unknown"
    };
}
