using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Bangumi.Configuration;
#if !EMBY
using System;
using Fastenshtein;
#endif

namespace Jellyfin.Plugin.Bangumi.Model;

public class Subject
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Id { get; set; }

    [JsonIgnore]
    public string? Name => Configuration.TranslationPreference switch
    {
        TranslationPreferenceType.Chinese => string.IsNullOrEmpty(ChineseName) ? OriginalName : ChineseName,
        TranslationPreferenceType.Original => OriginalName,
        _ => OriginalName
    };

    [JsonIgnore]
    public string OriginalName => WebUtility.HtmlDecode(OriginalNameRaw);

    [JsonPropertyName("name")]
    public string OriginalNameRaw { get; set; } = "";

    [JsonIgnore]
    public string? ChineseName => WebUtility.HtmlDecode(ChineseNameRaw);

    [JsonPropertyName("name_cn")]
    public string? ChineseNameRaw { get; set; }

    [JsonIgnore]
    public string? Summary => Configuration.ConvertLineBreaks ? SummaryRaw?.ReplaceLineEndings(Constants.HtmlLineBreak).TrimStart() : SummaryRaw;

    [JsonPropertyName("summary")]
    public string? SummaryRaw { get; set; }

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

    public string? Platform { get; set; }

    [JsonIgnore]
    public string[] PopularTags
    {
        get
        {
            var baseline = Tags.Sum(tag => tag.Count) / 25;
            return Tags.Where(tag => tag.Count >= baseline).Select(tag => tag.Name).ToArray();
        }
    }

    public static List<Subject> SortBySimilarity(IEnumerable<Subject> list, string keyword)
    {
#if EMBY
        return list
#else
        var instance = new Levenshtein(keyword);
        return list
            .OrderBy(subject =>
                Math.Min(
                    instance.DistanceFrom(subject.ChineseName),
                    instance.DistanceFrom(subject.OriginalName)
                )
            )
#endif
            .ToList();
    }
}