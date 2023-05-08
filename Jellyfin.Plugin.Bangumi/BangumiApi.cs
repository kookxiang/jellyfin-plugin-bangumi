using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using JellyfinPersonType = MediaBrowser.Model.Entities.PersonType;

namespace Jellyfin.Plugin.Bangumi;

public class BangumiApi
{
    private const int PageSize = 50;
    private const int Offset = 20;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OAuthStore _store;

    public BangumiApi(IHttpClientFactory httpClientFactory, OAuthStore store)
    {
        _httpClientFactory = httpClientFactory;
        _store = store;
    }

    private static Plugin Plugin => Plugin.Instance!;

    public Task<List<Subject>> SearchSubject(string keyword, CancellationToken token)
    {
        return SearchSubject(keyword, SubjectType.Anime, token);
    }

    public async Task<List<Subject>> SearchSubject(string keyword, SubjectType? type, CancellationToken token)
    {
        var url = $"https://api.bgm.tv/search/subject/{Uri.EscapeDataString(keyword)}?responseGroup=large";
        if (type != null)
            url += $"&type={(int)type}";
        try
        {
            var searchResult = await SendRequest<SearchResult<Subject>>(url, token);
            var list = searchResult?.List ?? new List<Subject>();
            return Subject.SortBySimilarity(list, keyword);
        }
        catch (JsonException)
        {
            // 404 Not Found Anime
            return new List<Subject>();
        }
    }

    public async Task<Subject?> GetSubject(int id, CancellationToken token)
    {
        return await SendRequest<Subject>($"https://api.bgm.tv/v0/subjects/{id}", token);
    }

    public async Task<List<Episode>?> GetSubjectEpisodeList(int id, EpisodeType? type, double episodeNumber, CancellationToken token)
    {
        var result = await GetSubjectEpisodeListWithOffset(id, type, 0, token);
        if (result == null)
            return null;
        if (episodeNumber < PageSize && episodeNumber < result.Total)
            return result.Data;
        if (episodeNumber > PageSize && episodeNumber > result.Total)
            return result.Data;

        // guess offset number
        var offset = Math.Min((int)episodeNumber, result.Total) - Offset;

        var initialResult = result;
        var history = new HashSet<int>();

        RequestEpisodeList:
        if (offset < 0)
            return result.Data;
        if (offset > result.Total)
            return result.Data;
        if (history.Contains(offset))
            return result.Data;
        history.Add(offset);

        try
        {
            result = await GetSubjectEpisodeListWithOffset(id, type, offset, token);
            if (result == null)
                return initialResult.Data;
        }
        catch (HttpRequestException e)
        {
            // bad request: offset is out of range
            if (e.StatusCode == HttpStatusCode.BadRequest)
                return initialResult.Data;
            throw;
        }

        if (result.Data.Exists(x => (int)x.Order == (int)episodeNumber))
            return result.Data;

        var filteredEpisodeList = result.Data.Where(x => x.Type == (type ?? EpisodeType.Normal)).ToList();
        if (filteredEpisodeList.Count == 0)
            filteredEpisodeList = result.Data;

        if (filteredEpisodeList.Min(x => x.Order) > episodeNumber)
            offset -= PageSize;
        else
            offset += PageSize;

        goto RequestEpisodeList;
    }

    public async Task<DataList<Episode>?> GetSubjectEpisodeListWithOffset(int id, EpisodeType? type, double offset, CancellationToken token)
    {
        var url = $"https://api.bgm.tv/v0/episodes?subject_id={id}&limit={PageSize}";
        if (type != null)
            url += $"&type={(int)type}";
        if (offset > 0)
            url += $"&offset={offset}";
        return await SendRequest<DataList<Episode>>(url, token);
    }

    public async Task<List<PersonInfo>> GetSubjectCharacters(int id, CancellationToken token)
    {
        var result = new List<PersonInfo>();
        var characters = await SendRequest<List<RelatedCharacter>>($"https://api.bgm.tv/v0/subjects/{id}/characters", token);
        characters?.ForEach(character =>
        {
            if (character.Actors == null)
                return;
            result.AddRange(character.Actors.Select(actor => new PersonInfo
            {
                Name = actor.Name,
                Role = character.Name,
                ImageUrl = actor.DefaultImage,
                Type = JellyfinPersonType.Actor,
                ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, $"{actor.Id}" } }
            }));
        });
        return result;
    }

    public async Task<List<RelatedPerson>?> GetSubjectPersons(int id, CancellationToken token)
    {
        return await SendRequest<List<RelatedPerson>>($"https://api.bgm.tv/v0/subjects/{id}/persons", token);
    }

    public async Task<List<PersonInfo>> GetSubjectPersonInfos(int id, CancellationToken token)
    {
        var result = new List<PersonInfo>();
        var persons = await GetSubjectPersons(id, token);
        persons?.ForEach(person =>
        {
            var item = new PersonInfo
            {
                Name = person.Name,
                ImageUrl = person.DefaultImage,
                Type = person.Relation switch
                {
                    "导演" => JellyfinPersonType.Director,
                    "制片人" => JellyfinPersonType.Producer,
                    "系列构成" => JellyfinPersonType.Composer,
                    "脚本" => JellyfinPersonType.Writer,
                    _ => ""
                },
                ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, $"{person.Id}" } }
            };
            if (!string.IsNullOrEmpty(item.Type))
                result.Add(item);
        });
        return result;
    }

    public async Task<Episode?> GetEpisode(int id, CancellationToken token)
    {
        return await SendRequest<Episode>($"https://api.bgm.tv/v0/episodes/{id}", token);
    }

    public async Task<PersonDetail?> GetPerson(int id, CancellationToken token)
    {
        return await SendRequest<PersonDetail>($"https://api.bgm.tv/v0/persons/{id}", token);
    }

    public async Task<User?> GetAccountInfo(string accessToken, CancellationToken token)
    {
        return await SendRequest<User>("https://api.bgm.tv/v0/me", accessToken, token);
    }

    public async Task UpdateCollectionStatus(string accessToken, int subjectId, CollectionType type, CancellationToken token)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"https://api.bgm.tv/v0/users/-/collections/{subjectId}");
        request.Content = new JsonContent(new Collection { Type = type });
        await SendRequest(request, accessToken, token);
    }

    public async Task<EpisodeCollectionInfo?> GetEpisodeStatus(string accessToken, int episodeId, CancellationToken token)
    {
        return await SendRequest<EpisodeCollectionInfo>($"https://api.bgm.tv/v0/users/-/collections/-/episodes/{episodeId}", accessToken, token);
    }
    
    public async Task UpdateEpisodeStatus(string accessToken, int subjectId, int episodeId, EpisodeCollectionType status, CancellationToken token)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"https://api.bgm.tv/v0/users/-/collections/-/episodes/{episodeId}");
        request.Content = new JsonContent(new EpisodeCollectionInfo
        {
            Type = status
        });
        await SendRequest(request, accessToken, token);
    }

    private Task<string> SendRequest(string url, string? accessToken, CancellationToken token)
    {
        return SendRequest(new HttpRequestMessage(HttpMethod.Get, url), accessToken, token);
    }

    private async Task<string> SendRequest(HttpRequestMessage request, string? accessToken, CancellationToken token)
    {
        var httpClient = GetHttpClient();
        if (!string.IsNullOrEmpty(accessToken))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse("Bearer " + accessToken);
        using var response = await httpClient.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) await ServerException.ThrowFrom(response);
        return await response.Content.ReadAsStringAsync(token);
    }

    private async Task<T?> SendRequest<T>(string url, CancellationToken token)
    {
        return await SendRequest<T>(url, _store.GetAvailable()?.AccessToken, token);
    }

    private async Task<T?> SendRequest<T>(string url, string? accessToken, CancellationToken token)
    {
        var jsonString = await SendRequest(url, accessToken, token);
        return JsonSerializer.Deserialize<T>(jsonString, Options);
    }

    public HttpClient GetHttpClient()
    {
        var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Jellyfin.Plugin.Bangumi", Plugin.Version.ToString()));
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(https://github.com/kookxiang/jellyfin-plugin-bangumi)"));
        httpClient.Timeout = TimeSpan.FromMilliseconds(Plugin.Configuration.RequestTimeout);
        return httpClient;
    }

    private class JsonContent : StringContent
    {
        public JsonContent(object obj) : base(JsonSerializer.Serialize(obj, Options), Encoding.UTF8, "application/json")
        {
            Headers.ContentType!.CharSet = null;
        }
    }
}