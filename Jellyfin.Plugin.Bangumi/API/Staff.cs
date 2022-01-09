using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Bangumi.Configuration;

namespace Jellyfin.Plugin.Bangumi.API
{
    public class Staff
    {
        public int Id { get; set; }

        public string Url { get; set; }

        [JsonPropertyName("name")]
        public string OriginalName { get; set; }

        [JsonPropertyName("name_cn")]
        public string ChineseName { get; set; }

        [JsonIgnore]
        public string Name => Plugin.Instance.Configuration.TranslationPreference switch
        {
            TranslationPreferenceType.Chinese => string.IsNullOrEmpty(ChineseName) ? OriginalName : ChineseName,
            TranslationPreferenceType.Original => OriginalName,
            _ => OriginalName
        };

        public Dictionary<string, string> Images { get; set; }

        [JsonIgnore]
        public string DefaultImage => Images?["large"];

        public List<string> Jobs { get; set; }
    }
}