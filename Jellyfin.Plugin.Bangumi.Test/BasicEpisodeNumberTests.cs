using System.Threading;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Parser;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class BasicEpisodeNumberTests
{
    private const string HanzawaFile = "Hanzawa.Naoki.S02E03.2020.Friday.WEB-DL.1080p.H264.AAC-AREY.mkv";

    [DataTestMethod]
    [DataRow(HanzawaFile, 3d)]
    [DataRow("Show.S02E03.1999.WEB-DL.mkv", 3d)]
    [DataRow("Show.s02e03.2024.WEB-DL.mkv", 3d)]
    [DataRow("Show.EP03.2020.WEB-DL.mkv", 3d)]
    [DataRow("Show - 03.2020.WEB-DL.mkv", 3d)]
    [DataRow("Show #03.2020.WEB-DL.mkv", 3d)]
    [DataRow("Show.EP2020.mkv", 2020d)]
    [DataRow("Show.S02E03.1080p.mkv", 3d)]
    [DataRow("Show.EP12.5.mkv", 12.5d)]
    [DataRow("Show.S02E12.5.2020.WEB-DL.mkv", 12.5d)]
    [DataRow("Show [14.5][1080p].mkv", 14.5d)]
    [DataRow("Show - 12.5 [1080p].mkv", 12.5d)]
    public void FilenameEpisodeDoesNotIncludeReleaseYear(string filename, double expected)
    {
        Assert.AreEqual(expected, Extract(filename, true, 99, 0));
    }

    [DataTestMethod]
    [DataRow(false, 0, 0, 3d)]
    [DataRow(false, 7, 0, 7d)]
    [DataRow(true, 7, 2, 1d)]
    public void NumberingPreferencesStillApply(bool replace, int existingIndex, int offset, double expected)
    {
        Assert.AreEqual(expected, Extract(HanzawaFile, replace, existingIndex, offset));
    }

    [TestMethod]
    public void FileSelectorOffsetAppliesToParsingAndDisplay()
    {
        var config = new LocalConfiguration
        {
            Offset = 3,
            Sections = [new LocalConfigurationSection { Selector = "[某字幕组][**].mp4", Offset = 26 }],
        };
        var matching = "[某字幕组][27].mp4";
        var other = "[其他字幕组][03].mp4";
        Assert.AreEqual(1d, Extract(matching, true, 0, config));
        Assert.AreEqual(0d, Extract(other, true, 0, config));
        Assert.AreEqual(27, LocalConfigurationHelper.GetDisplayEpisodeIndex(1, config, matching));
        Assert.AreEqual(4, LocalConfigurationHelper.GetDisplayEpisodeIndex(1, config, other));
    }

    private static double Extract(string filename, bool replace, int existingIndex, int offset)
        => Extract(filename, replace, existingIndex, new LocalConfiguration { Offset = offset });

    private static double Extract(string filename, bool replace, int existingIndex, LocalConfiguration localConfiguration)
    {
        var context = new EpisodeParserContext(
            ServiceLocator.GetService<BangumiApi>(),
            ServiceLocator.GetService<ILibraryManager>(),
            new EpisodeInfo { Path = filename, IndexNumber = existingIndex },
            ServiceLocator.GetService<IMediaSourceManager>(),
            new PluginConfiguration { AlwaysReplaceEpisodeNumber = replace },
            localConfiguration,
            CancellationToken.None);
        return BasicEpisodeParser.ExtractEpisodeNumberFromPath(context, ServiceLocator.GetService<Logger<BasicEpisodeParser>>());
    }
}
