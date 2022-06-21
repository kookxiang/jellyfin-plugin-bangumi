using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class RelatedPerson
{
    public int Id { get; set; }

    public int Type { get; set; }

    public string Name { get; set; } = "";

    public List<PersonCareer>? Career { get; set; }

    public Dictionary<string, string> Images { get; set; } = new();

    [JsonIgnore]
    public string? DefaultImage => Images?["large"];

    public string? Relation { get; set; }
}