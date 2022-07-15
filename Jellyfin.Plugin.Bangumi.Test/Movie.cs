using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class Movie
{
    private readonly Bangumi.Plugin _plugin = ServiceLocator.GetService<Bangumi.Plugin>();
    private readonly MovieProvider _provider = ServiceLocator.GetService<MovieProvider>();

    private readonly CancellationToken _token = new();

    [TestMethod]
    public void ProviderInfo()
    {
        Assert.AreEqual(_provider.Name, Constants.ProviderName);
        Assert.IsTrue(_provider.Order < 0);
    }

    [TestMethod]
    public async Task GetByName()
    {
        var result = await _provider.GetMetadata(new MovieInfo
        {
            Name = "STEINS;GATE 負荷領域のデジャヴ"
        }, _token);
        AssertMovie(result);
    }

    [TestMethod]
    public async Task GetById()
    {
        var result = await _provider.GetMetadata(new MovieInfo
        {
            ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "23119" } }
        }, _token);
        AssertMovie(result);
    }

    [TestMethod]
    public async Task SearchByName()
    {
        var searchResults = await _provider.GetSearchResults(new MovieInfo
        {
            Name = "STEINS;GATE 負荷領域のデジャヴ"
        }, _token);
        Assert.IsTrue(searchResults.Any(x => x.ProviderIds[Constants.ProviderName].Equals("23119")), "should have correct search result");
    }

    [TestMethod]
    public async Task GetNameByAnitomySharp()
    {
        _plugin.Configuration.AlwaysGetTitleByAnitomySharp = true;
        var result = await _provider.GetMetadata(new MovieInfo
        {
            Name = "[Zagzad] Memories (BDRip 1764x972 1800x976 1788x932 HEVC-10bit THD)",
            Path = FakePath.Create("[Zagzad] Memories (BDRip 1764x972 1800x976 1788x932 HEVC-10bit THD)")
        }, _token);
        _plugin.Configuration.AlwaysGetTitleByAnitomySharp = false;
        Assert.AreEqual("回忆三部曲", result.Item.Name, "should return correct series name");
    }

    [TestMethod]
    public async Task SearchById()
    {
        var searchResults = await _provider.GetSearchResults(new MovieInfo
        {
            ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "23119" } }
        }, _token);
        Assert.IsTrue(searchResults.Any(x => x.ProviderIds[Constants.ProviderName].Equals("23119")), "should have correct search result");
    }

    private static void AssertMovie(MetadataResult<MediaBrowser.Controller.Entities.Movies.Movie> result)
    {
        Assert.IsNotNull(result.Item, "series data should not be null");
        Assert.AreEqual("命运石之门 负荷领域的既视感", result.Item.Name, "should return correct series name");
        Assert.AreNotEqual("", result.Item.Overview, "should return series overview");
        Assert.AreEqual(DateTime.Parse("2013-04-20"), result.Item.PremiereDate, "should return correct premiere date");
        Assert.IsTrue(result.Item.CommunityRating is > 0 and <= 10, "should return rating info");
        Assert.IsNotNull(result.People.Find(x => x.IsType(PersonType.Actor)), "should have at least one actor");
        Assert.IsNotNull(result.People.Find(x => x.IsType(PersonType.Director)), "should have at least one director");
        Assert.IsNotNull(result.People.Find(x => x.IsType(PersonType.Writer)), "should have at least one writer");
        Assert.AreNotEqual("", result.People?[0].ImageUrl, "person should have image url");
        Assert.AreEqual("23119", result.Item.ProviderIds[Constants.ProviderName], "should have plugin provider id");
    }
}