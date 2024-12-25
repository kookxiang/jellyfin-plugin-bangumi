using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Bangumi.Configuration;
#if !EMBY
using FuzzySharp;
using Levenshtein = Fastenshtein.Levenshtein;
#endif

namespace Jellyfin.Plugin.Bangumi.Model;

public class Subject
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Id { get; set; }

    public SubjectType Type { get; set; }

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
    public string? Summary => SummaryRaw?.ToMarkdown();

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
    public List<Tag> AllTags { get; set; } = new();

    [JsonPropertyName("nsfw")]
    public bool IsNSFW { get; set; }

    public string? Platform { get; set; }

    [JsonIgnore]
    public string[] PopularTags => AllTags
        .OrderByDescending(tag => tag.Count)
        .Select(tag => tag.Name)
        .Take(Math.Max(8, AllTags.Count / 25))
        .ToArray();

    [JsonIgnore]
    public string[] GenreTags => AllTags
        .Where(tag => Tag.GetCommonTagList(Type).Contains(tag.Name))
        .OrderByDescending(tag => tag.Count)
        .Select(tag => tag.Name)
        .Take(4)
        .ToArray();

    [JsonPropertyName("infobox")]
    public JsonElement? JsonInfoBox
    {
        get => null;
        set => InfoBox = InfoBox.ParseJson(value!.Value);
    }

    [JsonIgnore]
    public InfoBox? InfoBox { get; set; }

    [JsonIgnore]
    public string? OfficialWebSite => InfoBox?.Get("官方网站");

    [JsonIgnore]
    public string[]? Alias => InfoBox?.GetList("别名");

    [JsonIgnore]
    public DateTime? EndDate
    {
        get
        {
            var dateStr = InfoBox?.Get("播放结束");
            if (dateStr != null && DateTime.TryParseExact(dateStr, "yyyy年MM月dd日", CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.None, out var date))
                return date;
            return null;
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
                    instance.DistanceFrom(subject.ChineseName ?? subject.OriginalName),
                    instance.DistanceFrom(subject.OriginalName)
                )
            )
#endif
            .ToList();
    }

    public static List<Subject> SortByFuzzScore(IEnumerable<Subject> list, string keyword)
    {
#if EMBY
        return list.ToList();
#else
        keyword = keyword.ToLower();

        var score = list.Select(subject =>
            {
                var chineseNameScore = subject.ChineseName != null
                    ? Fuzz.Ratio(subject.ChineseName.ToLower(), keyword)
                    : 0;
                var originalNameScore = Fuzz.Ratio(subject.OriginalName.ToLower(), keyword);
                var aliasScore = subject.Alias?.Select(alias => Fuzz.Ratio(alias.ToLower(), keyword)) ?? Enumerable.Empty<int>();

                var maxScore = Math.Max(chineseNameScore, Math.Max(originalNameScore, aliasScore.DefaultIfEmpty(int.MinValue).Max()));

                return new { Subject = subject, Score = maxScore };
            })
            .OrderByDescending(pair => pair.Score)
            .Select(pair => pair.Subject)
            .ToList();

        return score;
#endif
    }
}