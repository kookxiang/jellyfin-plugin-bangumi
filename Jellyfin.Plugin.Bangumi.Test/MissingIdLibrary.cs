using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissingIdController = Jellyfin.Plugin.Bangumi.Tools.MissingBangumiId.Controller;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class MissingIdLibraryTestCases
{
    [DataTestMethod]
    [DataRow("/media/anime/series/episode.mkv", "/media/anime", true)]
    [DataRow("/media/anime-other/episode.mkv", "/media/anime", false)]
    [DataRow("/media/anime/../movie/episode.mkv", "/media/anime", false)]
    [DataRow(null, "/media/anime", false)]
    [DataRow("/media/anime/episode.mkv", "", false)]
    public void RestrictsResultsToLibraryDirectory(string? path, string location, bool expected)
    {
        Assert.AreEqual(expected, MissingIdController.IsInLibrary(path, location));
    }
}
