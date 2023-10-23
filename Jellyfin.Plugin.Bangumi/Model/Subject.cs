using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
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
    public string? ProductionYear => AirDate?.Length >= 4 ? AirDate?[..4] : null;

    public Dictionary<string, string>? Images { get; set; }

    [JsonIgnore]
    public string? DefaultImage => Images?["large"];

    [JsonPropertyName("eps")]
    public int? EpisodeCount { get; set; }

    [JsonPropertyName("rating")]
    public Rating? Rating { get; set; }

    [JsonPropertyName("tags")]
    public List<Tag> Tags { get; set; } = new();

    [JsonPropertyName("nsfw")]
    public bool IsNSFW { get; set; }

    [JsonIgnore]
    public string[] PopularTags
    {
        get
        {
            var baseline = Tags.Sum(tag => tag.Count) / 25;
            return Tags.Where(tag => tag.Count >= baseline).Select(tag => tag.Name).ToArray();
        }
    }
    [JsonPropertyName("infobox")]
    public List<InfoboxItem> Infobox { get; set; } = new();

    public string[] Alias()
    {
        var aliases = new List<string>();

        foreach (var item in Infobox)
        {
            if (item.Key == "别名" && item.Value.ValueKind == JsonValueKind.Array)
            {
                var values = item.Value.EnumerateArray()
                    .Select(x => x.GetProperty("v").GetString())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToArray();
                if (values != null)
                {
                    aliases.AddRange(values!);
                }
                return aliases.ToArray();
            }
        }

        return aliases.ToArray();
    }
    public class InfoboxItem
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }= string.Empty;

        [JsonPropertyName("value")]
        public JsonElement Value { get; set; }
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
        var distances = new Dictionary<Subject, int>();

        foreach (var subject in list)
        {
            var chineseNameDistance = instance.DistanceFrom(subject.ChineseName);
            var originalNameDistance = instance.DistanceFrom(subject.OriginalName);
            var aliasDistances = subject.Alias().Select(alias => instance.DistanceFrom(alias));

            var minDistance = Math.Min(Math.Min(chineseNameDistance, originalNameDistance), aliasDistances.Any() ? aliasDistances.Min() : int.MaxValue);
            distances.Add(subject, minDistance);
        }

        return distances.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToList();
    }

}