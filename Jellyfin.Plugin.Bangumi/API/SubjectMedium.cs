using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.API
{
    public class SubjectMedium : SubjectSmall
    {
        [JsonPropertyName("crt")]
        public List<Character> Characters { get; set; }

        [JsonPropertyName("staff")]
        public List<Staff> StaffList { get; set; }
    }
}