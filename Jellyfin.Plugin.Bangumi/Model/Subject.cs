using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Serialization;
using Fastenshtein;
using Jellyfin.Plugin.Bangumi.Configuration;

namespace Jellyfin.Plugin.Bangumi.Model;

public class Subject
{
    public int Id { get; set; }

    [JsonIgnore]
    public string OriginalName => WebUtility.HtmlDecode(OriginalNameRaw);

    [JsonPropertyName("name")]
    public string OriginalNameRaw { get; set; } = "";

    [JsonIgnore]
    public string? ChineseName => WebUtility.HtmlDecode(ChineseNameRaw);

    [JsonPropertyName("name_cn")]
    public string? ChineseNameRaw { get; set; }

    public string? Summary { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("air_date")]
    public string? Date2 { get; set; }

    [JsonIgnore]
    public string? AirDate => Date ?? Date2;

    [JsonIgnore]
    public string? ProductionYear => AirDate?[..4];

    public Dictionary<string, string>? Images { get; set; }

    [JsonIgnore]
    public string? DefaultImage => Images?["large"];

    [JsonPropertyName("eps")]
    public int? EpisodeCount { get; set; }

    [JsonPropertyName("rating")]
    public Rating? Rating { get; set; }

    [JsonPropertyName("tags")]
    public List<Tag> Tags { get; set; } = new();

    [JsonIgnore]
    public string[] PopularTags
    {
        get
        {
            var baseline = Tags.Sum(tag => tag.Count) / 25;
            return Tags.Where(tag => tag.Count >= baseline).Select(tag => tag.Name).ToArray();
        }
    }

    public string GetName(PluginConfiguration? configuration = default)
    {
        return configuration?.TranslationPreference switch
        {
            TranslationPreferenceType.Chinese => string.IsNullOrEmpty(ChineseName) ? OriginalName : ChineseName,
            TranslationPreferenceType.Original => OriginalName,
            _ => OriginalName
        };
    }

    public static List<Subject> SortBySimilarity(IEnumerable<Subject> list, string keyword)
    {
        var instance = new Levenshtein(keyword);
        return list
            .OrderBy(subject =>
                Math.Min(
                    instance.DistanceFrom(subject.ChineseName),
                    instance.DistanceFrom(subject.OriginalName)
                )
            )
            .ToList();
    }
}