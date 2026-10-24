using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.OAuth;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Jellyfin.Plugin.Bangumi.Test.Util;
using Jellyfin.Plugin.Bangumi.Tools.EpisodeSubjectMismatch;
using MediaBrowser.Controller.Entities;
using TvSeason = MediaBrowser.Controller.Entities.TV.Season;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScanController = Jellyfin.Plugin.Bangumi.Tools.EpisodeSubjectMismatch.Controller;
using TvEpisode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class EpisodeSubjectMismatchTests
{
    private static int _nextId = 1800000000;
    private readonly List<BaseItem> _items = [];
    private MockedLibraryManager _library = null!;
    private LookupApi _api = null!;
    private ScanController _controller = null!;
    private TvSeason _season = null!;
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = "subject-scan-" + Guid.NewGuid();
        _library = new MockedLibraryManager
        {
            ItemsResultQuery = query => new QueryResult<BaseItem>(query.StartIndex, _items.Count,
                _items.Skip(query.StartIndex ?? 0).Take(query.Limit ?? int.MaxValue).ToList()),
        };
        _api = new LookupApi();
        _controller = new ScanController(_library, _api, ServiceLocator.GetService<Logger<ScanController>>());
        _season = new TvSeason { Path = FakePath.Create(_root), Name = "Season 1", IndexNumber = 1 };
        _season.SetProviderId(Constants.ProviderName, "100");
        _library.CreateItem(_season, null);
    }

    [TestMethod]
    public async Task ReportsMismatchWhileKeepingBothStoredIdsUnchanged()
    {
        var matching = AddEpisode(100);
        var mismatch = AddEpisode(200);
        var duplicate = AddEpisode(200);
        duplicate.ProviderIds = new(mismatch.ProviderIds);
        var result = await Scan();
        Assert.AreEqual(3, result.CheckedCount);
        Assert.AreEqual(2, result.Items.Count);
        Assert.AreEqual(2, _api.Requests);
        var row = result.Items[0];
        Assert.AreEqual("Mismatch", row.Status);
        Assert.AreEqual(100, row.SeasonBangumiId);
        Assert.AreEqual(200, row.ActualSubjectId);
        Assert.AreEqual(_season.Id, row.SeasonId);
        Assert.AreEqual("100", _season.GetProviderId(Constants.ProviderName));
        Assert.AreEqual(row.EpisodeBangumiId.ToString(), mismatch.GetProviderId(Constants.ProviderName));
        Assert.AreEqual(_season.Id, matching.SeasonId);
    }

    [TestMethod]
    public async Task ExcludesSpecialsFromHostApiFilenameAndDirectoryConfiguration()
    {
        AddEpisode(200).ParentIndexNumber = 0;
        AddEpisode(200, type: 1);
        AddEpisode(200, fileName: "OVA01.mkv");
        var configured = AddEpisode(200);
        await File.WriteAllTextAsync(Path.Join(Path.GetDirectoryName(configured.Path), "bangumi.ini"), "[Bangumi]\nType=Special\n");
        var result = await Scan();
        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(4, result.SkippedCount);
        Assert.AreEqual(1, _api.Requests, "Only the API-classified special needs a lookup.");
    }

    [TestMethod]
    public async Task MissingSubjectAndQueryFailureAreUnverifiedInsteadOfHealthy()
    {
        var failure = AddEpisode(200);
        _api.FailId = int.Parse(failure.GetProviderId(Constants.ProviderName)!);
        AddEpisode(200).ProviderIds = new(failure.ProviderIds);
        var missing = AddEpisode(200);
        missing.SeasonId = Guid.Empty;
        // The mock host has no parent, so this item has no associated season.
        var result = await Scan();
        Assert.AreEqual(0, result.CheckedCount);
        Assert.AreEqual(3, result.Items.Count);
        Assert.IsTrue(result.Items.All(item => item.Status == "Unverified"));
        Assert.IsTrue(result.Items.Any(item => item.ActualSubjectId == 200));
        Assert.AreEqual(2, _api.Requests, "A failed ID is not requested repeatedly within a page.");
    }

    [TestMethod]
    public async Task PagesTheLibraryAndAppliesLibraryScopeBeforeApiQueries()
    {
        var first = AddEpisode(200);
        var second = AddEpisode(200);
        _library.VirtualFolders.Add(new VirtualFolderInfo
        {
            ItemId = "legacy", Name = "Anime", Locations = [Path.GetDirectoryName(second.Path)!],
        });
        var firstPage = await Scan("legacy", 0, 1);
        Assert.AreEqual(1, firstPage.NextStartIndex);
        Assert.AreEqual(1, firstPage.SkippedCount);
        Assert.AreEqual(0, _api.Requests);
        var secondPage = await Scan("legacy", 1, 1);
        Assert.IsNull(secondPage.NextStartIndex);
        Assert.AreEqual(second.Id, secondPage.Items.Single().Id);
        Assert.AreNotEqual(first.Id, second.Id);
        var folderId = Guid.NewGuid();
        _library.VirtualFolders.Add(new VirtualFolderInfo { ItemId = folderId.ToString(), Name = "Indexed" });
        _library.ItemsResultQuery = query =>
        {
            Assert.AreEqual(folderId, query.ParentId);
            Assert.AreEqual(20, query.Limit);
            Assert.IsTrue(query.Recursive);
            Assert.IsFalse(query.IsVirtualItem);
            return new QueryResult<BaseItem>();
        };
        await Scan(folderId.ToString());
    }

    [TestMethod]
    public async Task RejectsUnknownLibrariesAndInvalidPageSizes()
    {
        Assert.IsInstanceOfType<BadRequestObjectResult>((await _controller.GetItems("unknown")).Result);
        Assert.IsInstanceOfType<BadRequestObjectResult>((await _controller.GetItems(limit: 51)).Result);
        Assert.IsInstanceOfType<BadRequestObjectResult>((await _controller.GetItems(startIndex: -1)).Result);
        Assert.AreEqual(0, _api.Requests);
    }

    [TestMethod]
    public async Task CallerCancellationStopsScanning()
    {
        AddEpisode(200);
        using var cancellation = new CancellationTokenSource();
        _api.Cancel = cancellation;
        try
        {
            await _controller.GetItems(cancellationToken: cancellation.Token);
            Assert.Fail("Caller cancellation must propagate.");
        }
        catch (OperationCanceledException)
        {
            Assert.IsTrue(cancellation.IsCancellationRequested);
        }
    }

    private TvEpisode AddEpisode(int subjectId, int type = 0, string fileName = "01.mkv")
    {
        var id = Interlocked.Increment(ref _nextId);
        var episode = new TvEpisode
        {
            Path = FakePath.CreateFile($"{_root}/{id}/{fileName}"), Name = "Episode",
            SeasonId = _season.Id, ParentIndexNumber = 1,
        };
        episode.SetProviderId(Constants.ProviderName, id.ToString());
        _api.Episodes[id] = (subjectId, type);
        _library.CreateItem(episode, _season);
        _items.Add(episode);
        return episode;
    }

    private async Task<ScanResult> Scan(string? library = null, int start = 0, int limit = 20) =>
        (ScanResult)((OkObjectResult)(await _controller.GetItems(library, start, limit)).Result!).Value!;

    private sealed class LookupApi() : BangumiApi(new Bangumi.Archive.ArchiveData(new MockedApplicationPaths()),
        ServiceLocator.GetService<OAuthStore>(), ServiceLocator.GetService<Logger<BangumiApi>>())
    {
        public Dictionary<int, (int Subject, int Type)> Episodes { get; } = [];
        public int Requests { get; private set; }
        public int? FailId { get; set; }
        public CancellationTokenSource? Cancel { get; set; }
        public override HttpClient GetHttpClient(bool allowAutoRedirect = true) => new(new Handler(this));

        private sealed class Handler(LookupApi api) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                api.Requests++;
                if (api.Cancel is not null) await api.Cancel.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
                var id = int.Parse(request.RequestUri!.Segments.Last());
                if (api.FailId == id) throw new HttpRequestException("Unavailable");
                var (subject, type) = api.Episodes[id];
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"id":{{id}},"subject_id":{{subject}},"type":{{type}}}"""),
                };
            }
        }
    }
}
