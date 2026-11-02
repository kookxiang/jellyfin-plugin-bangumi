using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System;
using Jellyfin.Plugin.Bangumi.OAuth;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Jellyfin.Plugin.Bangumi.Test.Util;
using Jellyfin.Plugin.Bangumi.Tools.EpisodeSubjectMismatch;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TvSeason = MediaBrowser.Controller.Entities.TV.Season;
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

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SpecialFilterCoversHostApiFilenameAndDirectoryConfiguration(bool skipSpecials)
    {
        AddEpisode(200).ParentIndexNumber = 0;
        AddEpisode(200, type: 1);
        AddEpisode(200, fileName: "OVA01.mkv");
        var configured = AddEpisode(200);
        await File.WriteAllTextAsync(Path.Join(Path.GetDirectoryName(configured.Path), "bangumi.ini"), "[Bangumi]\nType=Special\n");
        var result = await Scan(skipSpecials: skipSpecials);
        Assert.AreEqual(skipSpecials ? 0 : 4, result.Items.Count);
        Assert.AreEqual(skipSpecials ? 4 : 0, result.SkippedCount);
        Assert.AreEqual(skipSpecials ? 1 : 4, _api.Requests);
    }

    [TestMethod]
    public async Task RecentFilterDefaultsToLastMonthAndCanIncludeOldOrUnknownDates()
    {
        AddEpisode(200).DateModified = DateTime.UtcNow.AddDays(-10);
        AddEpisode(200).DateModified = DateTime.UtcNow.AddMonths(-2);
        AddEpisode(200).DateModified = default;
        var recent = await Scan();
        Assert.AreEqual(1, recent.CheckedCount);
        Assert.AreEqual(2, recent.SkippedCount);
        Assert.AreEqual(1, _api.Requests, "Old files must be filtered before Bangumi lookups.");
        var all = await Scan(recentOnly: false);
        Assert.AreEqual(3, all.CheckedCount);
        Assert.AreEqual(0, all.SkippedCount);
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

    [TestMethod]
    public async Task StreamSendsCumulativeProgressAcrossPagesWithoutRepeatingItems()
    {
        for (var index = 0; index < 53; index++) AddEpisode(index % 2 == 0 ? 200 : 100);
        using var body = new MemoryStream();
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        _controller.Response.Body = body;
        await _controller.Scan();
        Assert.AreEqual("text/event-stream; charset=utf-8", _controller.Response.ContentType);
        Assert.AreEqual("no", _controller.Response.Headers["X-Accel-Buffering"].ToString());
        var events = ReadEvents(body);
        Assert.AreEqual("complete", events.Last().Kind);
        Assert.AreEqual(53, events.Last().Data.GetProperty("ScannedCount").GetInt32());
        Assert.AreEqual(53, events.Last().Data.GetProperty("CheckedCount").GetInt32());
        var ids = events.SelectMany(entry => entry.Data.GetProperty("Items").EnumerateArray())
            .Select(item => item.GetProperty("Id").GetString()).ToList();
        Assert.AreEqual(27, ids.Count);
        Assert.AreEqual(27, ids.Distinct().Count());
        Assert.IsTrue(events.Where(entry => entry.Kind == "progress")
            .Select(entry => entry.Data.GetProperty("ScannedCount").GetInt32()).SequenceEqual(
                events.Where(entry => entry.Kind == "progress")
                    .Select(entry => entry.Data.GetProperty("ScannedCount").GetInt32()).Order()));
    }

    [TestMethod]
    public async Task StreamDeliversResultsBeforePageEndsAndCancellationStopsLookups()
    {
        AddEpisode(200);
        AddEpisode(200);
        AddEpisode(200);
        using var cancellation = new CancellationTokenSource();
        using var body = new MemoryStream();
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        _controller.Response.Body = body;
        _api.BeforeRequest = () =>
        {
            if (_api.Requests != 2) return;
            var events = ReadEvents(body);
            Assert.IsTrue(events.Any(entry => entry.Data.GetProperty("Items").GetArrayLength() == 1),
                "The first result must be flushed before the second lookup finishes.");
            cancellation.Cancel();
        };
        await _controller.Scan(cancellationToken: cancellation.Token);
        Assert.AreEqual(2, _api.Requests);
        Assert.IsFalse(ReadEvents(body).Any(entry => entry.Kind == "complete"));
    }

    [TestMethod]
    public async Task StreamRejectsUnknownLibraryBeforeOpeningAndReportsTraversalFailure()
    {
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        Assert.IsInstanceOfType<BadRequestObjectResult>(await _controller.Scan("unknown"));
        Assert.IsNull(_controller.Response.ContentType);
        using var body = new MemoryStream();
        _controller.Response.Body = body;
        _library.ItemsResultQuery = _ => throw new InvalidOperationException("Database unavailable");
        await _controller.Scan();
        var events = ReadEvents(body);
        Assert.AreEqual("error", events.Last().Kind);
        Assert.IsFalse(events.Any(entry => entry.Kind == "complete"));
    }

    private static List<(string Kind, JsonElement Data)> ReadEvents(MemoryStream body) =>
        System.Text.Encoding.UTF8.GetString(body.ToArray()).Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(frame =>
            {
                var lines = frame.Split('\n');
                using var document = JsonDocument.Parse(lines[1][6..]);
                return (lines[0][7..], document.RootElement.Clone());
            }).ToList();

    private TvEpisode AddEpisode(int subjectId, int type = 0, string fileName = "01.mkv")
    {
        var id = Interlocked.Increment(ref _nextId);
        var episode = new TvEpisode
        {
            Path = FakePath.CreateFile($"{_root}/{id}/{fileName}"), Name = "Episode",
            SeasonId = _season.Id, ParentIndexNumber = 1, DateModified = DateTime.UtcNow,
        };
        episode.SetProviderId(Constants.ProviderName, id.ToString());
        _api.Episodes[id] = (subjectId, type);
        _library.CreateItem(episode, _season);
        _items.Add(episode);
        return episode;
    }

    private async Task<ScanResult> Scan(string? library = null, int start = 0, int limit = 20,
        bool recentOnly = true, bool skipSpecials = true) =>
        (ScanResult)((OkObjectResult)(await _controller.GetItems(library, start, limit, recentOnly, skipSpecials)).Result!).Value!;

    private sealed class LookupApi() : BangumiApi(new Bangumi.Archive.ArchiveData(new MockedApplicationPaths()),
        ServiceLocator.GetService<OAuthStore>(), ServiceLocator.GetService<Logger<BangumiApi>>())
    {
        public Dictionary<int, (int Subject, int Type)> Episodes { get; } = [];
        public int Requests { get; private set; }
        public Action? BeforeRequest { get; set; }
        public int? FailId { get; set; }
        public CancellationTokenSource? Cancel { get; set; }
        public override HttpClient GetHttpClient(bool allowAutoRedirect = true) => new(new Handler(this));

        private sealed class Handler(LookupApi api) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                api.Requests++;
                api.BeforeRequest?.Invoke();
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
