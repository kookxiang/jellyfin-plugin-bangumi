using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class Season
{
    private readonly BangumiApi _api = ServiceLocator.GetService<BangumiApi>();
    private readonly SubjectImageProvider _imageProvider = ServiceLocator.GetService<SubjectImageProvider>();
    private readonly SeasonProvider _provider = ServiceLocator.GetService<SeasonProvider>();
    private readonly ILibraryManager _libraryManager = ServiceLocator.GetService<ILibraryManager>();

    private readonly CancellationToken _token = new();

    [TestMethod]
    public void ProviderInfo()
    {
        Assert.AreEqual(_provider.Name, Constants.ProviderName);
        Assert.IsTrue(_provider.Order < 0);
    }

    [TestMethod]
    public async Task WithSeasonFolder()
    {
        var result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create("White Album 2/Season 1"),
            ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "69496" } }
        },
            _token);
        Assert.IsTrue(result.HasMetadata, "should return metadata when folder name contains season");
    }

    [TestMethod]
    public async Task GuessNextSeason()
    {
        var subject = await _api.SearchNextSubject(135275, _token);
        Assert.AreEqual(174043, subject?.Id, "can guess next season by subject id");

        subject = await _api.SearchNextSubject(152091, _token);
        Assert.AreEqual(283643, subject?.Id, "Can guess next TV season with BFS");

        subject = await _api.SearchNextSubject(174043, _token);
        Assert.AreNotEqual(220631, subject?.Id, "should skip movie");
        Assert.AreEqual(342667, subject?.Id, "can guess next season by subject id");

        subject = await _api.SearchNextSubject(28900, _token);
        Assert.AreNotEqual(99796, subject?.Id, "should skip OVA");
        Assert.AreNotEqual(136311, subject?.Id, "should skip OVA but with wrong metadata in Bangumi");
        Assert.AreEqual(127573, subject?.Id, "can guess next season by subject id");
    }

    [TestMethod]
    public async Task ImageProvider()
    {
        var season = new MediaBrowser.Controller.Entities.TV.Season();
        Assert.AreEqual(-5, _imageProvider.Order, "should have provider order: -5");
        Assert.AreEqual(Constants.PluginName, _imageProvider.Name, "should have provider name");
        Assert.IsTrue(_imageProvider.Supports(season), "should support series image");
        Assert.AreEqual(ImageType.Primary, _imageProvider.GetSupportedImages(season).First(), "should support primary image");
        var imgList = await _imageProvider.GetImages(new MediaBrowser.Controller.Entities.TV.Season { ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "69496" } } }, _token);
        Assert.IsTrue(imgList.Any(), "should return at least one image");
    }

    [TestMethod]
    public async Task HeadlessSeason()
    {
        var seriesPath = FakePath.Create("打了300年的史莱姆，不知不觉就练到了满级");
        var season2Path = FakePath.Create("打了300年的史莱姆，不知不觉就练到了满级/Season 2");

        var series = new MediaBrowser.Controller.Entities.TV.Series
        {
            Path = seriesPath,
            Name = "打了300年的史莱姆，不知不觉就练到了满级"
        };
        _libraryManager.CreateItem(series, null);

        _libraryManager.CreateItem(new MediaBrowser.Controller.Entities.TV.Season
        {
            Path = season2Path,
            IndexNumber = 2
        }, series);

        var result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = season2Path,
            IndexNumber = 2
        },
            _token);
        Assert.IsTrue(result.HasMetadata, "should return metadata when folder name contains season");
        Assert.IsNotNull(result.Item, "data should not be null");
        Assert.AreEqual("スライム倒して300年、知らないうちにレベルMAXになってました ～そのに～", result.Item.Name, "should return the right title");
    }
    [TestMethod]
    public async Task HeadlessSeasonWithOtherFolder()
    {
        var seriesPath = FakePath.Create("打了300年的史莱姆，不知不觉就练到了满级");
        var season1Path = FakePath.Create("打了300年的史莱姆，不知不觉就练到了满级/CDs");

        var series = new MediaBrowser.Controller.Entities.TV.Series
        {
            Path = seriesPath,
            Name = "打了300年的史莱姆，不知不觉就练到了满级",
            ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "292969" } } //第一季
        };
        _libraryManager.CreateItem(series, null);

        _libraryManager.CreateItem(new MediaBrowser.Controller.Entities.TV.Season
        {
            Path = season1Path,
        }, series);

        var result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = season1Path,
        },
            _token);
        Assert.IsFalse(result.HasMetadata, "should return metadata when folder name contains season");
    }
}
