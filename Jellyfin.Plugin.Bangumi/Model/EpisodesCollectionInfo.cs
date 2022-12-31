using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class EpisodesCollectionInfo
{
    [JsonPropertyName("episode_id")]
    public List<int> EpisodeIdList { get; set; } = new();

    public EpisodeCollectionType Type { get; set; }
}