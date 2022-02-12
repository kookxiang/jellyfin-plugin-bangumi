using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Bangumi.Configuration;

namespace Jellyfin.Plugin.Bangumi.Model
{
    public class Legacy
    {
        public class SubjectMedium
        {
            public int Id { get; set; }

            public string? Summary { get; set; }

            public Dictionary<string, string> Images { get; set; } = new();

            [JsonIgnore]
            public string DefaultImage => Images["large"];

            [JsonPropertyName("crt")]
            public List<Character> Characters { get; set; } = new();
        }

        public class Character
        {
            public int Id { get; set; }

            [JsonPropertyName("name")]
            public string? OriginalName { get; set; }

            [JsonPropertyName("name_cn")]
            public string? ChineseName { get; set; }

            [JsonIgnore]
            public string? Name => Plugin.Instance!.Configuration.TranslationPreference switch
            {
                TranslationPreferenceType.Chinese => string.IsNullOrEmpty(ChineseName) ? OriginalName : ChineseName,
                TranslationPreferenceType.Original => OriginalName,
                _ => OriginalName
            };

            public Dictionary<string, string> Images { get; set; } = new();

            [JsonIgnore]
            public string DefaultImage => Images["large"];

            [JsonPropertyName("role_name")]
            public string? Role { get; set; }

            public List<Actor>? Actors { get; set; }
        }

        public class Actor
        {
            public int Id { get; set; }

            public string? Name { get; set; }

            public Dictionary<string, string> Images { get; set; } = new();

            [JsonIgnore]
            public string DefaultImage => Images["large"];
        }
    }
}