using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.BaseItemManager;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArchiveEpisode = Jellyfin.Plugin.Bangumi.Archive.Data.Episode;
using JellyfinSeries = MediaBrowser.Controller.Entities.TV.Series;
using JellyfinEpisode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class MissingEpisodeTests
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "bangumi-missing-" + Guid.NewGuid());
    private PluginConfiguration _old = null!;
    private ILibraryManager _oldLibrary = null!;
    private MissingEpisodeProvider _provider = null!;
    private LibraryProxy _library = null!;
    private Bangumi.Archive.ArchiveData _archive = null!;
    private JellyfinSeries _series = null!;
    private MediaBrowser.Controller.Entities.TV.Season _season = null!;

    private sealed class Paths(string path) : MockedApplicationPaths, IApplicationPaths
    {
        public new string DataPath => path;
    }

    [TestInitialize]
    public void Setup()
    {
        var plugin = ServiceLocator.GetService<Bangumi.Plugin>();
        _old = new PluginConfiguration
        {
            ImportMissingEpisodes = plugin.Configuration.ImportMissingEpisodes,
            ImportUnairedEpisodes = plugin.Configuration.ImportUnairedEpisodes,
            EnabledMissingEpisodeLibraries = plugin.Configuration.EnabledMissingEpisodeLibraries
        };
        var library = DispatchProxy.Create<ILibraryManager, LibraryProxy>();
        _library = (LibraryProxy)library;
        _oldLibrary = BaseItem.LibraryManager;
        BaseItem.LibraryManager = library;
        plugin.Configuration.ImportMissingEpisodes = true;
        plugin.Configuration.ImportUnairedEpisodes = true;
        plugin.Configuration.EnabledMissingEpisodeLibraries = [_library.Folder.Id.ToString()];
        _archive = new Bangumi.Archive.ArchiveData(new Paths(_root));
        _provider = new MissingEpisodeProvider(_archive, library,
            DispatchProxy.Create<IBaseItemManager, EpisodeVersionResolverTests.FetcherProxy>());
        _series = new JellyfinSeries { Id = Guid.NewGuid(), Path = Path.Join(_root, "series"), Name = "Test", PresentationUniqueKey = "test-series" };
        _series.SetProviderId(Constants.ProviderName, "10");
        _season = new MediaBrowser.Controller.Entities.TV.Season
        {
            Id = Guid.NewGuid(), IndexNumber = 1, Name = "Season 1", Path = Path.Join(_series.Path, "Season 1")
        };
        Directory.CreateDirectory(_season.Path);
        _library.Items.Add(_season);
    }

    [TestCleanup]
    public void Cleanup()
    {
        var config = ServiceLocator.GetService<Bangumi.Plugin>().Configuration;
        config.ImportMissingEpisodes = _old.ImportMissingEpisodes;
        config.ImportUnairedEpisodes = _old.ImportUnairedEpisodes;
        config.EnabledMissingEpisodeLibraries = _old.EnabledMissingEpisodeLibraries;
        BaseItem.LibraryManager = _oldLibrary;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private Task<ItemUpdateType> Scan() => _provider.FetchAsync(_series, null!, CancellationToken.None);

    private async Task Archive(params ArchiveEpisode[] episodes)
    {
        Directory.CreateDirectory(_archive.TempPath);
        await File.WriteAllTextAsync(_archive.Episode.FilePath,
            string.Join('\n', episodes.OrderBy(episode => episode.Id).Select(episode => JsonSerializer.Serialize(episode))) + "\n");
        await _archive.Episode.GenerateIndex(CancellationToken.None);
        await _archive.SubjectEpisodeRelation.GenerateIndex(CancellationToken.None);
    }

    private static ArchiveEpisode Source(int id, double order, string date = "2020-01-01", EpisodeType type = EpisodeType.Normal) => new()
    {
        Id = id, ParentId = 10, Order = order, AirDate = date, Type = type, OriginalName = "Episode " + order
    };

    [TestMethod]
    public async Task ImportsWholeOfflineListAndIsIdempotent()
    {
        await Archive(Enumerable.Range(1, 60).Select(i => Source(i, i)).ToArray());
        Assert.AreEqual(ItemUpdateType.MetadataImport, await Scan());
        var imported = _library.Items.OfType<JellyfinEpisode>().ToArray();
        Assert.AreEqual(60, imported.Length);
        Assert.IsTrue(imported.All(MissingEpisodeProvider.IsOwned));
        Assert.IsTrue(imported.All(episode => episode.ParentId == _season.Id && episode.SeasonId == _season.Id));
        Assert.AreEqual(ItemUpdateType.None, await Scan());
        Assert.AreEqual(60, _library.Items.OfType<JellyfinEpisode>().Count());
        await Archive(Source(1, 1));
        Assert.AreEqual(ItemUpdateType.MetadataImport, await Scan());
        Assert.AreEqual(1, _library.Items.OfType<JellyfinEpisode>().Count());
    }

    [TestMethod]
    public async Task MissingArchiveAndMissingSubjectLeavePlaceholdersUntouched()
    {
        Assert.AreEqual(ItemUpdateType.None, await Scan());
        await Archive(Source(1, 1));
        await Scan();
        File.Move(_archive.Episode.FilePath, _archive.Episode.FilePath + ".unavailable");
        Assert.AreEqual(ItemUpdateType.None, await Scan());
        Assert.AreEqual(1, _library.Items.OfType<JellyfinEpisode>().Count());
        File.Move(_archive.Episode.FilePath + ".unavailable", _archive.Episode.FilePath);
        _season.SetProviderId(Constants.ProviderName, "999");
        Assert.AreEqual(ItemUpdateType.None, await Scan());
        Assert.AreEqual(0, _library.Deleted.Count);
    }

    [TestMethod]
    public async Task PhysicalEpisodesAndRangesReplaceOnlyOwnedPlaceholders()
    {
        await Archive(Source(1, 1), Source(2, 2), Source(3, 3));
        await Scan();
        var physical = new JellyfinEpisode { Id = Guid.NewGuid(), Path = "/anime/01-02.mkv", ParentIndexNumber = 1, IndexNumber = 1, IndexNumberEnd = 2 };
        physical.SetProviderId(Constants.ProviderName, "1");
        _library.Items.Add(physical);
        var foreign = new JellyfinEpisode { Id = Guid.NewGuid(), IsVirtualItem = true, ParentIndexNumber = 1, IndexNumber = 9 };
        foreign.SetProviderId(Constants.ProviderName, "99");
        _library.Items.Add(foreign);
        await Scan();
        Assert.AreEqual(1, _library.Items.OfType<JellyfinEpisode>().Count(MissingEpisodeProvider.IsOwned));
        ServiceLocator.GetService<Bangumi.Plugin>().Configuration.ImportMissingEpisodes = false;
        ServiceLocator.GetService<Bangumi.Plugin>().Configuration.ImportUnairedEpisodes = false;
        await Scan();
        Assert.IsTrue(_library.Items.Contains(physical));
        Assert.IsTrue(_library.Items.Contains(foreign));
        Assert.AreEqual(0, _library.Items.OfType<JellyfinEpisode>().Count(MissingEpisodeProvider.IsOwned));
        Assert.IsTrue(_library.Deleted.All(entry => !entry.Options.DeleteFileLocation));
    }

    [TestMethod]
    public async Task FileMetadataRefreshCleansUpInTheSameScanWithoutArchiveAccess()
    {
        await Archive(Source(1, 1));
        await Scan();
        File.Move(_archive.Episode.FilePath, _archive.Episode.FilePath + ".unavailable");
        var physical = new JellyfinEpisode { Path = "/anime/01.mkv", SeriesId = _series.Id, ParentIndexNumber = 1, IndexNumber = 1 };
        physical.SetProviderId(Constants.ProviderName, "1");
        await new MissingEpisodeCleanupProvider(BaseItem.LibraryManager).FetchAsync(physical, null!, CancellationToken.None);
        Assert.AreEqual(0, _library.Items.OfType<JellyfinEpisode>().Count());
        Assert.IsFalse(_library.Deleted.Single().Options.DeleteFileLocation);
    }

    [TestMethod]
    public async Task LocalOffsetsAndCorrectIndexStayInSyncAndSpecialsAreExcluded()
    {
        await Archive(Source(1, 13), Source(2, 13.5), Source(3, 14, type: EpisodeType.Special));
        await File.WriteAllTextAsync(Path.Join(_season.Path, "bangumi.ini"), "ID=10\nOffset=-12");
        await Scan();
        var episode = _library.Items.OfType<JellyfinEpisode>().Single();
        Assert.AreEqual(1, episode.IndexNumber);
        await File.WriteAllTextAsync(Path.Join(_season.Path, "bangumi.ini"), "ID=10\nOffset=-12\nCorrectIndex=true");
        await Scan();
        Assert.AreEqual(13, episode.IndexNumber);
        await File.WriteAllTextAsync(Path.Join(_season.Path, "bangumi.ini"), "ID=10\nSkip=true");
        await Scan();
        Assert.AreEqual(0, _library.Items.OfType<JellyfinEpisode>().Count());
    }

    [TestMethod]
    public async Task NameBasedLibrarySelectionImportsMissingEpisodeAndRespectsOptOut()
    {
        _library.Folder.Name = "Bangumi";
        var config = ServiceLocator.GetService<Bangumi.Plugin>().Configuration;
        config.EnabledMissingEpisodeLibraries = ["name:Bangumi"];
        await Archive(Source(12, 12));
        Assert.AreEqual(ItemUpdateType.MetadataImport, await Scan());
        Assert.AreEqual(12, _library.Items.OfType<JellyfinEpisode>().Single().IndexNumber);
        Assert.AreEqual(ItemUpdateType.None, await Scan());
        config.EnabledMissingEpisodeLibraries = ["name:Other"];
        await Scan();
        Assert.AreEqual(0, _library.Items.OfType<JellyfinEpisode>().Count());
        Assert.IsFalse(_library.Deleted.Single().Options.DeleteFileLocation);
    }

    [TestMethod]
    public async Task OptOutAndNonBangumiEpisodeLibrariesCleanUp()
    {
        await Archive(Source(1, 1));
        await Scan();
        _library.Options.TypeOptions[0].MetadataFetchers = ["TheMovieDb"];
        await Scan();
        Assert.AreEqual(0, _library.Items.OfType<JellyfinEpisode>().Count());
        _library.Options.TypeOptions[0].MetadataFetchers = [Constants.ProviderName];
        await Scan();
        ServiceLocator.GetService<Bangumi.Plugin>().Configuration.EnabledMissingEpisodeLibraries = [];
        await Scan();
        Assert.AreEqual(0, _library.Items.OfType<JellyfinEpisode>().Count());
    }

    [TestMethod]
    public void OnlyDatedIntegerNormalEpisodesUseTheirRespectiveSwitch()
    {
        var config = new PluginConfiguration { ImportMissingEpisodes = true };
        var local = new LocalConfiguration();
        var today = new DateTime(2026, 9, 12);
        Assert.IsTrue(MissingEpisodeProvider.ShouldImport(Source(1, 1).ToEpisode(), local, config, today));
        Assert.IsFalse(MissingEpisodeProvider.ShouldImport(Source(1, 1, "2026-09-12").ToEpisode(), local, config, today));
        config.ImportUnairedEpisodes = true;
        Assert.IsTrue(MissingEpisodeProvider.ShouldImport(Source(1, 1, "2026-09-12").ToEpisode(), local, config, today));
        foreach (var date in new[] { "", "2026", "2026-02-30", "TBA" })
            Assert.IsFalse(MissingEpisodeProvider.ShouldImport(Source(1, 1, date).ToEpisode(), local, config, today));
        foreach (var number in new[] { 1.5, double.NaN, double.PositiveInfinity, -1, (double)int.MaxValue + 1 })
            Assert.IsFalse(MissingEpisodeProvider.ShouldImport(Source(1, number).ToEpisode(), local, config, today));
        local.Offset = int.MaxValue;
        Assert.IsFalse(MissingEpisodeProvider.ShouldImport(Source(1, 1).ToEpisode(), local, config, today));
    }

    [TestMethod]
    public async Task VirtualRefreshDoesNotParseAFileOrResetSeason()
    {
        var episode = new JellyfinEpisode { IsVirtualItem = true, ParentIndexNumber = 2, IndexNumber = 13 };
        await new EpisodePreRefreshProvider().FetchAsync(episode, null!, CancellationToken.None);
        Assert.AreEqual(2, episode.ParentIndexNumber);
        var result = await ServiceLocator.GetService<EpisodeProvider>().GetMetadata(new MediaBrowser.Controller.Providers.EpisodeInfo
        {
            Path = null!, ProviderIds = new Dictionary<string, string> { [Constants.ProviderName] = "1" }
        }, CancellationToken.None);
        Assert.IsFalse(result.HasMetadata);
    }

    [TestMethod]
    public void SeasonAndLocalIdsTakePrecedenceWithoutGuessingLaterSeasons()
    {
        var local = new LocalConfiguration();
        Assert.AreEqual(10, MissingEpisodeProvider.ResolveSubjectId(_series, _season, local));
        _season.IndexNumber = 2;
        Assert.AreEqual(0, MissingEpisodeProvider.ResolveSubjectId(_series, _season, local));
        _season.SetProviderId(Constants.ProviderName, "20");
        Assert.AreEqual(20, MissingEpisodeProvider.ResolveSubjectId(_series, _season, local));
        local.Id = 30;
        Assert.AreEqual(30, MissingEpisodeProvider.ResolveSubjectId(_series, _season, local));
    }

    public class LibraryProxy : DispatchProxy
    {
        public List<BaseItem> Items { get; } = [];
        public List<(BaseItem Item, DeleteOptions Options)> Deleted { get; } = [];
        public Folder Folder { get; } = new CollectionFolder { Id = Guid.NewGuid() };
        public LibraryOptions Options { get; } = new()
        {
            TypeOptions = [new TypeOptions { Type = "Episode", MetadataFetchers = [Constants.ProviderName] }]
        };

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "GetItemList": return Items.ToArray();
                case "GetLibraryOptions": return Options;
                case "GetCollectionFolders": return new List<Folder> { Folder };
                case "GetNewItemId": return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes((string)args![0]!)).AsSpan(0, 16));
                case "CreateItem": Items.Add((BaseItem)args![0]!); return null;
                case "UpdateItemAsync": return Task.CompletedTask;
                case "DeleteItem":
                    var item = (BaseItem)args![0]!;
                    Deleted.Add((item, (DeleteOptions)args[1]!));
                    Items.Remove(item);
                    return null;
                default: throw new NotImplementedException(targetMethod.Name);
            }
        }
    }
}
