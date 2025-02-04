#if EMBY
using PersonEntityType = MediaBrowser.Model.Entities.PersonType;
#else
using Jellyfin.Data.Enums;
#endif
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Bangumi.Model;

public class RelatedPerson
{
#if EMBY
    private static readonly Dictionary<string, PersonEntityType> RelationMap = new()
    {
        ["导演"] = PersonEntityType.Director,
        ["制片人"] = PersonEntityType.Producer,
        ["系列构成"] = PersonEntityType.Composer,
        ["脚本"] = PersonEntityType.Writer
    };
#else
    private static readonly Dictionary<string, PersonKind> RelationMap = new()
    {
        ["导演"] = PersonKind.Director,
        ["制片人"] = PersonKind.Producer,
        ["系列构成"] = PersonKind.Composer,
        ["脚本"] = PersonKind.Writer
    };
#endif

    public int Id { get; set; }

    public int Type { get; set; }

    public string Name { get; set; } = "";

    public IEnumerable<PersonCareer>? Career { get; set; }

    public Dictionary<string, string?> Images { get; set; } = new();

    [JsonIgnore]
    public string? DefaultImage => Images.GetValueOrDefault("large", null);

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
