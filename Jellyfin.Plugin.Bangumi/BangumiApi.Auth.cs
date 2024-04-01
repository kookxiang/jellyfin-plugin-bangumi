using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;

#if EMBY
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;
#endif

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi
{
    public async Task<User?> GetAccountInfo(string accessToken, CancellationToken token)
    {
        return await SendRequest<User>("https://api.bgm.tv/v0/me", accessToken, token);
    }

    public async Task<DataList<EpisodeCollectionInfo>?> GetEpisodeCollectionInfo(string accessToken, int subjectId, int episodeType, CancellationToken token)
    {
        return await SendRequest<DataList<EpisodeCollectionInfo>>($"https://api.bgm.tv/v0/users/-/collections/{subjectId}/episodes?episode_type={episodeType}", accessToken, token);
    }

    public async Task UpdateCollectionStatus(string accessToken, int subjectId, CollectionType type, CancellationToken token)
    {
#if EMBY
        var options = new HttpRequestOptions
        {
            Url = $"https://api.bgm.tv/v0/users/-/collections/{subjectId}",
            RequestHttpContent = new JsonContent(new Collection { Type = type }),
            RequestHeaders = {
                { "Authorization", "Bearer " + accessToken }
            },
            ThrowOnErrorResponse = false,
        };
        await SendRequest("POST", options);
#else
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.bgm.tv/v0/users/-/collections/{subjectId}");
        request.Content = new JsonContent(new Collection { Type = type });
        await SendRequest(request, accessToken, token);
#endif
    }

    public async Task<EpisodeCollectionInfo?> GetEpisodeStatus(string accessToken, int episodeId, CancellationToken token)
    {
        return await SendRequest<EpisodeCollectionInfo>($"https://api.bgm.tv/v0/users/-/collections/-/episodes/{episodeId}", accessToken, token);
    }

    public async Task UpdateEpisodeStatus(string accessToken, int subjectId, int episodeId, EpisodeCollectionType status, CancellationToken token)
    {
#if EMBY
        var options = new HttpRequestOptions
        {
            Url = $"https://api.bgm.tv/v0/users/-/collections/-/episodes/{episodeId}",
            RequestHttpContent = new JsonContent(new EpisodeCollectionInfo
            {
                Type = status
            }),
            RequestHeaders = {
                { "Authorization", "Bearer " + accessToken }
            },
            ThrowOnErrorResponse = false,
        };
        await SendRequest("PUT", options);
#else
        var request = new HttpRequestMessage(HttpMethod.Put, $"https://api.bgm.tv/v0/users/-/collections/-/episodes/{episodeId}");
        request.Content = new JsonContent(new EpisodeCollectionInfo
        {
            Type = status
        });
        await SendRequest(request, accessToken, token);
#endif
    }
}