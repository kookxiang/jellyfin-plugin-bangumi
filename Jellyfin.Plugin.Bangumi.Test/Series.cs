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
public class Series
{
    private readonly BangumiApi _api = ServiceLocator.GetService<BangumiApi>();
    private readonly SubjectImageProvider _imageProvider = ServiceLocator.GetService<SubjectImageProvider>();
    private readonly Bangumi.Plugin _plugin = ServiceLocator.GetService<Bangumi.Plugin>();
    private readonly SeriesProvider _provider = ServiceLocator.GetService<SeriesProvider>();

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
        var result = await _provider.GetMetadata(new SeriesInfo
        {
            Name = "White Album 2",
            Path = FakePath.Create("White Album 2")
        }, _token);
        AssertSeries(result);
    }

    [TestMethod]
    public async Task GetById()
    {
        var result = await _provider.GetMetadata(new SeriesInfo
        {
            Name = "White Album 2",
            Path = FakePath.Create("White Album 2"),
            ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "69496" } }
        }, _token);
        AssertSeries(result);
    }

    [TestMethod]
    public async Task SearchByName()
    {
        var searchResults = await _provider.GetSearchResults(new SeriesInfo
        {
            Name = "White Album 2",
            Path = FakePath.Create("White Album 2")
        }, _token);
        Assert.IsTrue(searchResults.Any(x => x.ProviderIds[Constants.ProviderName].Equals("69496")), "should have correct search result");
    }

    [TestMethod]
    public async Task GetNameByAnitomySharp()
    {
        _plugin.Configuration.AlwaysGetTitleByAnitomySharp = true;
        var result = await _provider.GetMetadata(new SeriesInfo
        {
            Name = "[Airota&LoliHouse] Toaru Kagaku no Railgun T [BDRip 1080p HEVC-10bit FLAC]",
            Path = FakePath.Create("[Airota&LoliHouse] Toaru Kagaku no Railgun T [BDRip 1080p HEVC-10bit FLAC]")
        }, _token);
        _plugin.Configuration.AlwaysGetTitleByAnitomySharp = false;
        Assert.AreEqual("とある科学の超電磁砲T", result.Item.Name, "should return correct series name");
    }

    [TestMethod]
    public async Task SearchById()
    {
        var searchResults = await _provider.GetSearchResults(new SeriesInfo
        {
            Path = FakePath.Create("White Album 2"),
            ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "69496" } }
        }, _token);
        Assert.IsTrue(searchResults.Any(x => x.ProviderIds[Constants.ProviderName].Equals("69496")), "should have correct search result");
    }

    [TestMethod]
    public async Task SortSearchResult()
    {
        var searchResults = await _api.SearchSubject("マジンガーZ", _token);
        Assert.AreEqual(searchResults.First().Id, 10390, "should return most similar item as first");

        searchResults = await _api.SearchSubject("ガンダムビルドファイターズトライ", _token);
        Assert.AreEqual(searchResults.First().Id, 105875, "should return most similar item as first");
    }

    [TestMethod]
    public async Task ImageProvider()
    {
        var series = new MediaBrowser.Controller.Entities.TV.Series();
        Assert.AreEqual(-5, _imageProvider.Order, "should have provider order: -5");
        Assert.AreEqual(Constants.PluginName, _imageProvider.Name, "should have provider name");
        Assert.IsTrue(_imageProvider.Supports(series), "should support series image");
        Assert.AreEqual(ImageType.Primary, _imageProvider.GetSupportedImages(series).First(), "should support primary image");
        var imgList = await _imageProvider.GetImages(new MediaBrowser.Controller.Entities.TV.Episode
        {
            ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "69496" } }
        }, _token);
        Assert.IsTrue(imgList.Any(), "should return at least one image");
    }

    private static void AssertSeries(MetadataResult<MediaBrowser.Controller.Entities.TV.Series> result)
    {
        Assert.IsNotNull(result.Item, "series data should not be null");
        Assert.AreEqual("WHITE ALBUM2", result.Item.Name, "should return correct series name");
        Assert.AreNotEqual("", result.Item.Overview, "should return series overview");
        Assert.AreEqual("2013-10-05", result.Item.AirTime, "should return correct air time info");
        Assert.AreEqual(DayOfWeek.Saturday, result.Item.AirDays?[0], "should return correct air day info");
        Assert.IsTrue(result.Item.CommunityRating is > 0 and <= 10, "should return rating info");
        Assert.IsNotNull(result.People.Find(x => x.IsType(PersonType.Actor)), "should have at least one actor");
        Assert.IsNotNull(result.People.Find(x => x.IsType(PersonType.Director)), "should have at least one director");
        Assert.IsNotNull(result.People.Find(x => x.IsType(PersonType.Writer)), "should have at least one writer");
        Assert.AreNotEqual("", result.People?[0].ImageUrl, "person should have image url");
        Assert.IsNotNull(result.Item.ProviderIds[Constants.ProviderName], "should have plugin provider id");
    }
}