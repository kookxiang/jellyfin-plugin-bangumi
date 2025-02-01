using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class Book
{
    private readonly BangumiApi _api = ServiceLocator.GetService<BangumiApi>();
    private readonly SubjectImageProvider _imageProvider = ServiceLocator.GetService<SubjectImageProvider>();
    private readonly Bangumi.Plugin _plugin = ServiceLocator.GetService<Bangumi.Plugin>();
    private readonly BookProvider _provider = ServiceLocator.GetService<BookProvider>();

    private readonly CancellationToken _token = new();

    [TestMethod]
    public void ProviderInfo()
    {
        Assert.AreEqual(_provider.Name, Constants.ProviderName);
        Assert.IsTrue(_provider.Order < 0);
    }

    [TestMethod]
    public async Task GetById()
    {
        var result = await _provider.GetMetadata(new BookInfo
            {
                Name = "Sword Art Online",
                Path = FakePath.Create("Sword Art Online/01.epub"),
                ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "8071" } }
            },
            _token);

        Assert.IsNotNull(result.Item, "book data should not be null");
        Assert.AreEqual("ソードアート・オンライン (1) アインクラッド", result.Item.Name, "should return correct name");
        Assert.AreNotEqual("", result.Item.Overview, "should return overview info");
        Assert.AreEqual(DateTime.Parse("2009-04-10"), result.Item.PremiereDate, "should return correct release time info");
        Assert.IsTrue(result.Item.CommunityRating is > 0 and <= 10, "should return rating info");
        Assert.AreNotEqual("", result.People?[0].ImageUrl, "person should have image url");
        Assert.IsNotNull(result.Item.ProviderIds[Constants.ProviderName], "should have plugin provider id");
    }

    [TestMethod]
    public async Task SearchByName()
    {
        var searchResults = await _provider.GetSearchResults(new BookInfo
            {
                Name = "ソードアート・オンライン 1",
                Path = FakePath.Create("Sword Art Online/01.epub")
            },
            _token);
        Assert.IsTrue(searchResults.Any(x => x.ProviderIds[Constants.ProviderName].Equals("8071")), "should have correct search result");
    }
}
