using System.Text.Json.Serialization;
using Jellyfin.Plugin.Bangumi.Configuration;

namespace Jellyfin.Plugin.Bangumi.API
{
    internal class Episode
    {
        public int Id { get; set; }

        public string Url { get; set; }

        public EpisodeType Type { get; set; }

        [JsonPropertyName("name")]
        public string OriginalName { get; set; }

        [JsonPropertyName("name_cn")]
        public string ChineseName { get; set; }

        [JsonIgnore]
        public string Name => Plugin.Instance.Configuration.TranslationPreference switch
        {
            TranslationPreferenceType.Chinese => ChineseName,
            TranslationPreferenceType.Original => OriginalName,
            _ => OriginalName
        };

        public string Duration { get; set; }

        [JsonPropertyName("airdate")]
        public string AirDate { get; set; }

        [JsonPropertyName("desc")]
        public string Description { get; set; }

        [JsonPropertyName("sort")]
        public int Order { get; set; }

        public string Status { get; set; }
    }
}