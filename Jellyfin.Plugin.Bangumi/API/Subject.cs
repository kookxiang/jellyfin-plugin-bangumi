using System.Text.Json.Serialization;
using Jellyfin.Plugin.Bangumi.Configuration;

namespace Jellyfin.Plugin.Bangumi.API
{
    public class Subject : StatusCode
    {
        public int Id { get; set; }

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
    }
}