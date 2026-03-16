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
    /// <param name="keyword">搜索关键词，不区分大小写</param>
    /// <param name="seasonNumber">季号，由于季号存在多种格式（如第二季、Season 2、II），直接放到关键词中搜索可能不准确，因此单独传入进行辅助判断
    ///     <br/>如果为null，仅对关键词进行常规匹配；
    ///     <br/>如果为0，只匹配OVA、剧场版条目；
    ///     <br/>如果为其他，则同时匹配候选列表及关键词中的季号，降低不匹配候选项分数
    ///     <br/><br/>不为null时，需要确保 <paramref name="keyword"/> 不包含季号信息，否则匹配度可能降低
    /// </param>
    /// <returns>（条目、分数）元组集合，按匹配度由高到低排序。
    ///     <br/>分数最高为100，表示完全匹配；分数最低为0，表示完全不匹配。
    /// </returns>
    public static IEnumerable<(Subject, int)> GetSortedScores(IEnumerable<Subject> list, string keyword, int? seasonNumber = null)
    {
        List<(Subject, int)> result = [];

        if (list == null) return result;
        var candidateList = list.ToArray();
        if (candidateList.Length == 0) return result;

        foreach (var candidate in candidateList)
        {
            result.Add(GetSubjectScore(candidate, keyword, seasonNumber));
        }

        // 按最匹配顺序排序
        return result.OrderByDescending(s => s.Item2);
    }

    /// <summary>
    /// 获取条目的匹配分数
    /// </summary>
    /// <param name="subject">待匹配条目</param>
    /// <param name="keyword">搜索关键词，不区分大小写</param>
    /// <param name="seasonNumber">季号，由于季号存在多种格式（如第二季、Season 2、II），直接放到关键词中搜索可能不准确，因此单独传入进行辅助判断
    ///     <br/>如果为null，仅对关键词进行常规匹配；
    ///     <br/>如果为0，只匹配OVA、剧场版条目；
    ///     <br/>如果为其他，则同时匹配候选列表及关键词中的季号，降低不匹配候选项分数
    ///     <br/><br/>不为null时，需要确保 <paramref name="keyword"/> 不包含季号信息，否则匹配度可能降低
    /// </param>
    /// <returns>（条目、分数）元组。
    ///     <br/>分数最高为100，表示完全匹配；分数最低为0，表示完全不匹配。
    /// </returns>
    private static (Subject, int) GetSubjectScore(Subject subject, string keyword, int? seasonNumber)
    {
        // 类型不匹配，跳过分数计算步骤
        if (seasonNumber == 0 && !BangumiApi.IsOVAOrMovie(subject))
        {
            return (subject, 0);
        }

        // 预处理候选名称列表，统一为小写
        var names = (subject.Alias ?? [])
            .Concat([subject.ChineseName, subject.OriginalName])
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!.ToLower())
            .ToArray();
        keyword = keyword.ToLower();

        // 拆分并更新候选名称，提取季号
        int actualSeasonNumber = 1;
        if (seasonNumber != null)
        {
#if EMBY
            actualSeasonNumber = seasonNumber.Value;
#else
            // 拆分候选名称和季号
            for (int i = 0; i < names.Length; i++)
            {
                var (parsedNameTitle, parsedNameSeason) = FileNameParser.SplitAnimeTitleAndSeason(names[i], false);

                // 更新候选名称为不包含季号信息的名称
                names[i] = parsedNameTitle;

                // 标题不含季号则当作第一季处理
                parsedNameSeason ??= 1;

                // 部分别名不包含季号信息，优先取高于1的季号
                if (parsedNameSeason > 1)
                {
                    actualSeasonNumber = (int)parsedNameSeason;
                }
            }
#endif
        }

        // 获取候选名称最高分数
        var score = Plugin.Instance!.Configuration.SortByFuzzScore
            ? GetSortedScoresByFuzz(names, keyword)
            : GetSortedScoresBySimilarity(names, keyword);

        // 季号不匹配，降权
        if (seasonNumber != null && seasonNumber != actualSeasonNumber)
        {
            score = (int)(score * 0.8);
        }

        return (subject, score);
    }

    /// <summary>
    /// 使用 Levenshtein 算法计算候选名称集合与关键词的相似度，并返回最高分数。
    /// </summary>
    /// <param name="candidateList">候选名称集合</param>
    /// <param name="keyword">关键词</param>
    /// <returns>分数最高为100，表示完全匹配；分数最低为0，表示完全不匹配。</returns>
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
            var maxLen = Math.Max(candidate.Length, keyword.Length);
            float percent = maxLen == 0
                ? 100
                : (1f - (float)score / maxLen) * 100f;

            percent = Math.Clamp(percent, 0f, 100f);

            return percent;
        }).OrderByDescending(s => s)
        .First();

        return (int)maxScore;
#endif
    }

    /// <summary>
    /// 使用 Fuzz.Ratio 算法计算候选名称集合与关键词的相似度，并返回最高分数。
    /// </summary>
    /// <param name="candidateList">候选名称集合</param>
    /// <param name="keyword">关键词</param>
    /// <returns>分数最高为100，表示完全匹配；分数最低为0，表示完全不匹配。</returns>
    public static int GetSortedScoresByFuzz(IEnumerable<string> candidateList, string keyword, int minScore = 0)
    {
#if EMBY
        return 100;
#else
        var maxScore = candidateList
        .Select(candidate => Fuzz.Ratio(candidate, keyword))
        .Where(score => score >= minScore)
        .OrderByDescending(s => s)
        .First();

        return maxScore;
#endif
    }
}
