using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class SearchParams
{
    public string? Keyword { get; set; }

    public string? Sort { get; set; }

    public SearchFilter Filter { get; set; } = new();

    public class SearchFilter
    {
        public SubjectType[]? Type { get; set; }

        [JsonPropertyName("nsfw")]
        public bool? NSFW { get; set; }
    }
}