using System.Collections.Generic;

namespace Jellyfin.Plugin.Bangumi.Model;

public class Collection
{
    public CollectionType? Type { get; set; }

    public int? Rate { get; set; }

    public string? Comment { get; set; }

    public bool? Private { get; set; }

    public IEnumerable<string>? Tags { get; set; }
}