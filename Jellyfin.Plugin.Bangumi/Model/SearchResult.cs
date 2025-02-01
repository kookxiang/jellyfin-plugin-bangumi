using System.Collections.Generic;

namespace Jellyfin.Plugin.Bangumi.Model;

public class SearchResult<T>
{
    public int Total { get; set; }

    public int Offset { get; set; }

    public int Limit { get; set; }

    // FIXME: workaround for old search api
    public IEnumerable<T>? List { get; set; }

    public IEnumerable<T>? Data { get; set; }
}
