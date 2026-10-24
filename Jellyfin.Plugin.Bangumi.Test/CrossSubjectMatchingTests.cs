using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.OAuth;
using Jellyfin.Plugin.Bangumi.Parser;
using Jellyfin.Plugin.Bangumi.Parser.TorrentParser;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class CrossSubjectMatchingTests
{
    [DataTestMethod]
    [DataRow(false, 1, false, true)]
    [DataRow(false, 3, false, false)]
    [DataRow(false, 3, true, true)]
    [DataRow(true, 1, false, false)]
    [DataRow(true, 1, true, true)]
    public async Task TorrentMatchingHonorsOptInInBothDirections(bool secondSubject, int index, bool enabled, bool matches)
    {
        var api = new SplitApi();
        var subjectId = secondSubject ? api.FirstId + 1 : api.FirstId;
        var configuration = new PluginConfiguration { AllowCrossSubjectEpisodeMatching = enabled };
        var context = new EpisodeParserContext(api, ServiceLocator.GetService<ILibraryManager>(),
            new EpisodeInfo { Path = "/Show/episode.mkv" }, ServiceLocator.GetService<IMediaSourceManager>(),
            configuration, new Model.LocalConfiguration(), CancellationToken.None);
        var result = await TorrentEpisodeParser.SearchEpisodes(context,
            ServiceLocator.GetService<Logger<TorrentEpisodeParser>>(), EpisodeType.Normal, subjectId, index);
        Assert.AreEqual(matches, result is not null);
        if (matches) Assert.AreEqual(index > 2 ? api.FirstId + 1 : api.FirstId, result!.ParentId);
        Assert.AreEqual(enabled && (secondSubject && index < 3 || !secondSubject && index > 2), api.RelationRequests > 0);
    }

    private sealed class SplitApi() : BangumiApi(new Bangumi.Archive.ArchiveData(new MockedApplicationPaths()),
        ServiceLocator.GetService<OAuthStore>(), ServiceLocator.GetService<Logger<BangumiApi>>())
    {
        private static int _nextId = 1810000000;
        public int FirstId { get; } = Interlocked.Add(ref _nextId, 10);
        public int RelationRequests { get; private set; }
        public override HttpClient GetHttpClient(bool allowAutoRedirect = true) => new(new Handler(this));

        private sealed class Handler(SplitApi api) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri!.PathAndQuery;
                string body;
                if (path.StartsWith("/v0/episodes?", StringComparison.Ordinal))
                {
                    var second = path.Contains($"subject_id={api.FirstId + 1}", StringComparison.Ordinal);
                    var subject = api.FirstId + (second ? 1 : 0);
                    var firstOrder = second ? 3 : 1;
                    body = $$"""{"total":2,"data":[{"id":{{subject + 2}},"subject_id":{{subject}},"sort":{{firstOrder}},"type":0},{"id":{{subject + 3}},"subject_id":{{subject}},"sort":{{firstOrder + 1}},"type":0}]}""";
                }
                else if (path.EndsWith("/subjects", StringComparison.Ordinal))
                {
                    api.RelationRequests++;
                    var second = path.Contains($"/{api.FirstId + 1}/", StringComparison.Ordinal);
                    var other = second ? api.FirstId : api.FirstId + 1;
                    var relation = second ? "前传" : "续集";
                    body = $$"""[{"id":{{other}},"type":2,"relation":"{{relation}}"}]""";
                }
                else
                {
                    var id = path.EndsWith((api.FirstId + 1).ToString(), StringComparison.Ordinal) ? api.FirstId + 1 : api.FirstId;
                    body = $$"""{"id":{{id}},"type":2,"name":"TV subject"}""";
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            }
        }
    }
}
