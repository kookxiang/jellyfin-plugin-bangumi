using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class EpisodesCollectionInfo
{
    [JsonPropertyName("episode_id")]
    public IEnumerable<int> EpisodeIdList { get; set; } = [];

    public EpisodeCollectionType Type { get; set; }
}
