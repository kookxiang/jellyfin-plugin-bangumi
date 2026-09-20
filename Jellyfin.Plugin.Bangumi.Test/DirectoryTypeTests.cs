using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser;
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class DirectoryTypeTests
{
    [DataTestMethod]
    [DataRow(EpisodeParserType.Basic, DirectoryType.Normal)]
    [DataRow(EpisodeParserType.AnitomySharp, DirectoryType.Normal)]
    [DataRow(EpisodeParserType.Torrent, DirectoryType.Normal)]
    [DataRow(EpisodeParserType.Basic, DirectoryType.Special)]
    [DataRow(EpisodeParserType.AnitomySharp, DirectoryType.Special)]
    [DataRow(EpisodeParserType.Torrent, DirectoryType.Special)]
    public async Task ForcedTypeOverridesPathSavedIdAndOldSeason(EpisodeParserType parser, DirectoryType type)
    {
        var plugin = ServiceLocator.GetService<Bangumi.Plugin>();
        var oldParser = plugin.Configuration.EpisodeParser;
        var oldTrust = plugin.Configuration.TrustExistedBangumiId;
        try
        {
            plugin.Configuration.EpisodeParser = parser;
            plugin.Configuration.TrustExistedBangumiId = true;
            var directory = $"forced-{parser}-{type}/OVA";
            var season = FakePath.CreateSeason(ServiceLocator.GetService<ILibraryManager>(), directory);
            season.IndexNumber = 0;
            FakePath.CreateLocalConfiguration(directory, new Model.LocalConfiguration { Id = 69496, Type = type });
            var result = await ServiceLocator.GetService<EpisodeProvider>().GetMetadata(new EpisodeInfo
            {
                Path = FakePath.CreateFile($"{directory}/White Album 2 [OVA][01].mkv"),
                IndexNumber = 1,
                ParentIndexNumber = 0,
                ProviderIds = new Dictionary<string, string> { [Constants.ProviderName] = "938953" },
            }, CancellationToken.None);
            Assert.IsTrue(result.HasMetadata);
            Assert.AreEqual("259013", result.Item.ProviderIds[Constants.ProviderName]);
            if (type == DirectoryType.Special)
                Assert.AreEqual(0, result.Item.ParentIndexNumber);
            else
                Assert.IsTrue(result.Item.ParentIndexNumber > 0);
            Assert.AreEqual(1, result.Item.IndexNumber);
        }
        finally
        {
            plugin.Configuration.EpisodeParser = oldParser;
            plugin.Configuration.TrustExistedBangumiId = oldTrust;
        }
    }

    [DataTestMethod]
    [DataRow(EpisodeParserType.Basic)]
    [DataRow(EpisodeParserType.AnitomySharp)]
    [DataRow(EpisodeParserType.Torrent)]
    public async Task NormalOverridesSpecialApiType(EpisodeParserType parser)
    {
        var plugin = ServiceLocator.GetService<Bangumi.Plugin>();
        var oldParser = plugin.Configuration.EpisodeParser;
        try
        {
            plugin.Configuration.EpisodeParser = parser;
            var directory = $"forced-api-{parser}";
            FakePath.CreateLocalConfiguration(directory, new Model.LocalConfiguration { Id = 279457, Type = DirectoryType.Normal });
            var result = await ServiceLocator.GetService<EpisodeProvider>().GetMetadata(new EpisodeInfo
            {
                Path = FakePath.CreateFile($"{directory}/[Sword Art Online][00].mkv"),
                IndexNumber = 0,
                ParentIndexNumber = 0,
            }, CancellationToken.None);
            Assert.IsTrue(result.HasMetadata);
            Assert.AreEqual("リフレクション", result.Item.Name);
            Assert.AreEqual(1, result.Item.ParentIndexNumber);
            Assert.AreEqual(0, result.Item.IndexNumber);
        }
        finally
        {
            plugin.Configuration.EpisodeParser = oldParser;
        }
    }

    [DataTestMethod]
    [DataRow(EpisodeType.Normal, 1)]
    [DataRow(EpisodeType.Special, 2)]
    public void ConfiguredTypeBreaksTiesWithoutChangingApiObjects(EpisodeType type, int expected)
    {
        Model.Episode[] episodes =
        [
            new() { Id = 1, Order = 1, Type = EpisodeType.Normal },
            new() { Id = 2, Order = 1, Type = EpisodeType.Special },
        ];
        var match = LocalConfigurationHelper.MatchDirectoryEpisode(episodes, type, 1)!;
        Assert.AreEqual(expected, match.Id);
        match.SeasonNumber = 0;
        Assert.IsNull(episodes[expected - 1].SeasonNumber);
        Assert.IsNull(LocalConfigurationHelper.MatchDirectoryEpisode(episodes, type, 13));
        Assert.AreEqual(EpisodeType.Normal, episodes[0].Type);
        Assert.AreEqual(EpisodeType.Special, episodes[1].Type);
    }
}
