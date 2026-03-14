using System.IO;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
using Jellyfin.Plugin.Bangumi.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Utils.Tests
{
    [TestClass()]
    public class FileNameParserTests
    {
        [TestMethod()]
        public void SplitAnimeTitleAndSeasonTest()
        {
            var result = FileNameParser.SplitAnimeTitleAndSeason("[アニメ BD] 魔法少女リリカルなのはStrikerS(第3期) 第21話「決戦」(1912x1068 HEVC 10bit FLAC softSub(chs+cht+eng) chap).mkv", true);
            Assert.AreEqual("[アニメ BD] 魔法少女リリカルなのはStrikerS() 「決戦」(1912x1068 HEVC 10bit FLAC softSub(chs+cht+eng) chap).mkv", result.Item1);
            Assert.AreEqual(3, result.Item2);

            result = FileNameParser.SplitAnimeTitleAndSeason("[Pussub&VCB-Studio] White Album 2 [Hi10p_1080p]", false);
            Assert.AreEqual("[Pussub&VCB-Studio] White Album  [Hi10p_1080p]", result.Item1);
            Assert.AreEqual(2, result.Item2);

            result = FileNameParser.SplitAnimeTitleAndSeason("Arifureta S02E01-[1080p][BDRIP][x265.FLAC].mkv", true);
            Assert.AreEqual("Arifureta -[1080p][BDRIP][x265.FLAC].mkv", result.Item1);
            Assert.AreEqual(2, result.Item2);
        }

        [TestMethod()]
        public void SplitAnimeTitleAndEpisodeTest()
        {
            var result = FileNameParser.SplitAnimeTitleAndEpisode("[アニメ BD] 魔法少女リリカルなのはStrikerS(第3期) 第21話「決戦」(1912x1068 HEVC 10bit FLAC softSub(chs+cht+eng) chap).mkv");
            Assert.AreEqual("[アニメ BD] 魔法少女リリカルなのはStrikerS(第3期) 「決戦」(1912x1068 HEVC 10bit FLAC softSub(chs+cht+eng) chap).mkv", result.Item1);
            Assert.AreEqual(21, result.Item2);

            result = FileNameParser.SplitAnimeTitleAndEpisode("Arifureta S02E01-[1080p][BDRIP][x265.FLAC].mkv");
            Assert.AreEqual("Arifureta S02-[1080p][BDRIP][x265.FLAC].mkv", result.Item1);
            Assert.AreEqual(1, result.Item2);

            result = FileNameParser.SplitAnimeTitleAndEpisode("[Nekomoe kissaten&VCB-Studio] Ayakashi Triangle [01][Ma10p_1080p][x265_flac_aac].mkv");
            Assert.AreEqual("[Nekomoe kissaten&VCB-Studio] Ayakashi Triangle [Ma10p_1080p][x265_flac_aac].mkv", result.Item1);
            Assert.AreEqual(1, result.Item2);

            result = FileNameParser.SplitAnimeTitleAndEpisode("[Magic-Raws] 魔女之旅 EP01 (BD 1920x1080 x265 FLAC).mkv");
            Assert.AreEqual("[Magic-Raws] 魔女之旅  (BD 1920x1080 x265 FLAC).mkv", result.Item1);
            Assert.AreEqual(1, result.Item2);

            result = FileNameParser.SplitAnimeTitleAndEpisode("[ReinForce] Steins;Gate - 01 (BD 1920x1080 x264 FLAC).mkv");
            Assert.AreEqual("[ReinForce] Steins;Gate (BD 1920x1080 x264 FLAC).mkv", result.Item1);
            Assert.AreEqual(1, result.Item2);

            result = FileNameParser.SplitAnimeTitleAndEpisode("[Snow-Raws] 這いよれ! ニャル子さんW 第01話 (BD 1920x1080 HEVC-YUV420P10 FLACx2).mkv");
            Assert.AreEqual("[Snow-Raws] 這いよれ! ニャル子さんW  (BD 1920x1080 HEVC-YUV420P10 FLACx2).mkv", result.Item1);
            Assert.AreEqual(1, result.Item2);

            result = FileNameParser.SplitAnimeTitleAndEpisode("[VCB-Studio] Sword Art Online II [14.5][Ma10p_1080p][x265_flac].mkv");
            Assert.AreEqual("[VCB-Studio] Sword Art Online II [Ma10p_1080p][x265_flac].mkv", result.Item1);
            Assert.AreEqual(14.5, result.Item2);
        }

        [TestMethod()]
        public void TryConvertCnNumberTest()
        {
            var successCases = new (string Input, double Expected)[]
            {
                ("零", 0),
                ("十", 10),
                ("十二", 12),
                ("二十", 20),
                ("二十三", 23),
                ("一百", 100),
                ("一百零二", 102),
                ("一千", 1000),
                ("一千零一", 1001),
                ("九千九百九十九", 9999)
            };

            foreach (var (input, expected) in successCases)
            {
                var ok = FileNameParser.TryConvertCnNumber(input, out var number);
                Assert.IsTrue(ok);
                Assert.AreEqual(expected, number);
            }

            var failCases = new[]
            {
                string.Empty,
                " ",
                "十百",
                "abc",
                "一A二"
            };

            foreach (var input in failCases)
            {
                var ok = FileNameParser.TryConvertCnNumber(input, out _);
                Assert.IsFalse(ok, $"{input} 应该失败");
            }
        }

        [TestMethod()]
        public void GetValidAnimeTitleAndSeason()
        {
            var path = "[VCB-Studio] Log Horizon 2 [Ma10p_1080p]";
            var result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("Log Horizon", result.Item1);
            Assert.AreEqual(2, result.Item2);
            result = GetByPureAnitomy(path);
            Assert.AreNotEqual(2, result.Item2);

            path = "[VCB-Studio] OVERLORD IV [Ma10p_1080p]";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("OVERLORD", result.Item1);
            Assert.AreEqual(4, result.Item2);
            result = GetByPureAnitomy(path);
            Assert.AreNotEqual(4, result.Item2);

            path = "Tales of the Abyss S01 1080p BDRip 10 bits DD x265-EMBER";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("Tales of the Abyss", result.Item1);
            Assert.AreEqual(1, result.Item2);
            result = GetByPureAnitomy(path);
            Assert.AreNotEqual(1, result.Item2);

            path = "Sorcerous Stabber Orphen S02 1080p Dual Audio BDRip 10 bits DD x265-EMBER";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("Sorcerous Stabber Orphen", result.Item1);
            Assert.AreEqual(2, result.Item2);
            result = GetByPureAnitomy(path);
            Assert.AreNotEqual(2, result.Item2);

            path = "The Faraway Paladin S02 1080p Dual Audio WEBRip AAC x265-EMBER";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("The Faraway Paladin", result.Item1);
            Assert.AreEqual(2, result.Item2);
            result = GetByPureAnitomy(path);
            Assert.AreNotEqual(2, result.Item2);

            path = "魔笛MAGI 魔奇少年 外传 辛巴德的冒险 Magi Sinbad no Bouken(2016)[BDRIP][1920x1080][TV13+OVA5+NCoped][x264_m4a][10bit]加刘景长压制";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual(null, result.Item2);

            path = "Lucky Star 2007 [BD 1920x1080 HEVC FLAC] - LittleBakas!";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("Lucky Star", result.Item1);
            Assert.AreEqual(null, result.Item2);

            path = "[アニメ BD] シゴフミ 全13話+特典+Scans (1920x1080 x264 10bit FLACx4 softSub(chi+eng) chap @Yurichan.org)";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("シゴフミ", result.Item1);
            Assert.AreEqual(null, result.Item2);

            path = "[VCB-Studio] Fate Stay Night 2006 [Ma10p_1080p]";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("Fate Stay Night", result.Item1);
            Assert.AreEqual(null, result.Item2);

            path = "[UCCUSS] Gekijouban Seitokai Yakuindomo 2 劇場版 生徒会役員共2 SYD2#26 (BD 1920x1080p AVC FLACx2 AAC)";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.IsNotNull(result.Item1);
            Assert.AreEqual(2, result.Item2);
            result = GetByPureAnitomy(path);
            Assert.AreNotEqual(2, result.Item2);

            path = "[UCCUSS] Jigoku Shoujo Yoi no Togi 地獄少女 宵伽 全6話 (BD 1920x1080p AVC FLAC)";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("Jigoku Shoujo Yoi no Togi 地獄少女 宵伽", result.Item1);
            Assert.AreEqual(null, result.Item2);

            path = "[SweetSub] Horimiya [01-13][BDRip 1080P HEVC-10bit FLAC CHS&CHT]";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("Horimiya", result.Item1);
            Assert.AreEqual(null, result.Item2);

            path = "[smplstc] Outbreak Company (BD 1080p x265 10-Bit Opus) [Dual-Audio]";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("Outbreak Company", result.Item1);
            Assert.AreEqual(null, result.Item2);

            path = "[MSRSub&Todokoi] Seitokai Yakuindomo S2 OAD - 25 [DVDRip 720p HEVC-10bit AC3]";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual(2, result.Item2);
            result = GetByPureAnitomy(path);
            Assert.AreNotEqual(2, result.Item2);

            path = "[mawen1250&VCB-Studio] Toradora! [Hi10p_1080p]";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("Toradora!", result.Item1);
            Assert.AreEqual(null, result.Item2);

            path = "[Magic-Raws] 宇宙战舰大和号2205 新的旅程";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.IsNotNull(result.Item1);
            Assert.AreEqual(null, result.Item2);

            path = "[Judas] Mahoromatic (Automatic Maiden) (Seasons 1-2 + OVA) [BD 1080p][HEVC x265 10bit][Dual-Audio][Eng-Subs]";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual(null, result.Item2);

            path = "[DMG] 冴えない彼女の育てかた [BDRip][S1+S2+MOVIE]";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("冴えない彼女の育てかた", result.Item1);
            Assert.AreEqual(1, result.Item2);
            result = GetByPureAnitomy(path);
            Assert.AreNotEqual(1, result.Item2);

            path = "[DBD-Raws][哥布林杀手 第二季][01-12TV全集][日版][1080P][BDRip][HEVC-10bit][简繁外挂][FLAC][MKV]";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("哥布林杀手", result.Item1);
            Assert.AreEqual(2, result.Item2);
            result = GetByPureAnitomy(path);
            Assert.AreNotEqual(2, result.Item2);

            path = "[BDrip] Tenten Kakumei S01 [Sakurato & 7³ACG]";
            result = FileNameParser.GetValidAnimeTitleAndSeason(path);
            Assert.AreEqual("Tenten Kakumei", result.Item1);
            Assert.AreEqual(1, result.Item2);
            result = GetByPureAnitomy(path);
            Assert.AreNotEqual(1, result.Item2);

            static (string?, int?) GetByPureAnitomy(string folderPath)
            {
                var folderName = Path.GetFileName(folderPath);

                var anitomy = new Anitomy(folderName);
                var searchName = anitomy.ExtractAnimeTitle();

                (string?, int?) result = (searchName, null);

                if (int.TryParse(anitomy.ExtractAnimeSeason(), out var season))
                {
                    result.Item2 = season;
                }

                return result;
            }
        }
    }
}
