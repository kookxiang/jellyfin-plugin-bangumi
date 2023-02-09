using System.Linq;

namespace Jellyfin.Plugin.Bangumi.Model
{

    public static class AnitomyEpisodeTypeMapping
    {
        private static readonly string[] Normal = { "TV", "GEKIJOUBAN", "MOVIE" };
        private static readonly string[] Special = { "OAD", "OAV", "ONA", "OVA", "番外編", "總集編", "映像特典", "特典", "特典アニメ", "SPECIAL", "SPECIALS", "SP" };
        private static readonly string[] Opening = { "NCOP", "OP", "OPENING" };
        private static readonly string[] Ending = { "ED", "ENDING", "NCED" };
        private static readonly string[] Preview = { "WEB PREVIEW", "PREVIEW", "CM", "SPOT", "PV", "Teaser", "TRAILER" };
        private static readonly string[] Madness = { "MV" };
        private static readonly string[] Other = { "MENU", "INTERVIEW", "EVENT", "TOKUTEN", "LOGO", "IV" };

        public static EpisodeType? GetEpisodeType(string type)
        {

            if (Opening.Contains(type))
            {
                return EpisodeType.Opening;
            }
            else if (Ending.Contains(type))
            {
                return EpisodeType.Ending;
            }
            else if (Preview.Contains(type))
            {
                return EpisodeType.Preview;
            }
            else if (Madness.Contains(type))
            {
                return EpisodeType.Madness;
            }
            else if (Other.Contains(type))
            {
                return EpisodeType.Other;
            }
            // 命名时Special常和其他特典关键词混用
            // 而AnitomySharp会识别出所有关键词，因此放在后面判断
            else if (Special.Contains(type))
            {
                return EpisodeType.Special;
            }
            else if (Normal.Contains(type))
            {
                return EpisodeType.Normal;
            }
            else
            {
                return null;
            }

        }
    }
}
