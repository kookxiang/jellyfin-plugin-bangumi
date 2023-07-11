using System.Collections.Generic;

namespace Jellyfin.Plugin.Bangumi.Model;

internal class SearchResult<T>
{
    public int Total { get; set; }

    public int Offset { get; set; }

    public int Limit { get; set; }

    public List<T> Data { get; set; } = new();
}