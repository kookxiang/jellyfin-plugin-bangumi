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
using Jellyfin.Plugin.Bangumi.Utils;
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

    /// <summary>
    /// 获取候选列表中每个条目的匹配分数
    /// </summary>
    /// <param name="list">候选列表</param>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="seasonNumber">季号，如果为null，仅对关键词进行常规匹配；
    /// 如果为0，只匹配OVA、剧场版条目；
    /// 如果为其他，则同时匹配候选列表及关键词中的季号，降低不匹配候选项分数</param>
    /// <returns></returns>
    /// <remarks><paramref name="seasonNumber"/> 不为null时，需要确保 <paramref name="keyword"/> 不包含季号信息，否则匹配度可能降低</remarks>
    public static IEnumerable<(Subject, int)> GetSortedScores(IEnumerable<Subject> list, string keyword, int? seasonNumber = null)
    {
        List<(Subject, int)> result = [];

        if (list == null) return result;
        var candidateList = list.ToArray();
        if (candidateList.Length == 0) return result;

        keyword = keyword.ToLower();

        foreach (var candidate in candidateList)
        {
            // 跳过非OVA、剧场版条目
            if (seasonNumber == 0)
            {
                if (!BangumiApi.IsOVAOrMovie(candidate))
                {
                    result.Add((candidate, 0));
                    continue;
                }
            }

            // 预处理候选名称列表
            var names = (candidate.Alias ?? [])
                .Concat([candidate.ChineseName, candidate.OriginalName])
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!.ToLower())
                .ToArray();

            int candidateSeasonNumber = 1;
            if (seasonNumber != null)
            {
#if EMBY
                candidateSeasonNumber = seasonNumber.Value;
#else
                // 拆分候选项名称和季号
                for (int i = 0; i < names.Length; i++)
                {
                    var (parsedNameTitle, parsedNameSeason) = FileNameParser.SplitAnimeTitleAndSeason(names[i], false);
                    names[i] = parsedNameTitle;

                    // 季号null等同1
                    parsedNameSeason ??= 1;

                    // 部分别名不包含季号信息，优先取高于1的季号
                    if (parsedNameSeason > 1)
                    {
                        candidateSeasonNumber = (int)parsedNameSeason;
                    }
                }
#endif
            }

            // 获取名称分数
            int score;
            if (Plugin.Instance!.Configuration.SortByFuzzScore)
            {
                score = GetSortedScoresByFuzz(names, keyword);
            }
            else
            {
                score = GetSortedScoresBySimilarity(names, keyword);
            }

            // 季号不匹配，降权
            if (seasonNumber != null && seasonNumber != candidateSeasonNumber)
            {
                score = (int)(score * 0.8);
            }

            result.Add((candidate, score));
        }

        // 按最匹配顺序排序
        return result.OrderByDescending(s => s.Item2);
    }

    public static int GetSortedScoresBySimilarity(IEnumerable<string> candidateList, string keyword)
    {
#if EMBY
        return 100;
#else
        var instance = new Levenshtein(keyword);

        var maxScore = candidateList.Select(candidate =>
        {
            var score = instance.DistanceFrom(candidate);

            // 转换 Levenshtein 距离
            float percent = (1 - ((float)score / keyword.Length)) * 100;

            return percent;
        }).OrderByDescending(s => s)
        .First();

        return (int)maxScore;
#endif
    }

    public static int GetSortedScoresByFuzz(IEnumerable<string> candidateList, string keyword)
    {
#if EMBY
        return 100;
#else
        var maxScore = candidateList.Select(candidate =>
        {
            var score = Fuzz.Ratio(candidate, keyword);

            return score;
        }).OrderByDescending(s => s)
        .First();

        return maxScore;
#endif
    }
}
