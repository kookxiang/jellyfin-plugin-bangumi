using Microsoft.VisualStudio.TestTools.UnitTesting;
using Jellyfin.Plugin.Bangumi.Utils;

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
                Assert.IsFalse(ok,$"{input} 应该失败");
            }
        }
    }
}
