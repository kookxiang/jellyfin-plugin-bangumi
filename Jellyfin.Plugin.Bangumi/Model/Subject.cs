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
    public IEnumerable<Tag> AllTags { get; set; } = [];

    [JsonPropertyName("nsfw")]
    public bool IsNSFW { get; set; }

    public string? Platform { get; set; }

    [JsonIgnore]
    public IEnumerable<string> PopularTags => AllTags
        .OrderByDescending(tag => tag.Count)
        .Select(tag => tag.Name)
        .Take(Math.Max(8, AllTags.Count() / 25));

    [JsonIgnore]
    public IEnumerable<string> GenreTags => AllTags
        .Where(tag => Tag.GetCommonTagList(Type).Contains(tag.Name))
        .OrderByDescending(tag => tag.Count)
        .Select(tag => tag.Name)
        .Take(4);

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
    public IEnumerable<string>? Alias => InfoBox?.GetList("别名");

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

    public static IEnumerable<Subject> SortBySimilarity(IEnumerable<Subject> list, string keyword)
    {
#if EMBY
        return list;
#else
        return GetSortedScoresBySimilarity(list, keyword).Select(s => s.Item1);
#endif
    }

    public static IEnumerable<(Subject, int)> GetSortedScoresBySimilarity(IEnumerable<Subject> list, string keyword)
    {
#if EMBY
        return list.Select(s=>(s,100));
#else
        var instance = new Levenshtein(keyword);

        return list.Select(subject =>
        {
            var score = Math.Min(
                instance.DistanceFrom(subject.ChineseName ?? subject.OriginalName),
                instance.DistanceFrom(subject.OriginalName)
            );

            if (subject.Alias != null)
            {
                foreach (var alias in subject.Alias)
                {
                    score = Math.Min(score, instance.DistanceFrom(alias));
                }
            }

            // 转换 Levenshtein 距离
            float percent = (1 - ((float)score / keyword.Length)) * 100;

            return (subject, percent);
        }).OrderByDescending(s => s.percent)
        .Select(s => (s.subject, (int)Math.Round(s.percent)));
#endif
    }

    public static IEnumerable<Subject> SortByFuzzScore(IEnumerable<Subject> list, string keyword)
    {
#if EMBY
        return list;
#else
        var score = GetSortedScoresByFuzz(list, keyword)
            .Select(pair => pair.Item1);

        return score;
#endif
    }

    public static IEnumerable<(Subject, int)> GetSortedScoresByFuzz(IEnumerable<Subject> list, string keyword)
    {
#if EMBY
        return list.Select(s=>(s,100));
#else
        keyword = keyword.ToLower();

        return list.Select(subject =>
        {
            var chineseNameScore = subject.ChineseName != null
                ? Fuzz.Ratio(subject.ChineseName.ToLower(), keyword)
                : 0;
            var originalNameScore = Fuzz.Ratio(subject.OriginalName.ToLower(), keyword);
            var aliasScore = subject.Alias?.Select(alias => Fuzz.Ratio(alias.ToLower(), keyword)) ?? [];

            var maxScore = Math.Max(chineseNameScore, Math.Max(originalNameScore, aliasScore.DefaultIfEmpty(int.MinValue).Max()));

            return (subject, maxScore);
        })
            .OrderByDescending(pair => pair.maxScore);
#endif
    }
}
