using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.OAuth;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;
using Jellyfin.Plugin.Bangumi.Parser.TorrentParser;
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TvSeason = MediaBrowser.Controller.Entities.TV.Season;
using TvSeries = MediaBrowser.Controller.Entities.TV.Series;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
[DoNotParallelize]
public class SeasonDetectionTests
{
    private readonly PluginConfiguration _configuration = ServiceLocator.GetService<Bangumi.Plugin>().Configuration;
    private readonly ILibraryManager _library = ServiceLocator.GetService<ILibraryManager>();
    private EpisodeParserType _previousParser;
    private bool _previousSeasonInference;
    private bool _previousEpisodeInference;
    private bool _previousFolderMatching;
    private bool _previousSeasonTitle;

    [TestInitialize]
    public void SaveConfiguration()
    {
        _previousParser = _configuration.EpisodeParser;
        _previousSeasonInference = _configuration.UseBangumiRelationChainForSeasonNumber;
        _previousEpisodeInference = _configuration.UseBangumiRelationChainForEpisodeSeasonNumber;
        _previousFolderMatching = _configuration.ProcessMultiSeasonFolder;
        _previousSeasonTitle = _configuration.UseBangumiSeasonTitle;
        _configuration.EpisodeParser = EpisodeParserType.Basic;
        _configuration.UseBangumiRelationChainForSeasonNumber = true;
        _configuration.UseBangumiRelationChainForEpisodeSeasonNumber = true;
        _configuration.ProcessMultiSeasonFolder = false;
        _configuration.UseBangumiSeasonTitle = true;
    }

    [TestCleanup]
    public void RestoreConfiguration()
    {
        _configuration.EpisodeParser = _previousParser;
        _configuration.UseBangumiRelationChainForSeasonNumber = _previousSeasonInference;
        _configuration.UseBangumiRelationChainForEpisodeSeasonNumber = _previousEpisodeInference;
        _configuration.ProcessMultiSeasonFolder = _previousFolderMatching;
        _configuration.UseBangumiSeasonTitle = _previousSeasonTitle;
    }

    [DataTestMethod]
    [DataRow(RelationFailure.Http)]
    [DataRow(RelationFailure.Timeout)]
    [DataRow(RelationFailure.InvalidJson)]
    public async Task SeasonKeepsMetadataWhenRelationLookupFails(RelationFailure failure)
    {
        var api = new FailingRelationsApi(failure);
        var result = await CreateSeasonProvider(api).GetMetadata(SeasonInfoFor(api.SubjectId), CancellationToken.None);

        Assert.AreEqual(1, api.RelationRequests);
        Assert.IsTrue(result.HasMetadata);
        Assert.AreEqual("Matched season", result.Item.Name);
        Assert.AreEqual(api.SubjectId.ToString(), result.Item.ProviderIds[Constants.ProviderName]);
        Assert.IsNull(result.Item.IndexNumber, "Failed inference must not invent a season number.");
    }

    [DataTestMethod]
    [DataRow(RelationFailure.Http)]
    [DataRow(RelationFailure.Timeout)]
    [DataRow(RelationFailure.InvalidJson)]
    public async Task EpisodeKeepsMetadataAndFallsBackWhenRelationLookupFails(RelationFailure failure)
    {
        foreach (var parser in new[] { EpisodeParserType.Basic, EpisodeParserType.AnitomySharp, EpisodeParserType.Torrent })
        {
            _configuration.EpisodeParser = parser;
            var api = new FailingRelationsApi(failure);
            var result = await CreateEpisodeProvider(api).GetMetadata(EpisodeInfoFor(api.SubjectId), CancellationToken.None);

            Assert.AreEqual(1, api.RelationRequests, $"{parser} must exercise the failing relation lookup.");
            Assert.IsTrue(result.HasMetadata);
            Assert.AreEqual("Matched episode", result.Item.Name);
            Assert.AreEqual(api.SubjectId.ToString(), result.Item.ProviderIds[Constants.ProviderName]);
            Assert.AreEqual(1, result.Item.IndexNumber);
            Assert.AreEqual(parser == EpisodeParserType.Basic ? 3 : 2, result.Item.ParentIndexNumber,
                "Use the parsed S02 when available, otherwise the existing episode season number.");
        }
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CallerCancellationDuringRelationLookupIsNotSwallowed(bool season)
    {
        using var cancellation = new CancellationTokenSource();
        var api = new FailingRelationsApi(RelationFailure.Cancel, cancellation);
        try
        {
            if (season)
                await CreateSeasonProvider(api).GetMetadata(SeasonInfoFor(api.SubjectId), cancellation.Token);
            else
                await CreateEpisodeProvider(api).GetMetadata(EpisodeInfoFor(api.SubjectId), cancellation.Token);
            Assert.Fail("Caller cancellation must propagate out of the metadata provider.");
        }
        catch (OperationCanceledException)
        {
            Assert.IsTrue(cancellation.IsCancellationRequested);
            Assert.AreEqual(1, api.RelationRequests, "Cancel during inference, not at provider entry.");
        }
    }

    [DataTestMethod]
    [DataRow(EpisodeParserType.Basic)]
    [DataRow(EpisodeParserType.AnitomySharp)]
    [DataRow(EpisodeParserType.Torrent)]
    public async Task MatchedSeasonIdIsUsedWhenScrapingItsEpisodes(EpisodeParserType parser)
    {
        _configuration.EpisodeParser = parser;
        _configuration.ProcessMultiSeasonFolder = true;
        // A numbered parent should determine the episode season without episode-level inference.
        _configuration.UseBangumiRelationChainForEpisodeSeasonNumber = false;
        var root = "season-handoff-" + Guid.NewGuid();
        var series = new TvSeries
        {
            Path = FakePath.Create(root),
            Name = "战姬绝唱SYMPHOGEAR",
            ProviderIds = new() { [Constants.ProviderName] = "25834" }
        };
        _library.CreateItem(series, null);
        var season = new TvSeason
        {
            Path = FakePath.Create(root + "/戦姫絶唱シンフォギアXV"),
            Name = "戦姫絶唱シンフォギアXV"
        };
        _library.CreateItem(season, series);
        var seasonResult = await ServiceLocator.GetService<SeasonProvider>().GetMetadata(new SeasonInfo
        {
            Path = season.Path,
            SeriesProviderIds = series.ProviderIds
        }, CancellationToken.None);
        Assert.IsTrue(seasonResult.HasMetadata);
        Assert.AreEqual("170689", seasonResult.Item.ProviderIds[Constants.ProviderName]);
        Assert.AreEqual(5, seasonResult.Item.IndexNumber);

        // Apply the returned season metadata before running the episode provider, as the host does.
        season.ProviderIds = new Dictionary<string, string>(seasonResult.Item.ProviderIds);
        season.IndexNumber = seasonResult.Item.IndexNumber;
        var episodeResult = await ServiceLocator.GetService<EpisodeProvider>().GetMetadata(new EpisodeInfo
        {
            Path = FakePath.CreateFile(root + "/戦姫絶唱シンフォギアXV/Senki Zesshou Symphogear XV [01].mkv"),
            IndexNumber = 1,
            SeriesProviderIds = series.ProviderIds
        }, CancellationToken.None);

        Assert.IsTrue(episodeResult.HasMetadata);
        Assert.AreEqual("893802", episodeResult.Item.ProviderIds[Constants.ProviderName]);
        Assert.AreEqual("人類史の彼方から", episodeResult.Item.Name);
        Assert.AreEqual(1, episodeResult.Item.IndexNumber);
        Assert.AreEqual(5, episodeResult.Item.ParentIndexNumber);
        Assert.AreEqual(season.Id, episodeResult.Item.SeasonId);
    }

    private static SeasonInfo SeasonInfoFor(int subjectId) => new()
    {
        Path = FakePath.Create("relation-season-" + Guid.NewGuid()),
        ProviderIds = new() { [Constants.ProviderName] = subjectId.ToString() }
    };

    private EpisodeInfo EpisodeInfoFor(int subjectId)
    {
        var folder = "relation-episode-" + Guid.NewGuid();
        FakePath.CreateSeason(_library, folder);
        return new EpisodeInfo
        {
            Path = FakePath.CreateFile(folder + "/Example.S02E01.mkv"),
            IndexNumber = 1,
            ParentIndexNumber = 3,
            SeriesProviderIds = new() { [Constants.ProviderName] = subjectId.ToString() }
        };
    }

    private SeasonProvider CreateSeasonProvider(BangumiApi api) =>
        new(api, ServiceLocator.GetService<Logger<SeasonProvider>>(), _library);

    private EpisodeProvider CreateEpisodeProvider(BangumiApi api) => new(api,
        ServiceLocator.GetService<Logger<EpisodeProvider>>(), _library,
        ServiceLocator.GetService<IMediaSourceManager>(),
        ServiceLocator.GetService<Logger<AnitomyEpisodeParser>>(),
        ServiceLocator.GetService<Logger<BasicEpisodeParser>>(),
        ServiceLocator.GetService<Logger<TorrentEpisodeParser>>());

    public enum RelationFailure { Http, Timeout, InvalidJson, Cancel }

    private sealed class FailingRelationsApi(RelationFailure failure, CancellationTokenSource? cancellation = null)
        : BangumiApi(new Bangumi.Archive.ArchiveData(new MockedApplicationPaths()),
            ServiceLocator.GetService<OAuthStore>(), ServiceLocator.GetService<Logger<BangumiApi>>())
    {
        // The HTTP cache is shared, so every test gets a distinct synthetic subject.
        private static int _nextSubjectId = 1900000000;
        private readonly RelationFailure _failure = failure;
        private readonly CancellationTokenSource? _cancellation = cancellation;
        public int SubjectId { get; } = Interlocked.Increment(ref _nextSubjectId);
        public int RelationRequests { get; private set; }
        public override HttpClient GetHttpClient(bool allowAutoRedirect = true) => new(new Handler(this));

        private sealed class Handler(FailingRelationsApi api) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = request.RequestUri!.AbsolutePath;
                string body;
                if (path == $"/v0/subjects/{api.SubjectId}/subjects")
                {
                    api.RelationRequests++;
                    switch (api._failure)
                    {
                        case RelationFailure.Http:
                            throw new HttpRequestException("Relation service unavailable");
                        case RelationFailure.Timeout:
                            throw new TaskCanceledException("Relation request timed out", new TimeoutException());
                        case RelationFailure.Cancel:
                            await api._cancellation!.CancelAsync();
                            cancellationToken.ThrowIfCancellationRequested();
                            throw new InvalidOperationException("Expected caller cancellation");
                        default:
                            body = "invalid JSON";
                            break;
                    }
                }
                else if (path == $"/v0/subjects/{api.SubjectId}")
                    body = $$"""{"id":{{api.SubjectId}},"name":"Matched season","name_cn":"Matched season","type":2}""";
                else if (path == "/v0/episodes")
                    body = $$"""{"total":1,"data":[{"id":{{api.SubjectId}},"subject_id":{{api.SubjectId}},"name":"Matched episode","name_cn":"Matched episode","sort":1,"type":0}]}""";
                else if (path == $"/v0/subjects/{api.SubjectId}/persons" || path == $"/v0/subjects/{api.SubjectId}/characters")
                    body = "[]";
                else
                    throw new AssertFailedException($"Unexpected request: {request.RequestUri}");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            }
        }
    }
}
