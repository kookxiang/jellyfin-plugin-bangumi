using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Jellyfin.Plugin.Bangumi.API;

namespace Jellyfin.Plugin.Bangumi.Providers
{
    public static class Api
    {
        private static readonly JsonSerializerOptions DefaultJsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static async Task<List<SubjectBase>?> SearchSeries(string keyword, CancellationToken token)
        {
            var jsonString = await SendRequest($"https://api.bgm.tv/search/subject/{HttpUtility.UrlEncode(keyword)}?type=2", token);
            var searchResult = JsonSerializer.Deserialize<SearchResult<SubjectBase>>(jsonString, DefaultJsonSerializerOptions);
            return searchResult?.List;
        }

        public static async Task<SubjectBase?> GetSeriesBasic(string id, CancellationToken token)
        {
            var jsonString = await SendRequest($"https://api.bgm.tv/subject/{id}", token);
            return JsonSerializer.Deserialize<SubjectBase>(jsonString, DefaultJsonSerializerOptions);
        }

        public static async Task<SubjectMedium?> GetSeriesDetail(string id, CancellationToken token)
        {
            var jsonString = await SendRequest($"https://api.bgm.tv/subject/{id}?responseGroup=medium", token);
            return JsonSerializer.Deserialize<SubjectMedium>(jsonString, DefaultJsonSerializerOptions);
        }

        private static Task<string> SendRequest(string url, CancellationToken cancellationToken)
        {
            var httpClient = Plugin.Instance.GetHttpClient();
            return httpClient.GetStringAsync(url, cancellationToken);
        }

        public static async Task<Episode?> GetEpisode(string episodeId, CancellationToken token)
        {
            var jsonString = await SendRequest($"https://api.bgm.tv/v0/episodes/{episodeId}", token);
            return JsonSerializer.Deserialize<Episode>(jsonString, DefaultJsonSerializerOptions);
        }

        public static async Task<EpisodeList?> GetEpisodeList(string seriesId, CancellationToken token)
        {
            var jsonString = await SendRequest($"https://api.bgm.tv/subject/{seriesId}/ep", token);
            return JsonSerializer.Deserialize<EpisodeList>(jsonString, DefaultJsonSerializerOptions);
        }
    }
}