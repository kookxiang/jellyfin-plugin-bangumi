using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DirectoryType
{
    Auto = 0,
    Normal = 1,
    Special = 2,
}
