using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class SearchFilter
{
    public IEnumerable<SubjectType>? Type { get; set; }

    [JsonPropertyName("nsfw")]
    public bool? NSFW { get; set; }
}
