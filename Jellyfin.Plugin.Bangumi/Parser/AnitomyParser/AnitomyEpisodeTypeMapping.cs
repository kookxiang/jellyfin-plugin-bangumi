using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Bangumi.Model;

namespace Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;

/// <summary>
///     将 Anitomy 识别出的章节类型映射到 Bangumi 章节类型
/// </summary>
public static class AnitomyEpisodeTypeMapping
{
    // 存在先后顺序的关键词集合，映射时优先返回最前面那个
    private static readonly HashSet<string> _normal = new(StringComparer.OrdinalIgnoreCase) { "TV", "GEKIJOUBAN", "MOVIE" };
    private static readonly HashSet<string> _special = new(StringComparer.OrdinalIgnoreCase) { "OAD", "OAV", "ONA", "OVA", "番外編", "總集編", "DRAMA" };
    private static readonly HashSet<string> _specialOther = new(StringComparer.OrdinalIgnoreCase) { "映像特典", "特典", "特典映像", "特典アニメ", "特報", "SPECIAL", "SPECIALS", "SP", "SPs" };
    private static readonly HashSet<string> _opening = new(StringComparer.OrdinalIgnoreCase) { "NCOP", "OP", "OPENING" };
    private static readonly HashSet<string> _ending = new(StringComparer.OrdinalIgnoreCase) { "NCED", "ED", "ENDING" };
    // 同类型可能被误匹配，如 CM01 匹配上了 PV01 的元数据
    private static readonly HashSet<string> _preview = new(StringComparer.OrdinalIgnoreCase) { "WEB PREVIEW", "PREVIEW", "CM", "SPOT", "PV", "Teaser", "TRAILER", "YOKOKU", "予告" };
    private static readonly HashSet<string> _madness = new(StringComparer.OrdinalIgnoreCase) { "MV" };
    private static readonly HashSet<string> _other = new(StringComparer.OrdinalIgnoreCase) { "MENU", "INTERVIEW", "EVENT", "TOKUTEN", "LOGO", "IV" };


    // 类型优先级顺序
    private static readonly (HashSet<string> Keywords, EpisodeType Type)[] _categoriesPriority =
    {
        (_opening, EpisodeType.Opening),
        (_ending, EpisodeType.Ending),
        (_preview, EpisodeType.Preview),
        (_madness, EpisodeType.Madness),
        (_other, EpisodeType.Other),
        (_special, EpisodeType.Special),
        // 命名时 Special 常和其他特典关键词混用
        // 而 AnitomySharp 会识别出所有关键词，因此放在后面判断
        (_specialOther, EpisodeType.Other),
        (_normal, EpisodeType.Normal)
    };


    /// <summary>
    ///     根据传入的字符串数组判断章节类型
    /// </summary>
    /// <param name="types">需要判断的字符串</param>
    /// <returns>(Anitomy 章节类型, Bangumi 章节类型)</returns>
    public static (string?, EpisodeType?) GetAnitomyAndBangumiEpisodeType(string[]? types)
    {
        if (types == null || types.Length == 0)
            return (null, null);

        var typesSet = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);

        foreach (var category in _categoriesPriority)
        {
            foreach (var keyword in category.Keywords)
            {
                if (typesSet.Contains(keyword))
                {
                    return (keyword, category.Type);
                }
            }
        }
        
        return (null, null);
    }
}

