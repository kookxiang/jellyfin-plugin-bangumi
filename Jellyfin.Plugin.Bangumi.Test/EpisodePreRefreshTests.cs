using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Providers;
using MediaBrowser.Controller.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class EpisodePreRefreshTests
{
    [TestMethod]
    public void CorrectsCodecNumberAndStaleSpecialSeason()
    {
        var info = new EpisodeInfo
        {
            Path = "/anime/Season 1/[Nekomoe kissaten] Arifureta Shokugyou de Sekai Saikyou 02 [BDRip 1080p HEVC-10bit FLAC].mkv",
            IndexNumber = 10,
            ParentIndexNumber = 0,
            IndexNumberEnd = 13
        };
        Assert.IsTrue(EpisodePreRefreshProvider.CorrectEpisodeNumbers(info, new LocalConfiguration()));
        Assert.AreEqual(2, info.IndexNumber);
        Assert.AreEqual(1, info.ParentIndexNumber);
        Assert.IsNull(info.IndexNumberEnd);
    }

    [TestMethod]
    public void HonorsForcedSpecialType()
    {
        var info = new EpisodeInfo { Path = "/anime/Season 1/Show - 02.mkv" };
        Assert.IsTrue(EpisodePreRefreshProvider.CorrectEpisodeNumbers(info,
            new LocalConfiguration { Type = DirectoryType.Special }));
        Assert.AreEqual(0, info.ParentIndexNumber);
    }

    [TestMethod]
    [DataRow("Show.mkv", false)]
    [DataRow("Show - 01-02.mkv", false)]
    [DataRow("Show - 01.5.mkv", false)]
    [DataRow("Show - 02.mkv", true)]
    public void LeavesUncertainOrSkippedEpisodesAlone(string name, bool skip)
    {
        var info = new EpisodeInfo { Path = "/anime/" + name, IndexNumber = 7, ParentIndexNumber = 3 };
        Assert.IsFalse(EpisodePreRefreshProvider.CorrectEpisodeNumbers(info, new LocalConfiguration { Skip = skip }));
        Assert.AreEqual(7, info.IndexNumber);
        Assert.AreEqual(3, info.ParentIndexNumber);
    }
}
