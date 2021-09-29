using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.API
{
    internal class EpisodeList : SubjectBase
    {
        [JsonPropertyName("eps")]
        public List<Episode> Episodes { get; set; }
    }
}