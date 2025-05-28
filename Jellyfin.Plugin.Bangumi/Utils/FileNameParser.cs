using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Utils
{
    public static partial class FileNameParser
    {
        /// <summary>
        /// 季号匹配正则
        /// </summary>
        private static readonly Regex[] SeasonNumberRegexes =
        [
            // 第一季, 二期, 第3部
            new Regex(@"第?(?<seasonNumber>[零一二三四五六七八九十\d]+)[季部期]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // Season 1
            new Regex(@"Season\s*(?<seasonNumber>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 纯数字, S1, S01
            new Regex(@"\bS?(?<seasonNumber>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 1st Season
            new Regex(@"\b(?<seasonNumber>\d+)(st|nd|rd|th)(\s*Season)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 英文单词
            new Regex(@"\bSeason\s*(?<seasonNumber>One|Two|Three|Four|Five|Six|Seven|Eight|Nine|Ten)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 罗马数字1-20
            new Regex(@"\b(?<seasonNumber>I|II|III|IV|V|VI|VII|VIII|IX|X|XI|XII|XIII|XIV|XV|XVI|XVII|XVIII|XIX|XX)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 罗马数字unicode
            new Regex(@"(?<seasonNumber>[\u2160-\u216b\u2170-\u217b]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        ];

        /// <summary>
        /// 罗马数字映射
        /// </summary>
        private static readonly Dictionary<string, int> _romanNumber = new()
        {
            { "I", 1 }, { "II", 2 }, { "III", 3 }, { "IV", 4 },{ "V", 5 },
            { "VI", 6 }, { "VII", 7 }, { "VIII", 8 },{ "IX", 9 }, { "X", 10 },
            { "XI", 11 }, { "XII", 12 },{ "XIII", 13 }, { "XIV", 14 }, { "XV", 15 },
            { "XVI", 16 }, { "XVII", 17 }, { "XVIII", 18 },{ "XIX", 19 }, { "XX", 20 },

            { "Ⅰ", 1 }, { "Ⅱ", 2 }, { "Ⅲ", 3 }, { "Ⅳ", 4 },{ "Ⅴ", 5 },
            { "Ⅵ", 6 }, { "Ⅶ", 7 }, { "Ⅷ", 8 },{ "Ⅸ", 9 }, { "Ⅹ", 10 },
            { "Ⅺ", 11 }, { "Ⅻ", 12 },{ "ⅩⅢ", 13 }, { "ⅩⅣ", 14 }, { "ⅩⅤ", 15 },
            { "ⅩⅥ", 16 }, { "ⅩⅦ", 17 }, { "ⅩⅧ", 18 },{ "ⅩⅨ", 19 }, { "ⅩⅩ", 20 },

            {"ⅰ" ,1}, { "ⅱ", 2 }, {"ⅲ", 3}, {"ⅳ", 4}, {"ⅴ", 5},
            {"ⅵ" ,6}, {"ⅶ", 7}, {"ⅷ", 8}, {"ⅸ", 9}, {"ⅹ", 10},
            {"ⅺ" ,11}, {"ⅻ", 12}, {"ⅹⅲ", 13}, {"ⅹⅳ", 14}, {"ⅹⅴ", 15},
            {"ⅹⅵ" ,16}, {"ⅹⅶ", 17}, {"ⅹⅷ", 18}, {"ⅹⅸ", 19}, {"ⅹⅹ", 20}
        };

        /// <summary>
        /// 中文数字
        /// </summary>
        private const string CnNumber = "零一二三四五六七八九十";

        /// <summary>
        /// 英文数字映射
        /// </summary>
        private static readonly Dictionary<string, int> _enNumber = new()
        {
            { "one", 1 }, { "two", 2 }, { "three", 3 }, { "four", 4 },{ "five", 5 },
            { "six", 6 }, { "seven", 7 }, { "eight", 8 },{ "nine", 9 }, { "ten", 10 }
        };

        /// <summary>
        /// 获取文件名中的季号匹配结果
        /// </summary>
        /// <param name="filename">文件名</param>
        /// <returns></returns>
        public static Match GetAnimeSeasonMatch(string filename)
        {
            Match? match = null;

            foreach (var regex in SeasonNumberRegexes)
            {
                match = regex.Match(filename);
                if (match.Success) break;
            }

            return match!;
        }

        /// <summary>
        /// 提取季号并转换为数字
        /// </summary>
        /// <param name="filename">文件名</param>
        /// <returns>季号数字，没有包含数字时返回null（通常可认为是第1季）</returns>
        public static double? ExtractAnimeSeason(string filename)
        {
            Match match = GetAnimeSeasonMatch(filename);
            if (!match.Success) return null;

            var seasonNumber = match.Groups["seasonNumber"].Value;
            if (string.IsNullOrEmpty(seasonNumber)) return null;

            if (double.TryParse(seasonNumber, out double num)) // 纯数字
            {
                return num;
            }
            else if (CnNumber.Any(seasonNumber.Contains)) // 中文数字转换
            {
                if (seasonNumber == CnNumber[0].ToString()) return 0;

                double result = 0;
                for (int i = 0; i < seasonNumber.Length; i++)
                {
                    var digit = CnNumber.IndexOf(seasonNumber[i]);
                    if (digit < 0) continue;
                    if (i == seasonNumber.Length - 1 || seasonNumber[i + 1] != CnNumber[CnNumber.Length - 1]) // 非十位
                    {
                        result += digit;
                    }
                    else // 十位
                    {
                        result += digit * 10;
                        i++; // 跳过十位数字
                    }
                }

                return result > 0 ? result : null;
            }
            else if (_romanNumber.TryGetValue(seasonNumber.ToUpperInvariant(), out var romanNum)) // 罗马数字转换
            {
                return romanNum;
            }
            else if (_enNumber.TryGetValue(seasonNumber.ToLowerInvariant(), out var enNum)) // 英文数字转换
            {
                return enNum;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 检查文件名是否仅包含季号
        /// </summary>
        /// <param name="filename">文件名</param>
        /// <returns></returns>
        public static bool IsSeasonNumberOnly(string filename)
        {
            filename = filename.Trim();

            Match match = GetAnimeSeasonMatch(filename);
            if (!match.Success) return false;

            return filename == match.Value;
        }
    }
}
