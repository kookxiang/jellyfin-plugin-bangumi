using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Providers;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test
{
    [TestClass]
    public class Episode
    {
        private readonly EpisodeProvider _provider = new(new TestApplicationPaths(), new NullLogger<EpisodeProvider>());

        private readonly CancellationToken _token = new();

        [TestMethod]
        public async Task EpisodeInfo()
        {
            var episodeData = await _provider.GetMetadata(new EpisodeInfo
            {
                Path = "/FakePath/White Album 2[01][Hi10p_1080p][BDRip][x264_2flac].mkv",
                ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "259013" } },
                SeriesProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "69496" } }
            }, _token);
            Assert.IsNotNull(episodeData, "episode data should not be null");
            Assert.IsNotNull(episodeData.Item, "episode data should not be null");
            Assert.AreEqual("WHITE ALBUM", episodeData.Item.Name, "should return the right episode title");
        }

        [TestMethod]
        public async Task EpisodeInfoWithoutId()
        {
            var episodeData = await _provider.GetMetadata(new EpisodeInfo
            {
                Path = "/FakePath/White Album 2[01][Hi10p_1080p][BDRip][x264_2flac].mkv",
                SeriesProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "69496" } }
            }, _token);
            Assert.IsNotNull(episodeData, "episode data should not be null");
            Assert.IsNotNull(episodeData.Item, "episode data should not be null");
            Assert.AreEqual("WHITE ALBUM", episodeData.Item.Name, "should return the right episode title");
        }

        [TestMethod]
        public async Task FixEpisodeIndex()
        {
            var episodeData = await _provider.GetMetadata(new EpisodeInfo
            {
                IndexNumber = 1080,
                Path = "/FakePath/White Album 2[01][Hi10p_1080p][BDRip][x264_2flac].mkv",
                SeriesProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "69496" } }
            }, _token);
            Assert.IsNotNull(episodeData, "episode data should not be null");
            Assert.IsNotNull(episodeData.Item, "episode data should not be null");
            Assert.AreEqual(1, episodeData.Item.IndexNumber, "should fix episode index automatically");
            Assert.AreEqual("WHITE ALBUM", episodeData.Item.Name, "should return the right episode title");
        }

        [TestMethod]
        public async Task FixEpisodeIndexWithoutCount()
        {
            var episodeData = await _provider.GetMetadata(new EpisodeInfo
            {
                IndexNumber = 1080,
                Path = "/FakePath/Asobi Asobase [12][Ma10p_1080p][x265_flac_aac].mkv",
                SeriesProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "236020" } }
            }, _token);
            Assert.IsNotNull(episodeData, "episode data should not be null");
            Assert.IsNotNull(episodeData.Item, "episode data should not be null");
            Assert.AreEqual(12, episodeData.Item.IndexNumber, "should fix episode index automatically");
            Assert.AreEqual("「ダニエル」「ブラ会議」「メルヘン・バトルロワイヤル」 「紙のみぞ戦争」", episodeData.Item.Name, "should return the right episode title");
        }

        [TestMethod]
        public async Task FixEpisodeIndexWithNumberInName()
        {
            var episodeData = await _provider.GetMetadata(new EpisodeInfo
            {
                IndexNumber = 0,
                Path = "/FakePath/Steins;Gate 0 [23][Ma10p_1080p][x265_flac].mkv",
                SeriesProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "129807" } }
            }, _token);
            Assert.IsNotNull(episodeData, "episode data should not be null");
            Assert.IsNotNull(episodeData.Item, "episode data should not be null");
            Assert.AreEqual(23, episodeData.Item.IndexNumber, "should fix episode index automatically");

            episodeData = await _provider.GetMetadata(new EpisodeInfo
            {
                IndexNumber = 0,
                Path = "/FakePath/Log Horizon 2 [08][Ma10p_1080p][x265_flac].mkv",
                SeriesProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "100517" } }
            }, _token);
            Assert.IsNotNull(episodeData, "episode data should not be null");
            Assert.IsNotNull(episodeData.Item, "episode data should not be null");
            Assert.AreEqual(8, episodeData.Item.IndexNumber, "should fix episode index automatically");
        }
        
        [TestMethod]
        public async Task FixIncorrectEpisodeId()
        {
            var episodeData = await _provider.GetMetadata(new EpisodeInfo
            {
                IndexNumber = 1080,
                Path = "/FakePath/Saki [01] [Hi10p_720p][BDRip][x264_flac].mkv",
                ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "162427" } },
                SeriesProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "1444" } }
            }, _token);
            Assert.IsNotNull(episodeData, "episode data should not be null");
            Assert.IsNotNull(episodeData.Item, "episode data should not be null");
            Assert.AreEqual("5168", episodeData.Item.ProviderIds[Constants.ProviderName], "should return the correct episode id");
            Assert.AreEqual("出会い", episodeData.Item.Name, "should return the correct episode title");
            Assert.AreEqual(1, episodeData.Item.IndexNumber, "should fix episode index automatically");
        }

        [TestMethod]
        public async Task SpecialEpisodeInDifferentSubject()
        {
            var episodeData = await _provider.GetMetadata(new EpisodeInfo
            {
                IndexNumber = 1080,
                Path = "/FakePath/Yahari Ore no Seishun Lovecome wa Machigatte Iru. Zoku [OVA][Ma10p_1080p][x265_flac].mkv",
                ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "555794" } },
                SeriesProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "102134" } }
            }, _token);
            Assert.IsNotNull(episodeData, "episode data should not be null");
            Assert.IsNotNull(episodeData.Item, "episode data should not be null");
            Assert.AreEqual("きっと、女の子はお砂糖とスパイスと素敵な何かでできている。", episodeData.Item.Name, "should return the correct episode title");
            Assert.AreEqual(1, episodeData.Item.IndexNumber, "should fix episode index automatically");
        }
    }
}