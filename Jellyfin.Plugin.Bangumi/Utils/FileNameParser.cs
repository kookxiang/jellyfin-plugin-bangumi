using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;

namespace Jellyfin.Plugin.Bangumi.Utils
{
    public static partial class FileNameParser
    {
        /// <summary>
        /// 季号预处理正则，删除文件名中容易干扰季号识别的数字
        /// </summary>
        private static readonly Regex[] _seasonNumberPreprocessRegexes =
        [
            // 01-12, 1 - 12
            new Regex(@"\d+\s*-\s*\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 10-Bit
            new Regex(@"\b\d{1,2}-Bit\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        ];

        /// <summary>
        /// 季号匹配正则
        /// </summary>
        private static readonly Regex[] _seasonNumberRegexes =
        [
            // 第一季, 二期, 第3部
            new Regex(@"第?(?<seasonNumber>[零一二三四五六七八九十\d]+)[季部期]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // Season 1
            new Regex(@"Season\s*(?<seasonNumber>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // S1, S01
            new Regex(@"\bS(?<seasonNumber>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 1st Season
            new Regex(@"\b(?<seasonNumber>\d+)(st|nd|rd|th)(\s*Season)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 英文单词
            new Regex(@"\bSeason\s*(?<seasonNumber>One|Two|Three|Four|Five|Six|Seven|Eight|Nine|Ten)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 罗马数字1-20
            new Regex(@"\b(?<seasonNumber>I|II|III|IV|V|VI|VII|VIII|IX|X|XI|XII|XIII|XIV|XV|XVI|XVII|XVIII|XIX|XX)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 罗马数字unicode
            new Regex(@"(?<seasonNumber>[\u2160-\u216b\u2170-\u217b]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 纯数字（容易识别错误，应放在最后）
            new Regex(@"\b(?<seasonNumber>\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ];

        /// <summary>
        /// 集号匹配正则
        /// </summary>
        private static readonly Regex[] _episodeNumberRegexes =
        [
            // 第21話, 第二话
            new Regex(@"第(?<episodeNumber>[零一二三四五六七八九十\d]+(\.\d)?)[话話集]", RegexOptions.IgnoreCase | RegexOptions.Compiled), 
            // S01E01
            new Regex(@"(?<=S\d+)E(?<episodeNumber>\d+(\.\d)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 带括号纯数字
            new Regex(@"[\[【(（](?<episodeNumber>\d+(\.\d)?)[\]】)）]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // E01, Ep01, Episode 01
            new Regex(@"\b(E|Ep|Episode\s*)(?<episodeNumber>\d+(\.\d)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 100-man no Inochi no Ue ni Ore wa Tatteiru - 01, Danganronpa 3 - Mirai Hen - 01
            new Regex(@"\s-\s(?<episodeNumber>\d+(\.\d)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // Season 2 - 01, S3 - 02
            new Regex(@"\b(?<=S\d+|Season\s*\d+\b.*?)(?<episodeNumber>\d+(\.\d)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 末尾数字无隔开，如：xxx01.mkv
            new Regex(@"(?<episodeNumber>\d+(\.\d)?)\.\w+$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // 纯数字（容易识别错误，应放在最后）
            new Regex(@"\b(?<episodeNumber>\d+(\.\d)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
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
        /// 中文数字单位及系数
        /// </summary>
        private static readonly Dictionary<char, int> _cnUnitMultiplier = new()
        {
            { '十', 10 },
            { '百', 100 },
            { '千', 1000 },
            { '万', 10000 }
        };

        /// <summary>
        /// 英文数字映射
        /// </summary>
        private static readonly Dictionary<string, int> _enNumber = new()
        {
            { "one", 1 }, { "two", 2 }, { "three", 3 }, { "four", 4 },{ "five", 5 },
            { "six", 6 }, { "seven", 7 }, { "eight", 8 },{ "nine", 9 }, { "ten", 10 }
        };

        /// <summary>
        /// 尝试将中文转换成数字，考虑正常番剧集数，仅支持到万位
        /// </summary>
        /// <param name="cnNumber">中文数字，如：十一、一百零二、一千零一</param>
        /// <param name="number">转换后的数字</param>
        /// <returns>转换是否成功</returns>
        public static bool TryConvertCnNumber(string cnNumber, out double number)
        {
            number = 0;

            if (string.IsNullOrWhiteSpace(cnNumber)) return false;
            cnNumber = cnNumber.Trim();

            int result = 0;
            // 当前正在处理的数字，默认为0。当遇到单位时，如果当前数字为0则默认为1，例如：十 -> 10
            int currentDigit = 0;
            // 是否已经处理过数字，主要用于处理单位前的数字是否默认为1，例如：十 -> 10, 二十 -> 20, 十一 -> 11
            bool hasCurrentDigit = false;
            // 上一个单位的系数
            int lastUnit = int.MaxValue;

            foreach (var ch in cnNumber)
            {
                // 字符是单位的情况
                if (_cnUnitMultiplier.TryGetValue(ch, out var unit))
                {
                    // 如果单位比上一个单位大，说明格式错误，例如：一千零一十（正确） vs 一千零一万（错误）
                    if (unit >= lastUnit)
                    {
                        return false;
                    }

                    // 格式错误，例如：零千、零百、零十
                    if (hasCurrentDigit && currentDigit == 0)
                    {
                        return false;
                    }

                    // 以上个数字作为基数，像十开头的数字则默认为1
                    var baseNumber = hasCurrentDigit ? currentDigit : 1;
                    result += baseNumber * unit;

                    // 重置当前数字和状态，继续处理下一个单位
                    currentDigit = 0;
                    hasCurrentDigit = false;
                    lastUnit = unit;
                    continue;
                }

                // 字符是数字的情况
                var digit = CnNumber.IndexOf(ch);
                // 如果字符既不是数字也不是单位，说明格式错误
                if (digit < 0)
                {
                    return false;
                }

                // 记录当前数字
                currentDigit = digit;
                hasCurrentDigit = true;
            }

            // 个位数非零时加上，例如：三百二十一、十二
            if (hasCurrentDigit)
            {
                result += currentDigit;
            }

            number = result;
            return true;
        }

        /// <summary>
        /// 检查文件夹名是否仅包含季号
        /// </summary>
        /// <param name="foldername">文件夹名</param>
        /// <returns></returns>
        public static bool IsSeasonNumberOnly(string foldername)
        {
            if (string.IsNullOrEmpty(foldername)) return false;

            var (namePart, _) = SplitAnimeTitleAndSeason(foldername, false);

            return string.IsNullOrEmpty(namePart);
        }

        /// <summary>
        /// 将文件名拆分为标题和季号
        /// </summary>
        /// <param name="filename">文件名</param>
        /// <param name="isEpisode">true: 剧集文件名，会先匹配并移除集号，再匹配季号; false: 季目录名，直接匹配季号</param>
        /// <returns></returns>
        public static (string, double?) SplitAnimeTitleAndSeason(string filename, bool isEpisode)
        {
            if (isEpisode)
            {
                var split = SplitAnimeTitleAndEpisode(filename);
                filename = split.Item1;
            }

            // 匹配正则
            filename = filename.Trim();
            Match? match = null;

            // 预处理，删除容易干扰季号识别的数字
            foreach (var regex in _seasonNumberPreprocessRegexes)
            {
                filename = regex.Replace(filename, "");
            }

            // 匹配季号
            foreach (var regex in _seasonNumberRegexes)
            {
                match = regex.Match(filename);
                if (match.Success) break;
            }
            if (match == null || !match.Success) return (filename, null);

            var seasonNumber = match.Groups["seasonNumber"].Value;
            if (string.IsNullOrEmpty(seasonNumber)) return (filename, null);

            // 转换数字
            double? seasonNum = null;
            if (double.TryParse(seasonNumber, out double num)) // 纯数字
            {
                seasonNum = num;
            }
            else if (TryConvertCnNumber(seasonNumber, out var cnNum)) // 中文数字转换
            {
                seasonNum = cnNum;
            }
            else if (_romanNumber.TryGetValue(seasonNumber.ToUpperInvariant(), out var romanNum)) // 罗马数字转换
            {
                seasonNum = romanNum;
            }
            else if (_enNumber.TryGetValue(seasonNumber.ToLowerInvariant(), out var enNum)) // 英文数字转换
            {
                seasonNum = enNum;
            }

            // 提取季号外的部分
            string namePart = filename.Remove(match.Index, match.Length).Trim();
            return (namePart, seasonNum);
        }

        /// <summary>
        /// 提取季号并转换为数字
        /// </summary>
        /// <param name="filename">文件名</param>
        /// <param name="isEpisode">true: 剧集文件名，会先匹配并移除集号，再匹配季号; false: 季目录名，直接匹配季号</param>
        /// <returns>季号数字，没有包含数字时返回null（通常可认为是第1季）</returns>
        public static double? ExtractAnimeSeason(string filename, bool isEpisode)
        {
            var (_, seasonNum) = SplitAnimeTitleAndSeason(filename, isEpisode);
            return seasonNum;
        }

        /// <summary>
        /// 将文件名拆分为标题和集号
        /// </summary>
        /// <param name="filename">文件名</param>
        /// <returns>标题和集号</returns>
        public static (string, double?) SplitAnimeTitleAndEpisode(string filename)
        {
            // 匹配正则
            filename = filename.Trim();
            Match? match = null;
            foreach (var regex in _episodeNumberRegexes)
            {
                match = regex.Match(filename);
                if (match.Success) break;
            }
            if (match == null || !match.Success) return (filename, null);

            var episodeNumber = match.Groups["episodeNumber"].Value;
            if (string.IsNullOrEmpty(episodeNumber)) return (filename, null);

            // 转换数字
            double? episodeNum = null;
            if (double.TryParse(episodeNumber, out double num)) // 纯数字
            {
                episodeNum = num;
            }
            else if (TryConvertCnNumber(episodeNumber, out var cnNum)) // 中文数字转换
            {
                episodeNum = cnNum;
            }

            // 提取集号外的部分
            string namePart = filename.Remove(match.Index, match.Length).Trim();
            return (namePart, episodeNum);
        }

        /// <summary>
        /// 提取集号并转换为数字
        /// </summary>
        /// <param name="filename">文件名</param>
        /// <returns>集号数字，没有包含数字时返回null</returns>
        public static double? ExtractAnimeEpisodeNumber(string filename)
        {
            var (_, episodeNum) = SplitAnimeTitleAndEpisode(filename);
            return episodeNum;
        }

        /// <summary>
        /// 从文件夹路径获取有效的番剧名称用于搜索，包含季号
        /// </summary>
        /// <param name="folderPath">文件夹路径</param>
        /// <returns></returns>
        public static (string?, int?) GetValidAnimeTitleAndSeason(string folderPath)
        {
            var folderName = Path.GetFileName(folderPath);

            // Anitomy 只能获取部分数字季号，使用其他方法获取季号
            var split = SplitAnimeTitleAndSeason(folderName, false);
            var result = ((string?)split.Item1, (int?)split.Item2);

            var anitomy = new Anitomy(result.Item1 ?? folderName);
            // 使用 Anitomy 清理文件名
            result.Item1 = anitomy.ExtractAnimeTitle();

            // 纠正季号，避免降低匹配度
            if (result.Item2 != null && result.Item2 < 1)
                result.Item2 = null;

            return result;
        }
    }
}
