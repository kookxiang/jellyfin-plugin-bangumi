using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.API
{
    /**
     * 声优
     */
    public class Actor
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Url { get; set; }

        public Dictionary<string, string> Images { get; set; }

        [JsonIgnore]
        public string DefaultImage => Images?["large"];
    }
}