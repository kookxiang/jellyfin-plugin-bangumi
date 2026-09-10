using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Emby.Naming.Common;
using Emby.Naming.TV;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Resolvers;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.BaseItemManager;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JellyfinEpisode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class EpisodeVersionResolverTests
{
    private bool _oldEnabled;
    private Bangumi.Plugin _plugin = null!;
    private MockedLibraryManager _library = null!;
    private Folder _parent = null!;
    private BangumiEpisodeVersionResolver _resolver = null!;
    private readonly NamingOptions _naming = new();

    [TestInitialize]
    public void Setup()
    {
        _plugin = ServiceLocator.GetService<Bangumi.Plugin>();
        _oldEnabled = _plugin.Configuration.MergeEpisodeVersionsByBangumiId;
        _plugin.Configuration.MergeEpisodeVersionsByBangumiId = true;
        _library = new MockedLibraryManager
        {
            LibraryOptions = new LibraryOptions
            {
                TypeOptions = [new TypeOptions { Type = "Episode", MetadataFetchers = [Constants.ProviderName] }],
            },
            PathResolver = (file, _) => new JellyfinEpisode { Path = file.FullName, Name = file.Name },
        };
        _parent = FakePath.CreateSeason(_library, $"versions-{Guid.NewGuid()}/Season 1");
        var manager = DispatchProxy.Create<IBaseItemManager, FetcherProxy>();
        _resolver = new BangumiEpisodeVersionResolver(_library, manager, _naming);
    }

    [TestCleanup]
    public void Cleanup() => _plugin.Configuration.MergeEpisodeVersionsByBangumiId = _oldEnabled;

    [TestMethod]
    public void DefaultAndDisabledLeaveNativeResolutionUntouched()
    {
        Assert.IsFalse(new PluginConfiguration().MergeEpisodeVersionsByBangumiId);
        _plugin.Configuration.MergeEpisodeVersionsByBangumiId = false;
        Assert.IsNull(_resolver.ResolveMultiple(_parent, Files("01", "02"), CollectionType.tvshows, null!));
        Assert.IsNull(_resolver.ResolvePath(null!));
    }

    [TestMethod]
    public void OnlyAppliesToTvLibrariesUsingBangumi()
    {
        Assert.IsNull(_resolver.ResolveMultiple(_parent, Files("01", "02"), CollectionType.movies, null!));
        _library.LibraryOptions.TypeOptions[0].MetadataFetchers = ["TheMovieDb"];
        Assert.IsNull(_resolver.ResolveMultiple(_parent, Files("01", "02"), CollectionType.tvshows, null!));
    }

    [TestMethod]
    public void FirstScanSeparatesAllThirteenMisparsedEpisodes()
    {
        var files = Files(Enumerable.Range(1, 13).Select(i => i.ToString("00")).ToArray());
        var parser = new EpisodePathParser(_naming);
        Assert.IsTrue(files.All(file => parser.Parse(file.FullName, false).EpisodeNumber == 10));
        var result = _resolver.ResolveMultiple(_parent, files, CollectionType.tvshows, null!);
        Assert.AreEqual(13, result.Items.Count);
        Assert.IsTrue(result.Items.Cast<JellyfinEpisode>().All(e => e.LocalAlternateVersions.Length == 0));
        Assert.AreEqual(0, result.ExtraFiles.Count);
    }

    [TestMethod]
    public void SecondScanMergesOnlyIdenticalPositiveIds()
    {
        var files = Files("01", "01", "02", "03", "04", "05", "06");
        files[1].FullName = files[1].FullName.Replace("1080p", "720p");
        string?[] ids = ["101", "101", "102", null, "0", "invalid", "-1"];
        for (var i = 0; i < files.Count; i++) Save(files[i], ids[i]);
        var result = _resolver.ResolveMultiple(_parent, files, CollectionType.tvshows, null!);
        Assert.AreEqual(6, result.Items.Count);
        var merged = result.Items.Cast<JellyfinEpisode>().Single(e => e.LocalAlternateVersions.Length > 0);
        CollectionAssert.AreEqual(new[] { files[1].FullName }, merged.LocalAlternateVersions);
    }

    [TestMethod]
    public void CorrectedIdsProduceSeparateGroupsWithoutMutatingSavedMetadata()
    {
        var files = Files("01", "02");
        var first = Save(files[0], "101");
        var second = Save(files[1], "102");
        first.LocalAlternateVersions = [second.Path];
        second.OwnerId = first.Id;
        second.SetPrimaryVersionId(first.Id);
        var updates = new List<Guid>();
        _library.ItemUpdated += (_, args) =>
        {
            updates.Add(args.Item.Id);
            if (args.Item.Id == first.Id)
                Assert.AreEqual(Guid.Empty, second.OwnerId, "Promote the child before dropping its owning relationship.");
        };
        var result = _resolver.ResolveMultiple(_parent, files, CollectionType.tvshows, null!);
        Assert.AreEqual(2, result.Items.Count);
        Assert.IsTrue(result.Items.Cast<JellyfinEpisode>().All(e => e.LocalAlternateVersions.Length == 0));
        Assert.AreEqual(0, first.LocalAlternateVersions.Length);
        Assert.AreEqual(Guid.Empty, second.OwnerId);
        Assert.IsNull(second.PrimaryVersionId);
        Assert.AreSame(second, _library.FindByPath(second.Path, false));
        Assert.AreEqual("101", first.ProviderIds[Constants.ProviderName]);
        Assert.AreEqual("102", second.ProviderIds[Constants.ProviderName]);
        CollectionAssert.AreEqual(new[] { second.Id, first.Id }, updates);
        _resolver.ResolveMultiple(_parent, files, CollectionType.tvshows, null!);
        Assert.AreEqual(2, updates.Count, "Repeated scans must not repeat version-detachment writes.");
    }

    [TestMethod]
    public void KeepsSubdirectoriesAndNonVideosForNativeResolution()
    {
        var files = Files("01", "02");
        files.Add(new FileSystemMetadata { FullName = _parent.Path + "/subtitles.ass" });
        files.Add(new FileSystemMetadata { FullName = _parent.Path + "/Specials", IsDirectory = true });
        files.Add(new FileSystemMetadata { FullName = _parent.Path + "/show-trailer.mkv" });
        var result = _resolver.ResolveMultiple(_parent, files, CollectionType.tvshows, null!);
        Assert.AreEqual(2, result.Items.Count);
        Assert.AreEqual(3, result.ExtraFiles.Count);
    }

    [TestMethod]
    public void KeepsMultipartVideosTogetherWithoutVersions()
    {
        var files = new List<FileSystemMetadata>
        {
            new() { FullName = _parent.Path + "/Show S01E01-cd1.mkv" },
            new() { FullName = _parent.Path + "/Show S01E01-cd2.mkv" },
        };
        var result = _resolver.ResolveMultiple(_parent, files, CollectionType.tvshows, null!);
        Assert.AreEqual(1, result.Items.Count);
        CollectionAssert.AreEqual(new[] { files[1].FullName }, ((JellyfinEpisode)result.Items[0]).AdditionalParts);
        Assert.AreEqual(0, result.ExtraFiles.Count);
    }

    [TestMethod]
    public void CopiedBangumiIdsCannotMergeDifferentFilenameNumbers()
    {
        var files = Files(Enumerable.Range(1, 13).Select(i => i.ToString("00")).ToArray());
        var saved = files.Select(file => Save(file, "938953")).ToArray();
        saved[0].LocalAlternateVersions = saved.Skip(1).Select(e => e.Path).ToArray();
        foreach (var episode in saved.Skip(1))
        {
            episode.IndexNumber = 0;
            episode.ParentIndexNumber = 0;
            episode.OwnerId = saved[0].Id;
            episode.SetPrimaryVersionId(saved[0].Id);
        }
        var result = _resolver.ResolveMultiple(_parent, files, CollectionType.tvshows, null!);
        Assert.AreEqual(13, result.Items.Count);
        Assert.IsTrue(result.Items.Cast<JellyfinEpisode>().All(e => e.LocalAlternateVersions.Length == 0));
        Assert.IsTrue(saved.All(e => e.OwnerId == Guid.Empty && e.PrimaryVersionId == null));
    }

    [DataTestMethod]
    [DataRow("Show S01E01.mkv", "Show S02E01.mkv")]
    [DataRow("Show [01].mkv", "Show [SP][01].mkv")]
    [DataRow("Show [OP][01].mkv", "Show [ED][01].mkv")]
    [DataRow("Show [12].mkv", "Show [12.5].mkv")]
    [DataRow("Show [1080p HEVC-10bit].mkv", "Show [720p HEVC-10bit].mkv")]
    [DataRow("Show [01-02].mkv", "Show [01].mkv")]
    public void ConflictingOrUnknownPathIdentitiesStaySeparate(string first, string second)
    {
        var files = new List<FileSystemMetadata>
        {
            new() { FullName = _parent.Path + "/" + first },
            new() { FullName = _parent.Path + "/" + second },
        };
        files.ForEach(file => Save(file, "101"));
        var result = _resolver.ResolveMultiple(_parent, files, CollectionType.tvshows, null!);
        Assert.AreEqual(2, result.Items.Count);
    }

    private List<FileSystemMetadata> Files(params string[] numbers) => numbers.Select(number => new FileSystemMetadata
    {
        FullName = $"{_parent.Path}/[Nekomoe kissaten] Arifureta Shokugyou de Sekai Saikyou {number} [BDRip 1080p HEVC-10bit FLAC].mkv",
    }).ToList();

    private JellyfinEpisode Save(FileSystemMetadata file, string? id)
    {
        var episode = new JellyfinEpisode { Id = Guid.NewGuid(), Path = file.FullName };
        if (id != null) episode.ProviderIds[Constants.ProviderName] = id;
        _library.CreateItem(episode, _parent);
        return episode;
    }

    public class FetcherProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "IsMetadataFetcherEnabled")
                return ((TypeOptions)args![1]!).MetadataFetchers.Contains((string)args[2]!);
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
