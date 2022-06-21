using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class Person
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public PersonType Type { get; set; }

    public List<PersonCareer>? Career { get; set; }

    public Dictionary<string, string>? Images { get; set; }

    [JsonIgnore]
    public string? DefaultImage => Images?["large"];

    [JsonPropertyName("short_summary")]
    public string ShortSummary { get; set; } = "";

    public bool Locked { get; set; }
}