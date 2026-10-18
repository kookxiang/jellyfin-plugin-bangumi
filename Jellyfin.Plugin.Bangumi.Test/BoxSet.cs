using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.External;
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class BoxSet
{
    private readonly BangumiApi _api = ServiceLocator.GetService<BangumiApi>();
    private readonly SubjectImageProvider _imageProvider = ServiceLocator.GetService<SubjectImageProvider>();
    private readonly Bangumi.Plugin _plugin = ServiceLocator.GetService<Bangumi.Plugin>();
    private readonly BoxSetProvider _provider = ServiceLocator.GetService<BoxSetProvider>();

    private readonly CancellationToken _token = new();

    [TestMethod]
    public void ProviderInfo()
    {
        Assert.AreEqual(_provider.Name, Constants.ProviderName);
        Assert.IsTrue(_provider.Order > 0);
    }

    [TestMethod]
    public void SupportsExternalId()
    {
        Assert.IsTrue(new ExternalIdProvider().Supports(new MediaBrowser.Controller.Entities.Movies.BoxSet()));
    }

    [TestMethod]
    public async Task GetById()
    {
        var result = await _provider.GetMetadata(new BoxSetInfo
            {
                Name = "ふたりはプリキュア",
                Path = FakePath.Create("ふたりはプリキュア"),
                ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "4243" } }
            },
            _token);

        Assert.IsNotNull(result.Item, "BoxSet data should not be null");
        Assert.AreEqual("ふたりはプリキュア（系列）", result.Item.Name, "should return correct name");
        Assert.AreNotEqual("", result.Item.Overview, "should return overview info");
        Assert.IsTrue(result.Item.CommunityRating is > 0 and <= 10, "should return rating info");
        Assert.IsNotNull(result.Item.ProviderIds[Constants.ProviderName], "should have plugin provider id");
    }

    [TestMethod]
    public async Task SearchByName()
    {
        var searchResults = await _provider.GetSearchResults(new BoxSetInfo
            {
                Name = "ふたりはプリキュア",
                Path = FakePath.Create("ふたりはプリキュア")
            },
            _token);
        Assert.IsTrue(searchResults.Any(x => x.ProviderIds[Constants.ProviderName].Equals("4243")), "should have correct search result");
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("An unrelated collection name")]
    public async Task SearchById(string name)
    {
        var results = (await _provider.GetSearchResults(new BoxSetInfo
        {
            Name = name,
            ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "4243" } }
        }, _token)).ToList();

        Assert.AreEqual(1, results.Count, "ID search should return only the specified subject");
        Assert.AreEqual("4243", results[0].ProviderIds[Constants.ProviderName]);
        Assert.AreEqual("ふたりはプリキュア", results[0].Name);
        Assert.IsFalse(string.IsNullOrEmpty(results[0].ImageUrl), "should return the subject image");
        Assert.IsFalse(string.IsNullOrEmpty(results[0].Overview), "should return the subject overview");
        Assert.AreEqual(2004, results[0].ProductionYear);
    }

    [TestMethod]
    public async Task SearchWithoutNameOrId()
    {
        var results = await _provider.GetSearchResults(new BoxSetInfo(), _token);

        Assert.IsFalse(results.Any(), "an empty search should not request unrelated results");
    }
}
