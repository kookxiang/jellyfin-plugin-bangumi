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
public class Season
{
    private readonly SubjectImageProvider _imageProvider = ServiceLocator.GetService<SubjectImageProvider>();
    private readonly SeasonProvider _provider = ServiceLocator.GetService<SeasonProvider>();

    private readonly CancellationToken _token = new();

    [TestMethod]
    public void ProviderInfo()
    {
        Assert.AreEqual(_provider.Name, Constants.ProviderName);
        Assert.IsTrue(_provider.Order < 0);
    }

    [TestMethod]
    public async Task WithoutSeasonFolder()
    {
        var result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create("White Album 2"),
            ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "69496" } }
        }, _token);
        Assert.IsFalse(result.HasMetadata, "should not return metadata when folder name not contains season");
    }

    [TestMethod]
    public async Task WithSeasonFolder()
    {
        var result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create("White Album 2/Season 1"),
            ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "69496" } }
        }, _token);
        Assert.IsTrue(result.HasMetadata, "should return metadata when folder name contains season");
    }

    [TestMethod]
    public async Task ImageProvider()
    {
        var season = new MediaBrowser.Controller.Entities.TV.Season();
        Assert.AreEqual(-5, _imageProvider.Order, "should have provider order: -5");
        Assert.AreEqual(Constants.PluginName, _imageProvider.Name, "should have provider name");
        Assert.IsTrue(_imageProvider.Supports(season), "should support series image");
        Assert.AreEqual(ImageType.Primary, _imageProvider.GetSupportedImages(season).First(), "should support primary image");
        var imgList = await _imageProvider.GetImages(new MediaBrowser.Controller.Entities.TV.Season
        {
            ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "69496" } }
        }, _token);
        Assert.IsTrue(imgList.Any(), "should return at least one image");
    }
}