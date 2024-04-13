using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubjectPlatform
{
    [EnumMember(Value = "TV")]
    Tv,

    [EnumMember(Value = "剧场版")]
    Movie
}