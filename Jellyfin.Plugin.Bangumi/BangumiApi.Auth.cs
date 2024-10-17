#if !EMBY
using System.Net.Http;
#else
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;
#endif
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi
{
    public async Task<User?> GetAccountInfo(string accessToken, CancellationToken token)
    {
        return await Get<User>($"{BaseUrl}/v0/me", accessToken, token);
    }

    public async Task<DataList<EpisodeCollectionInfo>?> GetEpisodeCollectionInfo(string accessToken, int subjectId, int episodeType, CancellationToken token)
    {
        return await Get<DataList<EpisodeCollectionInfo>>($"{BaseUrl}/v0/users/-/collections/{subjectId}/episodes?episode_type={episodeType}", accessToken, token);
    }

    public async Task UpdateCollectionStatus(string accessToken, int subjectId, CollectionType type, CancellationToken token)
    {
        await Post($"{BaseUrl}/v0/users/-/collections/{subjectId}", new JsonContent(new Collection { Type = type }), accessToken, token);
    }

    public async Task<EpisodeCollectionInfo?> GetEpisodeStatus(string accessToken, int episodeId, CancellationToken token)
    {
        return await Get<EpisodeCollectionInfo>($"{BaseUrl}/v0/users/-/collections/-/episodes/{episodeId}", accessToken, token);
    }

    public async Task UpdateEpisodeStatus(string accessToken, int subjectId, int episodeId, EpisodeCollectionType status, CancellationToken token)
    {
#if EMBY
        var options = new HttpRequestOptions
        {
            Url = $"{BaseUrl}/v0/users/-/collections/-/episodes/{episodeId}",
            RequestHttpContent = new JsonContent(new EpisodeCollectionInfo
            {
                Type = status
            }),
            RequestHeaders =
            {
                { "Authorization", "Bearer " + accessToken }
            },
            ThrowOnErrorResponse = false,
        };
        await Send("PUT", options);
#else
        var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/v0/users/-/collections/-/episodes/{episodeId}");
        request.Content = new JsonContent(new EpisodeCollectionInfo
        {
            Type = status
        });
        await Send(request, accessToken, token);
#endif
    }
}