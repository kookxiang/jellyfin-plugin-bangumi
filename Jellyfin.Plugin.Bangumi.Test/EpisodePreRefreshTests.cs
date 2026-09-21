using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JellyfinEpisode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class EpisodePreRefreshTests
{
    [DataTestMethod]
    [DataRow(EpisodeParserType.Basic, true)]
    [DataRow(EpisodeParserType.AnitomySharp, true)]
    [DataRow(EpisodeParserType.Torrent, true)]
    [DataRow(EpisodeParserType.Basic, false)]
    [DataRow(EpisodeParserType.AnitomySharp, false)]
    [DataRow(EpisodeParserType.Torrent, false)]
    public async Task VersionWorkaroundDoesNotOverwriteEpisodeNumbers(EpisodeParserType parser, bool enabled)
    {
        var configuration = ServiceLocator.GetService<Bangumi.Plugin>().Configuration;
        var previousParser = configuration.EpisodeParser;
        var previousEnabled = configuration.MergeEpisodeVersionsByBangumiId;
        try
        {
            configuration.EpisodeParser = parser;
            configuration.MergeEpisodeVersionsByBangumiId = enabled;
            var item = new JellyfinEpisode
            {
                Path = "/anime/White Album 2[01][Hi10p_1080p][BDRip][x264_2flac].mkv",
                IndexNumber = 10,
                IndexNumberEnd = 11,
                ParentIndexNumber = 1
            };
            var result = await new EpisodePreRefreshProvider().FetchAsync(item, null!, CancellationToken.None);
            Assert.AreEqual(10, item.IndexNumber);
            Assert.AreEqual(11, item.IndexNumberEnd);
            Assert.IsNull(item.ParentIndexNumber, "The selected metadata parser still determines the season.");
            Assert.AreEqual(ItemUpdateType.None, result);
        }
        finally
        {
            configuration.EpisodeParser = previousParser;
            configuration.MergeEpisodeVersionsByBangumiId = previousEnabled;
        }
    }
}
