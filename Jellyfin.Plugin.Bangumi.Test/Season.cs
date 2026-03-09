using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Util;
using Jellyfin.Plugin.Bangumi.Utils;
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

        subject = await _api.SearchNextSubject(174043, _token);
        Assert.AreNotEqual(220631, subject?.Id, "should skip movie");
        Assert.AreEqual(342667, subject?.Id, "can guess next season by subject id");

        subject = await _api.SearchNextSubject(28900, _token);
        Assert.AreNotEqual(99796, subject?.Id, "should skip OVA");
        Assert.AreNotEqual(136311, subject?.Id, "should skip OVA but with wrong metadata in Bangumi");
        Assert.AreEqual(127573, subject?.Id, "can guess next season by subject id");
    }

    [TestMethod]
    public async Task GuessNextSeason2()
    {
        var count = _plugin.Configuration.SeasonGuessMaxSearchCount;
        _plugin.Configuration.SeasonGuessMaxSearchCount = 2;
        var subject = await _api.SearchNextSubject(152091, _token);
        Assert.IsNull(subject?.Id, "Can not guess next TV season with BFS");

        _plugin.Configuration.SeasonGuessMaxSearchCount = 3;
        subject = await _api.SearchNextSubject(152091, _token);
        Assert.AreEqual(283643, subject?.Id, "Can guess next TV season with BFS");

        _plugin.Configuration.SeasonGuessMaxSearchCount = count;
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
            Assert.IsTrue(FileNameParser.IsSeasonNumberOnly(s), $"Should match: {s}");
        }
        foreach (var s in shouldNotMatch)
        {
            Assert.IsFalse(FileNameParser.IsSeasonNumberOnly(s), $"Should not match: {s}");
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

    [TestMethod]
    public void ExtractSeasonNumber()
    {
        var dict = new Dictionary<string, double?>()
        {
            { "S1", 1 },
            { "S01", 1 },
            { "Season 1", 1 },
            { "Season01", 1 },
            { "第一季", 1 },
            { "第1季", 1 },
            { "第零季", 0 },
            { "1st Season", 1 },
            { "Season One", 1 },
            { "Season Two", 2 },
            { "Season Ten", 10 },
            { "第2季", 2 },
            { "第十季", 10 },
            { "第3期", 3 },
            { "第2部", 2 },
            { "虚构推理 第二季", 2 },
            { "虚構推理 Season2", 2 },
            { "盾之勇者成名录 第二季", 2 },
            { "盾の勇者の成り上がり Season 2", 2 },
            { "Tate no Yuusha no Nariagari", null },
            { "Tate no Yuusha no Nariagari Season 2", 2 },
            { "The Rising of the Shield Hero Season 2", 2 },

            { "i", 1 }, { "II", 2 }, { "III", 3 }, { "IV", 4 }, { "V", 5 },
            { "VI", 6 }, { "VII", 7 }, { "VIII", 8 }, { "IX", 9 }, { "X", 10 },
            { "XI", 11 }, { "XII", 12 }, { "XIII", 13 }, { "XIV", 14 }, { "XV", 15 },
            { "XVI", 16 }, { "XVII", 17 }, { "XVIII", 18 }, { "XIX", 19 }, { "XX", 20 },

            { "White Album", null },
            { "Season", null },
            { "Sp", null },
            { "第季", null },
            { "OVA", null },
            { "偶像大师 闪耀色彩 第二季", 2 },
            { "OVERLORD IV", 4 },
            { "Overlord II", 2 },
            { "Arifureta S02", 2 },
            { "とある魔術の禁書目録Ⅱ", 2 },
            { "とある魔術の禁書目録Ⅲ", 3 },
            { "[Nekomoe kissaten&VCB-Studio] SPYxFAMILY S1 [Ma10p_1080p]", 1 },
            { "[Nekomoe kissaten&VCB-Studio] SPYxFAMILY S2 [Ma10p_1080p]", 2 },
            { "[VCB-Studio] Log Horizon [Hi10p_1080p]", null },
            { "[VCB-Studio] Log Horizon 2 [Ma10p_1080p]", 2 },
            { "[Snow-Raws] プリンセスコネクト！ Re：Dive", null },
            { "[Snow-Raws] プリンセスコネクト！ Re：Dive Season 2", 2 },
            { "[轻音少女第二季][SOSG][K-ON!!][1-27]", 2 },
            { "[VCB-Studio] Karakai Jouzu no Takagi-san [Ma10p_1080p]", null },
            { "[UHA-WINGS&VCB-Studio] Karakai Jouzu no Takagi-san 2 [Ma10p_1080p]", 2 },
            { "[UHA-WINGS&VCB-Studio] Karakai Jouzu no Takagi-san 3 [Ma10p_1080p]", 3 },
            { "[VCB-Studio&TUcaptions] Date A Live II [Hi10p_1080p]", 2 },
            { "[VCB-Studio] Sword Art Online II [Ma10p_1080p]", 2 },
        };

        foreach (var item in dict)
        {
            Assert.AreEqual(item.Value, FileNameParser.ExtractAnimeSeason(item.Key, false), $"Extracted season number for {item.Key} should be {item.Value}");
        }
    }

    [TestMethod]
    public async Task SearchSubjectByFolderPath()
    {
        FakePath.CreateSeries(_libraryManager, "[VCB-Studio] Tate no Yuusha no Nariagari");

        var result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create("[VCB-Studio] Tate no Yuusha no Nariagari/[VCB-Studio] Tate no Yuusha no Nariagari Season 2 [Ma10p_1080p]")
        }, _token);
        Assert.IsTrue(result.HasMetadata, "should return metadata when folder name contains season");
        Assert.AreEqual(289906, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.ProviderName) ?? ""), "should return the right season id");
        Assert.AreEqual(2, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.SeasonNumberProviderName) ?? ""), "should return the right season number");
    }

    [TestMethod]
    public async Task SearchSubjectByFolderPath_MergeSeriesNameAndSeasonNumber()
    {
        string seriesFolderName = "[neoDESU] By the Grace of the Gods [Season 1-2] [BD 1080p x265 HEVC OPUS AAC] [Dual Audio]";

        FakePath.CreateSeries(_libraryManager, seriesFolderName);

        var result = await _provider.GetMetadata(new SeasonInfo
        {
            Path = FakePath.Create($"{seriesFolderName}/Season 2")
        }, _token);

        Assert.IsTrue(result.HasMetadata, "should return metadata when using parent folder name and current folder season number");
        Assert.AreEqual(338150, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.ProviderName) ?? ""), "should return the expected season id");
        Assert.AreEqual(2, int.Parse(result.Item.ProviderIds.GetOrDefault(Constants.SeasonNumberProviderName) ?? ""), "should return the expected season number");
    }
}
