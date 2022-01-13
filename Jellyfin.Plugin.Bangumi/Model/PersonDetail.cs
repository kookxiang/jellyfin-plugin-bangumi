using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model
{
    public class PersonDetail
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public Dictionary<string, string> Images { get; set; } = new();

        [JsonIgnore]
        public string? DefaultImage => Images?["large"];

        [JsonPropertyName("summary")]
        public string Description { get; set; } = "";

        [JsonPropertyName("birth_year")]
        public int? BirthYear { get; set; }

        [JsonPropertyName("birth_mon")]
        public int? BirthMonth { get; set; }

        [JsonPropertyName("birth_day")]
        public int? Birthday { get; set; }

        public DateTime? Birthdate =>
            BirthYear != null && BirthMonth != null && Birthday != null ? new DateTime((int)BirthYear, (int)BirthMonth, (int)Birthday) : null;
    }
}