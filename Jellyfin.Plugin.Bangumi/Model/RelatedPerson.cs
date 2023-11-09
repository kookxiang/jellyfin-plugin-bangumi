using System.Collections.Generic;
using System.Text.Json.Serialization;
using MediaBrowser.Controller.Entities;
using PersonEntityType = MediaBrowser.Model.Entities.PersonType;

namespace Jellyfin.Plugin.Bangumi.Model;

public class RelatedPerson
{
#if EMBY
    private static readonly Dictionary<string, PersonEntityType> RelationMap = new()
#else
    private static readonly Dictionary<string, string> RelationMap = new()
#endif
    {
        ["导演"] = PersonEntityType.Director,
        ["制片人"] = PersonEntityType.Producer,
        ["系列构成"] = PersonEntityType.Composer,
        ["脚本"] = PersonEntityType.Writer
    };

    public int Id { get; set; }

    public int Type { get; set; }

    public string Name { get; set; } = "";

    public List<PersonCareer>? Career { get; set; }

    public Dictionary<string, string> Images { get; set; } = new();

    [JsonIgnore]
    public string? DefaultImage => Images?["large"];

    public string? Relation { get; set; }

    public PersonInfo? ToPersonInfo()
    {
        if (!RelationMap.TryGetValue(Relation ?? "", out var type))
            return null;
        var info = new PersonInfo
        {
            Name = Name,
            Type = type,
            ImageUrl = DefaultImage
        };
        info.ProviderIds.Add(Constants.ProviderName, $"{Id}");
        return info;
    }
}