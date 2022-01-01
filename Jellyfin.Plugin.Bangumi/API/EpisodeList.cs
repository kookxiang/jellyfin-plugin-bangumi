using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.API
{
    internal class EpisodeList : SubjectBase
    {
        [JsonPropertyName("eps")]
        public List<Episode> Episodes { get; set; }

        [JsonPropertyName("eps_count")]
        public new int EpisodeCount
        {
            get => base.EpisodeCount > 0 ? base.EpisodeCount : Convert.ToInt32(Episodes.Max(episode => episode.Order));
            set => base.EpisodeCount = value;
        }
    }
}