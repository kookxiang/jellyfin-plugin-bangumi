using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PersonCareer
{
    [EnumMember(Value = "producer")]
    Producer,

    [EnumMember(Value = "mangaka")]
    Mangaka,

    [EnumMember(Value = "artist")]
    Artist,

    [EnumMember(Value = "seiyu")]
    Seiyu,

    [EnumMember(Value = "writer")]
    Writer,

    [EnumMember(Value = "illustrator")]
    Illustrator,

    [EnumMember(Value = "actor")]
    Actor
}