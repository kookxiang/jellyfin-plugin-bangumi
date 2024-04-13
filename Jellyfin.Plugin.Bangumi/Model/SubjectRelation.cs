using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubjectRelation
{
    [EnumMember(Value = "续集")]
    Sequel,

    [EnumMember(Value = "前传")]
    Prequel,

    [EnumMember(Value = "片头曲")]
    Opening,

    [EnumMember(Value = "片尾曲")]
    Ending,

    [EnumMember(Value = "原声集")]
    OriginalSoundTrack,

    [EnumMember(Value = "角色歌")]
    CharacterSong,

    [EnumMember(Value = "番外篇")]
    Extra,

    [EnumMember(Value = "书籍")]
    Book,

    [EnumMember(Value = "游戏")]
    Game,

    [EnumMember(Value = "其他")]
    Other
}