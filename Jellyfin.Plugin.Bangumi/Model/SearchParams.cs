namespace Jellyfin.Plugin.Bangumi.Model;

public class SearchParams
{
    public string? Keyword { get; set; }

    public string? Sort { get; set; }

    public SearchFilter Filter { get; set; } = new();
}
