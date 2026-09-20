using System.Threading;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Parser;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;
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

    private static double Extract(string filename, bool replace, int existingIndex, int offset)
    {
        var context = new EpisodeParserContext(
            ServiceLocator.GetService<BangumiApi>(),
            ServiceLocator.GetService<ILibraryManager>(),
            new EpisodeInfo { Path = filename, IndexNumber = existingIndex },
            ServiceLocator.GetService<IMediaSourceManager>(),
            new PluginConfiguration { AlwaysReplaceEpisodeNumber = replace },
            new Model.LocalConfiguration { Offset = offset },
            CancellationToken.None);
        return BasicEpisodeParser.ExtractEpisodeNumberFromPath(context, ServiceLocator.GetService<Logger<BasicEpisodeParser>>());
    }
}
