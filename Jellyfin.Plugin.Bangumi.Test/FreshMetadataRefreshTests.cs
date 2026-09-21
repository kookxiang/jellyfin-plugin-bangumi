using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.OAuth;
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class FreshMetadataRefreshTests
{
    [TestMethod]
    public async Task RequestedRefreshBypassesAndReplacesCachedResponse()
    {
        var api = new ChangingApi(new Bangumi.Archive.ArchiveData(new MockedApplicationPaths()),
            ServiceLocator.GetService<OAuthStore>(), ServiceLocator.GetService<Logger<BangumiApi>>());
        var url = "https://api.bgm.tv/test/" + Guid.NewGuid();
        Assert.AreEqual("Title 1", (await api.Get<Model.Episode>(url, CancellationToken.None))!.Name);
        Assert.AreEqual("Title 1", (await api.Get<Model.Episode>(url, CancellationToken.None))!.Name);
        Assert.AreEqual(1, api.RequestCount);
        var path = Guid.NewGuid().ToString();
        BangumiApi.RequestFreshMetadata(path);
        using (BangumiApi.BeginRequestedRefresh(path))
            Assert.AreEqual("Title 2", (await api.Get<Model.Episode>(url, CancellationToken.None))!.Name);
        Assert.IsFalse(BangumiApi.IsFreshMetadataRefresh);
        Assert.AreEqual("Title 2", (await api.Get<Model.Episode>(url, CancellationToken.None))!.Name);
        Assert.AreEqual(2, api.RequestCount);
    }

    [TestMethod]
    public async Task RequestedRefreshIgnoresOldArchiveMetadata()
    {
        // A separate root keeps this stale archive out of the other tests.
        var paths = new ArchivePaths();
        var root = Path.Join(paths.DataPath, "bangumi", "archive");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Join(root, "episode.jsonlines"), """
            {"id":987654321,"subject_id":1,"name":"Stale archive","sort":1,"type":0,"airdate":"2000-01-01"}
            """);
        var archive = new Bangumi.Archive.ArchiveData(paths);
        await archive.Episode.GenerateIndex(CancellationToken.None);
        var api = new ChangingApi(archive, ServiceLocator.GetService<OAuthStore>(), ServiceLocator.GetService<Logger<BangumiApi>>());
        Assert.AreEqual("Stale archive", (await api.GetEpisode(987654321, CancellationToken.None))!.Name);
        Assert.AreEqual(0, api.RequestCount);
        var path = Guid.NewGuid().ToString();
        BangumiApi.RequestFreshMetadata(path);
        using (BangumiApi.BeginRequestedRefresh(path))
            Assert.AreEqual("Title 1", (await api.GetEpisode(987654321, CancellationToken.None))!.Name);
        Assert.AreEqual(1, api.RequestCount);
    }

    [DataTestMethod]
    [DataRow(EpisodeParserType.Basic)]
    [DataRow(EpisodeParserType.AnitomySharp)]
    [DataRow(EpisodeParserType.Torrent)]
    public async Task JellyfinQueuedRefreshUsesSavedIdWithoutChangingGlobalSettings(EpisodeParserType parser)
    {
        var configuration = ServiceLocator.GetService<Bangumi.Plugin>().Configuration;
        var previousParser = configuration.EpisodeParser;
        var previousTrust = configuration.TrustExistedBangumiId;
        var directory = "queued-title-" + Guid.NewGuid() + "/OVA";
        var library = ServiceLocator.GetService<ILibraryManager>();
        FakePath.CreateSeason(library, directory);
        var path = FakePath.CreateFile(directory + "/Unparseable.mkv");
        try
        {
            configuration.EpisodeParser = parser;
            configuration.TrustExistedBangumiId = false;
            BangumiApi.RequestFreshMetadata(path);
            var result = await ServiceLocator.GetService<EpisodeProvider>().GetMetadata(new EpisodeInfo
            {
                Path = path,
                ProviderIds = new() { [Constants.ProviderName] = "1143188" },
            }, CancellationToken.None);
            Assert.AreEqual("トニカクカワイイ ～制服～", result.Item.Name);
            Assert.AreEqual(14, result.Item.IndexNumber);
            Assert.AreEqual("1143188", result.Item.ProviderIds[Constants.ProviderName]);
            Assert.IsFalse(configuration.TrustExistedBangumiId);
            Assert.IsFalse(BangumiApi.IsFreshMetadataRefresh);
            Assert.IsNull(BangumiApi.BeginRequestedRefresh(path));
        }
        finally
        {
            BangumiApi.CancelFreshMetadataRequest(path);
            configuration.EpisodeParser = previousParser;
            configuration.TrustExistedBangumiId = previousTrust;
        }
    }

    private sealed class ChangingApi(Bangumi.Archive.ArchiveData archive, OAuthStore store, Logger<BangumiApi> logger)
        : BangumiApi(archive, store, logger)
    {
        public int RequestCount { get; private set; }
        public override HttpClient GetHttpClient(bool allowAutoRedirect = true) => new(new ResponseHandler(() => ++RequestCount));
    }

    private sealed class ResponseHandler(Func<int> next) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":987654321,\"name\":\"Title " + next() + "\"}", Encoding.UTF8, "application/json"),
            });
    }

    private sealed class ArchivePaths : MockedApplicationPaths, IApplicationPaths
    {
        public new string DataPath { get; } = FakePath.Create("fresh-archive-" + Guid.NewGuid());
    }
}
