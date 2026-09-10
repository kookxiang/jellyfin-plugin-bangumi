using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Emby.Naming.Common;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.BaseItemManager;
using MediaBrowser.Controller.Entities;
using JellyfinMovie = MediaBrowser.Controller.Entities.Movies.Movie;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TitleController = Jellyfin.Plugin.Bangumi.Tools.MissingTitle.Controller;
using JellyfinEpisode = MediaBrowser.Controller.Entities.TV.Episode;
using RefreshResult = Jellyfin.Plugin.Bangumi.Tools.MissingBangumiId.RefreshResult;
using MissingTitleItem = Jellyfin.Plugin.Bangumi.Tools.MissingTitle.MissingTitleItem;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class MissingTitleTests
{
    private readonly NamingOptions _naming = new();
    private readonly List<BaseItem> _items = [];
    private readonly List<(Guid Id, MetadataRefreshOptions Options)> _queued = [];
    private MockedLibraryManager _library = null!;
    private TitleController _controller = null!;
    private QueueProxy _queue = null!;

    [TestInitialize]
    public void Setup()
    {
        _library = new MockedLibraryManager
        {
            ItemQuery = _ => _items,
            LibraryOptions = new LibraryOptions
            {
                TypeOptions = [new TypeOptions { Type = "Episode", MetadataFetchers = [Constants.ProviderName] }],
            },
        };
        var provider = DispatchProxy.Create<IProviderManager, QueueProxy>();
        _queue = (QueueProxy)(object)provider;
        _queue.OnQueue = (id, options) => _queued.Add((id, options));
        _controller = new TitleController(ServiceLocator.GetService<Logger<TitleController>>(), _library,
            DispatchProxy.Create<IBaseItemManager, EpisodeVersionResolverTests.FetcherProxy>(), provider,
            DispatchProxy.Create<IDirectoryService, QueueProxy>(), _naming);
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var item in _items) BangumiApi.CancelFreshMetadataRequest(item.Path);
    }

    [DataTestMethod]
    [DataRow("Show S01E01", "标题与文件名一致")]
    [DataRow("", "标题为空")]
    [DataRow("真实的第一集标题", null)]
    public void MatchesOnlyMissingOrGeneratedTitles(string name, string? expected)
    {
        var item = AddEpisode();
        item.Name = name;
        Assert.AreEqual(expected, TitleController.GetMatchReason(item, _naming));
    }

    [TestMethod]
    public void UsesJellyfinNamingForCleanedTitles()
    {
        var item = new JellyfinMovie { Path = "/media/Example (2020).mkv", Name = "Example" };
        item.ProviderIds[Constants.ProviderName] = "123";
        Assert.AreEqual("标题与 Jellyfin 文件名解析结果一致", TitleController.GetMatchReason(item, _naming));
        item.Name = "Exampl";
        Assert.IsNull(TitleController.GetMatchReason(item, _naming), "No fuzzy title matching.");
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("invalid")]
    public void RequiresValidExistingBangumiId(string id)
    {
        var item = AddEpisode();
        item.ProviderIds[Constants.ProviderName] = id;
        Assert.IsNull(TitleController.GetMatchReason(item, _naming));
    }

    [TestMethod]
    public void ExcludesLockedVirtualAndRemoteItems()
    {
        var item = AddEpisode();
        item.IsLocked = true;
        Assert.IsNull(TitleController.GetMatchReason(item, _naming));
        item.IsLocked = false;
        item.LockedFields = [MetadataField.Name];
        Assert.IsNull(TitleController.GetMatchReason(item, _naming));
        item.LockedFields = [];
        item.IsVirtualItem = true;
        Assert.IsNull(TitleController.GetMatchReason(item, _naming));
        item.IsVirtualItem = false;
        item.Path = "https://example.invalid/video.mkv";
        Assert.IsNull(TitleController.GetMatchReason(item, _naming));
    }

    [TestMethod]
    public async Task ScanHonorsLibraryDirectoryAndSkipConfiguration()
    {
        var included = AddEpisode();
        var skipped = AddEpisode();
        var outside = AddEpisode();
        await File.WriteAllTextAsync(Path.GetDirectoryName(skipped.Path)! + "/bangumi.ini", "[Bangumi]\nSkip=on\n");
        _library.VirtualFolders.Add(new VirtualFolderInfo
        {
            ItemId = "anime", Name = "Anime", Locations = [Path.GetDirectoryName(included.Path)!, Path.GetDirectoryName(skipped.Path)!],
        });
        var result = await _controller.GetItems("anime");
        var found = (List<MissingTitleItem>)((OkObjectResult)result.Result!).Value!;
        CollectionAssert.AreEqual(new[] { included.Id }, found.Select(item => item.Id).ToArray());
        Assert.AreEqual(0, _queued.Count, "Scanning must not refresh anything.");
        Assert.IsInstanceOfType((await _controller.GetItems("unknown")).Result, typeof(BadRequestObjectResult));
        Assert.IsNotNull(outside);
    }

    [TestMethod]
    public async Task ScanDefaultsToRecentlyModifiedVideos()
    {
        var recent = AddEpisode();
        recent.DateModified = DateTime.UtcNow.AddDays(-10);
        var old = AddEpisode();
        old.DateModified = DateTime.UtcNow.AddMonths(-2);
        var unknown = AddEpisode();
        unknown.DateModified = default;

        var response = await _controller.GetItems();
        var found = (List<MissingTitleItem>)((OkObjectResult)response.Result!).Value!;
        CollectionAssert.AreEqual(new[] { recent.Id }, found.Select(item => item.Id).ToArray());

        response = await _controller.GetItems(recentOnly: false);
        found = (List<MissingTitleItem>)((OkObjectResult)response.Result!).Value!;
        CollectionAssert.AreEquivalent(new[] { recent.Id, old.Id, unknown.Id }, found.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public async Task SubmittingQueuesJellyfinRefreshAndRevalidatesItems()
    {
        var included = AddEpisode();
        var changed = AddEpisode();
        changed.Name = "已经更新的标题";
        var locked = AddEpisode();
        locked.IsLocked = true;
        var response = await _controller.Refresh($"{included.Id},{included.Id},{changed.Id},{locked.Id},{Guid.NewGuid()}");
        var result = (RefreshResult)((OkObjectResult)response.Result!).Value!;
        Assert.AreEqual(1, result.QueuedCount);
        Assert.AreEqual(3, result.SkippedCount);
        Assert.AreEqual(included.Id, _queued.Single().Id);
        Assert.AreEqual(MetadataRefreshMode.FullRefresh, _queued.Single().Options.MetadataRefreshMode);
        Assert.IsTrue(_queued.Single().Options.ReplaceAllMetadata);
        Assert.AreEqual("Show S01E01", included.Name, "The tool must not write metadata itself.");
        using (BangumiApi.BeginRequestedRefresh(included.Path)) Assert.IsTrue(BangumiApi.IsFreshMetadataRefresh);
        Assert.IsFalse(BangumiApi.IsFreshMetadataRefresh);
        Assert.IsNull(BangumiApi.BeginRequestedRefresh(included.Path), "A request is consumed only once.");
    }

    [TestMethod]
    public async Task DisabledProviderAndQueueFailuresCannotLeaveRefreshRequests()
    {
        var item = AddEpisode();
        _library.LibraryOptions.TypeOptions[0].MetadataFetchers = [];
        await _controller.Refresh(item.Id.ToString());
        Assert.AreEqual(0, _queued.Count);
        Assert.IsNull(BangumiApi.BeginRequestedRefresh(item.Path));
        _library.LibraryOptions.TypeOptions[0].MetadataFetchers = [Constants.ProviderName];
        _queue.OnQueue = (_, _) => throw new InvalidOperationException("queue failure");
        var response = await _controller.Refresh(item.Id.ToString());
        Assert.AreEqual(1, ((RefreshResult)((OkObjectResult)response.Result!).Value!).FailedCount);
        Assert.IsNull(BangumiApi.BeginRequestedRefresh(item.Path));
    }

    [TestMethod]
    public async Task InvalidSelectionDoesNotPartiallyQueue()
    {
        var item = AddEpisode();
        Assert.IsInstanceOfType((await _controller.Refresh($"{item.Id},invalid")).Result, typeof(BadRequestObjectResult));
        Assert.AreEqual(0, _queued.Count);
    }

    private JellyfinEpisode AddEpisode()
    {
        var item = new JellyfinEpisode
        {
            Id = Guid.NewGuid(), Name = "Show S01E01", DateModified = DateTime.UtcNow,
            Path = FakePath.CreateFile($"missing-title-{Guid.NewGuid()}/Show S01E01.mkv"),
            SeriesId = Guid.NewGuid(), SeriesName = "Show",
        };
        item.ProviderIds[Constants.ProviderName] = "259013";
        _items.Add(item);
        _library.CreateItem(item, null);
        return item;
    }

    public class QueueProxy : DispatchProxy
    {
        public Action<Guid, MetadataRefreshOptions>? OnQueue { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != "QueueRefresh") throw new NotSupportedException(targetMethod?.Name);
            OnQueue!((Guid)args![0]!, (MetadataRefreshOptions)args[1]!);
            return null;
        }
    }
}
