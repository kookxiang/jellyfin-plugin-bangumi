using System;
using System.Linq;
using Jellyfin.Plugin.Bangumi.Model;

namespace Jellyfin.Plugin.Bangumi.Parser.Anitomy
{
    public static class AnitomyEpisodeTypeMapping
    {
        private static readonly string[] Normal = { "TV", "GEKIJOUBAN", "MOVIE" };
        private static readonly string[] Special = { "OAD", "OAV", "ONA", "OVA", "番外編", "總集編" };
        private static readonly string[] SpecialOther = { "映像特典", "特典", "特典アニメ", "SPECIAL", "SPECIALS", "SP", "SPs" };
        private static readonly string[] Opening = { "NCOP", "OP", "OPENING" };
        private static readonly string[] Ending = { "ED", "ENDING", "NCED" };
        private static readonly string[] Preview = { "WEB PREVIEW", "PREVIEW", "CM", "SPOT", "PV", "Teaser", "TRAILER" };
        private static readonly string[] Madness = { "MV" };
        private static readonly string[] Other = { "MENU", "INTERVIEW", "EVENT", "TOKUTEN", "LOGO", "IV" };

        public static (string?, EpisodeType?) GetEpisodeType(string[]? types)
        {
            if (types is null)
                return (null, null);

            // 按优先级返回剧集类型
            if (Opening.Intersect(types, StringComparer.OrdinalIgnoreCase).Any())
            {
                return (Opening.Intersect(types, StringComparer.OrdinalIgnoreCase).FirstOrDefault(), EpisodeType.Opening);
            }
            else if (Ending.Intersect(types, StringComparer.OrdinalIgnoreCase).Any())
            {
                return (Ending.Intersect(types, StringComparer.OrdinalIgnoreCase).FirstOrDefault(), EpisodeType.Ending);
            }
            else if (Preview.Intersect(types, StringComparer.OrdinalIgnoreCase).Any())
            {
                return (Preview.Intersect(types, StringComparer.OrdinalIgnoreCase).FirstOrDefault(), EpisodeType.Preview);
            }
            else if (Madness.Intersect(types, StringComparer.OrdinalIgnoreCase).Any())
            {
                return (Madness.Intersect(types, StringComparer.OrdinalIgnoreCase).FirstOrDefault(), EpisodeType.Madness);
            }
            else if (Other.Intersect(types, StringComparer.OrdinalIgnoreCase).Any())
            {
                return (Other.Intersect(types, StringComparer.OrdinalIgnoreCase).FirstOrDefault(), EpisodeType.Other);
            }
            else if (Special.Intersect(types, StringComparer.OrdinalIgnoreCase).Any())
            {
                return (Special.Intersect(types, StringComparer.OrdinalIgnoreCase).FirstOrDefault(), EpisodeType.Special);
            }
            // 命名时 Special 常和其他特典关键词混用
            // 而 AnitomySharp 会识别出所有关键词，因此放在后面判断
            else if (SpecialOther.Intersect(types, StringComparer.OrdinalIgnoreCase).Any())
            {
                return (SpecialOther.Intersect(types, StringComparer.OrdinalIgnoreCase).FirstOrDefault(), EpisodeType.Other);
            }
            else if (Normal.Intersect(types, StringComparer.OrdinalIgnoreCase).Any())
            {
                return (Normal.Intersect(types, StringComparer.OrdinalIgnoreCase).FirstOrDefault(), EpisodeType.Normal);
            }

            return (null, null);
        }
    }
}
