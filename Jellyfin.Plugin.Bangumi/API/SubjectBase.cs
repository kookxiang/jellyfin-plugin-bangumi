using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.API
{
    public class SubjectBase : Subject
    {
        public string Summary { get; set; }

        [JsonPropertyName("air_date")]
        public string AirDate { get; set; }

        [JsonPropertyName("air_weekday")]
        public int AirWeekday { get; set; }

        [JsonPropertyName("eps_count")]
        public int EpisodeCount { get; set; }

        public Dictionary<string, string> Images { get; set; }

        [JsonIgnore]
        public string DefaultImage => Images?["large"];
    }
}