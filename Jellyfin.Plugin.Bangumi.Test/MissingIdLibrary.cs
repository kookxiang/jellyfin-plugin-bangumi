using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.BaseItemManager;
using MediaBrowser.Controller.Entities;
using JellyfinMovie = MediaBrowser.Controller.Entities.Movies.Movie;
using JellyfinEpisode = MediaBrowser.Controller.Entities.TV.Episode;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissingIdController = Jellyfin.Plugin.Bangumi.Tools.MissingBangumiId.Controller;
using MissingIdItem = Jellyfin.Plugin.Bangumi.Tools.MissingBangumiId.MissingBangumiIdItem;
using RefreshResult = Jellyfin.Plugin.Bangumi.Tools.MissingBangumiId.RefreshResult;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class MissingIdLibraryTestCases
{
    [DataTestMethod]
    [DataRow("/media/anime/series/episode.mkv", "/media/anime", true)]
    [DataRow("/media/anime-other/episode.mkv", "/media/anime", false)]
    [DataRow("/media/anime/../movie/episode.mkv", "/media/anime", false)]
    [DataRow(null, "/media/anime", false)]
    [DataRow("/media/anime/episode.mkv", "", false)]
    public void RestrictsResultsToLibraryDirectory(string? path, string location, bool expected)
    {
        Assert.AreEqual(expected, MissingIdController.IsInLibrary(path, location));
    }

    [TestMethod]
    public void ScanDefaultsToRecentlyModifiedVideosAndAllowsAllDates()
    {
        var recentEpisode = new JellyfinEpisode { Id = Guid.NewGuid(), DateModified = DateTime.UtcNow.AddDays(-10) };
        var recentMovie = new JellyfinMovie { Id = Guid.NewGuid(), DateModified = DateTime.UtcNow.AddDays(-5) };
        var old = new JellyfinEpisode { Id = Guid.NewGuid(), DateModified = DateTime.UtcNow.AddMonths(-2) };
        var unknown = new JellyfinMovie { Id = Guid.NewGuid(), DateModified = default };
        var identified = new JellyfinEpisode { Id = Guid.NewGuid(), DateModified = DateTime.UtcNow };
        identified.ProviderIds[Constants.ProviderName] = "123";
        var controller = CreateController([recentEpisode, recentMovie, old, unknown, identified]);

        var found = (List<MissingIdItem>)((OkObjectResult)controller.GetItems().Result!).Value!;
        CollectionAssert.AreEquivalent(new[] { recentEpisode.Id, recentMovie.Id }, found.Select(item => item.Id).ToArray());

        found = (List<MissingIdItem>)((OkObjectResult)controller.GetItems(recentOnly: false).Result!).Value!;
        CollectionAssert.AreEquivalent(new[] { recentEpisode.Id, recentMovie.Id, old.Id, unknown.Id }, found.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void RecentFilterCombinesWithSelectedLibrary()
    {
        var included = new JellyfinMovie { Id = Guid.NewGuid(), Path = "/media/anime/recent.mkv", DateModified = DateTime.UtcNow };
        var old = new JellyfinMovie { Id = Guid.NewGuid(), Path = "/media/anime/old.mkv", DateModified = DateTime.UtcNow.AddMonths(-2) };
        var outside = new JellyfinMovie { Id = Guid.NewGuid(), Path = "/media/anime-other/recent.mkv", DateModified = DateTime.UtcNow };
        var controller = CreateController([included, old, outside]);

        var found = (List<MissingIdItem>)((OkObjectResult)controller.GetItems("anime").Result!).Value!;
        CollectionAssert.AreEqual(new[] { included.Id }, found.Select(item => item.Id).ToArray());
        found = (List<MissingIdItem>)((OkObjectResult)controller.GetItems("anime", recentOnly: false).Result!).Value!;
        CollectionAssert.AreEquivalent(new[] { included.Id, old.Id }, found.Select(item => item.Id).ToArray());
        Assert.IsInstanceOfType(controller.GetItems("unknown").Result, typeof(BadRequestObjectResult));
    }

    [TestMethod]
    public void RefreshStillQueuesSelectedOlderVideos()
    {
        var old = new JellyfinMovie { Id = Guid.NewGuid(), DateModified = DateTime.UtcNow.AddMonths(-2) };
        var queued = new List<Guid>();
        var controller = CreateController([old], queued);

        var result = (RefreshResult)((OkObjectResult)controller.Refresh(old.Id.ToString()).Result!).Value!;
        Assert.AreEqual(1, result.QueuedCount);
        CollectionAssert.AreEqual(new[] { old.Id }, queued);
    }

    private static MissingIdController CreateController(List<BaseItem> items, List<Guid>? queued = null)
    {
        var library = DispatchProxy.Create<ILibraryManager, LibraryProxy>();
        ((LibraryProxy)(object)library).Items = items;
        var provider = DispatchProxy.Create<IProviderManager, MissingTitleTests.QueueProxy>();
        ((MissingTitleTests.QueueProxy)(object)provider).OnQueue = (id, _) => queued?.Add(id);
        return new MissingIdController(ServiceLocator.GetService<Logger<MissingIdController>>(), library,
            DispatchProxy.Create<IBaseItemManager, MockedBaseItemManager>(), provider,
            DispatchProxy.Create<IDirectoryService, MissingTitleTests.QueueProxy>());
    }

    public class LibraryProxy : DispatchProxy
    {
        public List<BaseItem> Items { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "GetItemList" => Items.Where(item => ((InternalItemsQuery)args![0]!).ItemIds.Length == 0
                || ((InternalItemsQuery)args[0]!).ItemIds.Contains(item.Id)).ToList(),
            "GetCollectionFolders" => new List<Folder>(),
            "GetLibraryOptions" => new LibraryOptions
            {
                TypeOptions = [new TypeOptions { Type = "Movie", MetadataFetchers = [Constants.ProviderName] }],
            },
            "GetVirtualFolders" => new List<VirtualFolderInfo>
            {
                new() { ItemId = "anime", Name = "Anime", Locations = ["/media/anime"] },
            },
            _ => throw new NotSupportedException(targetMethod?.Name),
        };
    }
}
