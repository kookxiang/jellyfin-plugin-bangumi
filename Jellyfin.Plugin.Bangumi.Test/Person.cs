using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class Person
{
    private readonly BangumiApi _api = ServiceLocator.GetService<BangumiApi>();
    private readonly PersonImageProvider _imageProvider = ServiceLocator.GetService<PersonImageProvider>();
    private readonly PersonProvider _provider = ServiceLocator.GetService<PersonProvider>();

    private readonly CancellationToken _token = new();

    [TestMethod]
    public void ProviderInfo()
    {
        Assert.AreEqual(_imageProvider.Name, Constants.ProviderName);
        Assert.IsTrue(_imageProvider.Order < 0);
    }

    [TestMethod]
    public async Task GetById()
    {
        Bangumi.Plugin.Instance!.Configuration.PersonTranslationPreference = TranslationPreferenceType.Chinese;
        var result = await _provider.GetMetadata(new PersonLookupInfo { ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "7307" } } }, _token);
        Assert.IsNotNull(result.Item, "person info should not be null");
        Assert.AreEqual("上坂すみれ", result.Item.OriginalTitle, "should return correct name");
        Assert.AreEqual("上坂堇", result.Item.Name, "should return translated name");
        Assert.IsNotNull(result.Item.ProviderIds[Constants.ProviderName], "should have plugin provider id");
    }
    [TestMethod]
    public async Task GetCharacterById()
    {
        Bangumi.Plugin.Instance!.Configuration.PersonTranslationPreference = TranslationPreferenceType.Chinese;
        Bangumi.Plugin.Instance!.Configuration.AddCharacterToPerson = true;
        var result = await _provider.GetMetadata(new PersonLookupInfo { ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, $"{Constants.CharacterIdPrefix}17672" } } }, _token);
        Bangumi.Plugin.Instance!.Configuration.AddCharacterToPerson = false;
        Assert.IsNotNull(result.Item, "person info should not be null");
        Assert.AreEqual("星宮いちご", result.Item.OriginalTitle, "should return correct name");
        Assert.AreEqual("星宫莓", result.Item.Name, "should return translated name");
        Assert.IsNotNull(result.Item.ProviderIds[Constants.ProviderName], "should have plugin provider id");
    }
    [TestMethod]
    public async Task TranslationPreferenceTypeWithChinese()
    {
        Bangumi.Plugin.Instance!.Configuration.PersonTranslationPreference = TranslationPreferenceType.Chinese;
        var result = (await _api.GetSubjectVirtualCharacters(56847, _token)).ToList();
        Assert.IsNotNull(result, "person info should not be null");
        Assert.AreEqual("辉夜姬", result[0].Name, "should return translated name");
        Assert.AreEqual("旁白", result[4].Name, "should return translated name");

        result = (await _api.GetSubjectCharacters(284524, _token)).ToList();
        Assert.IsNotNull(result, "person info should not be null");
        Assert.AreEqual("アヴちゃん", result[0].Name, "should return translated name");
        Assert.AreEqual("森山未来", result[2].Name, "should return translated name");
    }
    [TestMethod]
    public async Task TranslationPreferenceTypeWithOriginal()
    {
        Bangumi.Plugin.Instance!.Configuration.PersonTranslationPreference = TranslationPreferenceType.Original;
        var result = (await _api.GetSubjectVirtualCharacters(56847, _token)).ToList();
        Assert.IsNotNull(result, "person info should not be null");
        Assert.AreEqual("かぐや姫", result[0].Name, "should return translated name");
        Assert.AreEqual("ナレーション", result[4].Name, "should return translated name");

        result = (await _api.GetSubjectCharacters(284524, _token)).ToList();
        Assert.IsNotNull(result, "person info should not be null");
        Assert.AreEqual("アヴちゃん", result[0].Name, "should return translated name");
        Assert.AreEqual("森山未來", result[2].Name, "should return translated name");
    }

    [TestMethod]
    public async Task LineBreaksConversion()
    {
        var result = await _provider.GetMetadata(new PersonLookupInfo { ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "389" } } }, _token);
        Assert.IsNotNull(result.Item, "person info should not be null");
        Assert.IsTrue(result.Item.Overview.Contains("<br>") || result.Item.Overview.Contains("\n\n"), "should convert line breaks to html tag");
        Assert.IsNotNull(result.Item.ProviderIds[Constants.ProviderName], "should have plugin provider id");
    }

    [TestMethod]
    public async Task ImageProvider()
    {
        var person = new MediaBrowser.Controller.Entities.Person();
        Assert.AreEqual(-5, _imageProvider.Order, "should have provider order: -5");
        Assert.AreEqual(Constants.PluginName, _imageProvider.Name, "should have provider name");
        Assert.IsTrue(_imageProvider.Supports(person), "should support person image");
        Assert.AreEqual(ImageType.Primary, _imageProvider.GetSupportedImages(person).First(), "should support primary image");
        var imgList = await _imageProvider.GetImages(new MediaBrowser.Controller.Entities.TV.Episode { ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, "5847" } } }, _token);
        Assert.IsTrue(imgList.Any(), "should return at least one image");
    }

    [TestMethod]
    public async Task SearchPerson()
    {
        var results = await _provider.GetSearchResults(new PersonLookupInfo { Name = "上坂すみれ" }, _token);
        Assert.IsTrue(results.Any(), "should return at least one search result");
        Assert.IsNotNull(results.ToList().Find(x => x.ProviderIds[Constants.ProviderName] == "7307"), "should find correct person in search results");
    }
}
