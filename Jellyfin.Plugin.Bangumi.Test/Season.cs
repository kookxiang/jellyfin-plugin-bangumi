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
    private readonly Bangumi.Plugin _plugin = ServiceLocator.GetService<Bangumi.Plugin>();
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

    [TestMethod()]
    public void OnlySeasonNumberRegexTest()
    {
        // 匹配应为 true 的情况
        string[] shouldMatch =
        [
            "1", "01", "S1", "S01", "Season 1", "Season01",
            "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII", "XIX", "XX",
            "第一季", "第1季", "第十季", "第2部", "第3期", "第零季",
            "1st Season", "2nd Season", "3rd Season", "4th Season",
            "Season One", "Season Two", "Season Three", "Season Ten"
        ];

        // 匹配应为 false 的情况
        string[] shouldNotMatch =
        [
            "White Album",
            "Season",
            "Sp",
            "第季",
            "OVA",
            "偶像大师 闪耀色彩 第二季",
            "OVERLORD IV",
            "Overlord II",
            "High Score Girl S01 + OVA + S02",
            "Arifureta S02"
        ];

        foreach (var s in shouldMatch)
        {
            Assert.IsTrue(SeasonProvider.OnlySeasonNumberRegex().IsMatch(s), $"Should match: {s}");
        }
        foreach (var s in shouldNotMatch)
        {
            Assert.IsFalse(SeasonProvider.OnlySeasonNumberRegex().IsMatch(s), $"Should not match: {s}");
        }
    }

    [TestMethod]
    public async Task GuessSeasonNumber()
    {
        FakePath.CreateSeries(_libraryManager, "[DMG] 冴えない彼女の育てかた [BDRip][S1+S2+MOVIE]");

        var result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create("[DMG] 冴えない彼女の育てかた [BDRip][S1+S2+MOVIE]/[DMG] 冴えない彼女の育てかた [BDRip]")
        }, _token);
        Assert.IsTrue(result.HasMetadata, "should return metadata when folder name contains season");
        Assert.AreEqual("冴えない彼女の育てかた", result.Item.OriginalTitle, "should return the right season title");
        Assert.AreEqual(100403, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.ProviderName) ?? ""), "should return the right season id");
        Assert.AreEqual(1, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.SeasonNumberProviderName) ?? ""), "should return the right season number");

        result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create("[DMG] 冴えない彼女の育てかた [BDRip][S1+S2+MOVIE]/[DMG] 冴えない彼女の育てかた♭ [BDRip]")
        }, _token);
        Assert.IsTrue(result.HasMetadata, "should return metadata when folder name contains season");
        Assert.AreEqual("冴えない彼女の育てかた ♭", result.Item.OriginalTitle, "should return the right season title");
        Assert.AreEqual(132734, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.ProviderName) ?? ""), "should return the right season id");
        Assert.AreEqual(2, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.SeasonNumberProviderName) ?? ""), "should return the right season number");

        // default api does not support fuzzy search
        _plugin.Configuration.UseTestingSearchApi = true;
        result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create("[DMG] 冴えない彼女の育てかた [BDRip][S1+S2+MOVIE]/[DMG] 劇場版 冴えない彼女の育てかた Fine [BDRip]")
        }, _token);
        Assert.IsTrue(result.HasMetadata, "should return metadata when folder name contains season");
        Assert.AreEqual("冴えない彼女の育てかた Fine", result.Item.OriginalTitle, "should return the right season title");
        Assert.AreEqual(231497, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.ProviderName) ?? ""), "should return the right season id");
        Assert.AreEqual(0, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.SeasonNumberProviderName) ?? ""), "should return the right season number");
    }

    [TestMethod]
    public async Task GuessSeasonNumber2()
    {
        FakePath.CreateSeries(_libraryManager, "恶魔高校D×D");

        var result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create("恶魔高校D×D/恶魔高校D×D")
        }, _token);
        Assert.IsTrue(result.HasMetadata, "should return metadata when folder name contains season");
        Assert.AreEqual("ハイスクールD×D", result.Item.OriginalTitle, "should return the right season title");
        Assert.AreEqual(15910, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.ProviderName) ?? ""), "should return the right season id");
        Assert.AreEqual(1, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.SeasonNumberProviderName) ?? ""), "should return the right season number");

        result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create("恶魔高校D×D/恶魔高校D×D NEW")
        }, _token);
        Assert.IsTrue(result.HasMetadata, "should return metadata when folder name contains season");
        Assert.AreEqual("ハイスクールD×D NEW", result.Item.OriginalTitle, "should return the right season title");
        Assert.AreEqual(48700, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.ProviderName) ?? ""), "should return the right season id");
        Assert.AreEqual(2, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.SeasonNumberProviderName) ?? ""), "should return the right season number");

        result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create("恶魔高校D×D/恶魔高校D×D BorN")
        }, _token);
        Assert.IsTrue(result.HasMetadata, "should return metadata when folder name contains season");
        Assert.AreEqual("ハイスクールD×D BorN", result.Item.OriginalTitle, "should return the right season title");
        Assert.AreEqual(106212, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.ProviderName) ?? ""), "should return the right season id");
        Assert.AreEqual(3, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.SeasonNumberProviderName) ?? ""), "should return the right season number");

        result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create("恶魔高校D×D/恶魔高校D×D HERO")
        }, _token);
        Assert.IsTrue(result.HasMetadata, "should return metadata when folder name contains season");
        Assert.AreEqual("ハイスクールD×D HERO", result.Item.OriginalTitle, "should return the right season title");
        Assert.AreEqual(195845, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.ProviderName) ?? ""), "should return the right season id");
        Assert.AreEqual(4, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.SeasonNumberProviderName) ?? ""), "should return the right season number");

        result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create("恶魔高校D×D/恶魔高校D×D OAD"),
        }, _token);
        Assert.IsTrue(result.HasMetadata, "should return metadata when folder name contains season");
        Assert.AreEqual("ハイスクールD×D OAD", result.Item.OriginalTitle, "should return the right season title");
        Assert.AreEqual(46010, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.ProviderName) ?? ""), "should return the right season id");
        Assert.AreEqual(0, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.SeasonNumberProviderName) ?? ""), "should return the right season number");

        result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create("恶魔高校D×D/恶魔高校D×D DX OAD"),
        }, _token);
        Assert.IsTrue(result.HasMetadata, "should return metadata when folder name contains season");
        Assert.AreEqual("ハイスクールD×D DX OAD", result.Item.OriginalTitle, "should return the right season title");
        Assert.AreEqual(127827, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.ProviderName) ?? ""), "should return the right season id");
        Assert.AreEqual(0, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.SeasonNumberProviderName) ?? ""), "should return the right season number");
    }
}
