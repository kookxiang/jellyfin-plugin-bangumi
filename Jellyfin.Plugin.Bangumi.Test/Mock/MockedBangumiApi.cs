using System.Collections.Concurrent;
using System.Net.Http;
using Jellyfin.Plugin.Bangumi.OAuth;

namespace Jellyfin.Plugin.Bangumi.Test.Mock;

public sealed class MockedBangumiApi(Bangumi.Archive.ArchiveData archive, OAuthStore store, Logger<BangumiApi> logger)
    : BangumiApi(archive, store, logger)
{
    // Providers can catch API exceptions, so also check unmatched requests at assembly cleanup.
    internal static ConcurrentQueue<string> UnmatchedRequests { get; } = new();

    public override HttpClient GetHttpClient(bool allowAutoRedirect = true) =>
        new(new FixtureHttpMessageHandler(UnmatchedRequests.Enqueue));
}
