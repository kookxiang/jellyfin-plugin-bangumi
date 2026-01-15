using System.Collections.Generic;

namespace Jellyfin.Plugin.Bangumi.Tools.DuplicatedEpisodesDetector;

// ReSharper disable UnusedAutoPropertyAccessor.Global
public class DuplicatedEpisode
{
    public int BangumiId { get; set; }

    public string Title { get; set; } = null!;

    public IEnumerable<DuplicatedEpisodeItem> Items { get; set; } = [];
}
