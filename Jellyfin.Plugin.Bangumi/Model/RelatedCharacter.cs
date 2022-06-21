using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class RelatedCharacter
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public CharacterType Type { get; set; }

    public Dictionary<string, string> Images { get; set; } = new();

    [JsonIgnore]
    public string? DefaultImage => Images?["large"];

    public string Relation { get; set; } = "";

    public Person[]? Actors { get; set; }
}