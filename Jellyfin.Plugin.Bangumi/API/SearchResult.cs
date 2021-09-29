using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.API
{
    internal class SearchResult<T> : StatusCode
    {
        [JsonPropertyName("results")]
        public int ResultCount { get; set; }

        public List<T> List { get; set; }
    }
}