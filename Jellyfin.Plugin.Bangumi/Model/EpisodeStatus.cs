using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EpisodeStatus
{
    [EnumMember(Value = "watched")]
    Watched,

    [EnumMember(Value = "queue")]
    InQueue,

    [EnumMember(Value = "drop")]
    Dropped,

    [EnumMember(Value = "remove")]
    Removed
}