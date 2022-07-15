using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Controller.Entities;
using JellyfinPersonType = MediaBrowser.Model.Entities.PersonType;

namespace Jellyfin.Plugin.Bangumi;

public class BangumiApi
{
    private const int PageSize = 50;
    private const int Offset = 20;

    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Plugin _plugin;
    private readonly OAuthStore _store;

    public BangumiApi(Plugin plugin, OAuthStore store)
    {
        _plugin = plugin;
        _store = store;
    }

    public async Task<List<Subject>> SearchSubject(string keyword, CancellationToken token)
    {
        var jsonString = await SendRequest($"https://api.bgm.tv/search/subject/{Uri.EscapeDataString(keyword)}?type=2", token);
        var searchResult = JsonSerializer.Deserialize<SearchResult<Subject>>(jsonString, _options);
        var list = searchResult?.List ?? new List<Subject>();
        return Subject.SortBySimilarity(list, keyword);
    }

    public async Task<Subject?> GetSubject(int id, CancellationToken token)
    {
        return await GetSubject(id.ToString(), token);
    }

    public async Task<Subject?> GetSubject(string id, CancellationToken token)
    {
        var jsonString = await SendRequest($"https://api.bgm.tv/v0/subjects/{id}", token);
        return JsonSerializer.Deserialize<Subject>(jsonString, _options);
    }

    public async Task<List<Episode>?> GetSubjectEpisodeList(string seriesId, int episodeNumber, CancellationToken token)
    {
        return await GetSubjectEpisodeList(seriesId, EpisodeType.Normal, episodeNumber, token);
    }

    public async Task<List<Episode>?> GetSubjectEpisodeList(string seriesId, EpisodeType? type, double episodeNumber, CancellationToken token)
    {
        var result = await GetSubjectEpisodeListWithOffset(seriesId, type, 0, token);
        if (result == null)
            return null;
        if (episodeNumber < PageSize && episodeNumber < result.Total)
            return result.Data;

        // guess offset number
        var offset = Math.Min((int)episodeNumber, result.Total) - Offset;

        var initialResult = result;
        var history = new HashSet<int>();

        RequestEpisodeList:
        if (offset < 0)
            return result.Data;
        if (history.Contains(offset))
            return result.Data;
        history.Add(offset);

        try
        {
            result = await GetSubjectEpisodeListWithOffset(seriesId, type, offset, token);
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

        if (result.Data.First().Order > episodeNumber)
        {
            offset -= PageSize;
            goto RequestEpisodeList;
        }

        if (result.Data.Last().Order < episodeNumber)
        {
            offset += PageSize;
            goto RequestEpisodeList;
        }

        return result.Data;
    }

    public async Task<DataList<Episode>?> GetSubjectEpisodeListWithOffset(string seriesId, EpisodeType? type, double offset, CancellationToken token)
    {
        var url = $"https://api.bgm.tv/v0/episodes?subject_id={seriesId}&limit={PageSize}";
        if (type != null)
            url += $"&type={(int)type}";
        if (offset > 0)
            url += $"&offset={offset}";
        var jsonString = await SendRequest(url, token);
        return JsonSerializer.Deserialize<DataList<Episode>>(jsonString, _options);
    }

    public async Task<List<PersonInfo>> GetSubjectCharacters(string seriesId, CancellationToken token)
    {
        var result = new List<PersonInfo>();
        var jsonString = await SendRequest($"https://api.bgm.tv/v0/subjects/{seriesId}/characters", token);
        var characters = JsonSerializer.Deserialize<List<RelatedCharacter>?>(jsonString, _options);
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

    public async Task<List<PersonInfo>> GetSubjectPeople(string seriesId, CancellationToken token)
    {
        var result = new List<PersonInfo>();
        var jsonString = await SendRequest($"https://api.bgm.tv/v0/subjects/{seriesId}/persons", token);
        var persons = JsonSerializer.Deserialize<List<RelatedPerson>>(jsonString, _options);
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

    public async Task<Episode?> GetEpisode(string episodeId, CancellationToken token)
    {
        var jsonString = await SendRequest($"https://api.bgm.tv/v0/episodes/{episodeId}", token);
        return JsonSerializer.Deserialize<Episode>(jsonString, _options);
    }

    public async Task<PersonDetail?> GetPerson(string personId, CancellationToken token)
    {
        var jsonString = await SendRequest($"https://api.bgm.tv/v0/persons/{personId}", token);
        return JsonSerializer.Deserialize<PersonDetail>(jsonString, _options);
    }

    public async Task<User?> GetAccountInfo(string accessToken, CancellationToken token)
    {
        var jsonString = await SendRequest("https://api.bgm.tv/v0/me", accessToken, token);
        return JsonSerializer.Deserialize<User>(jsonString, _options);
    }

    public async Task UpdateEpisodeStatus(string accessToken, string episodeId, EpisodeStatus status, CancellationToken token)
    {
        await SendRequest($"https://api.bgm.tv/ep/{episodeId}/status/{status.GetValue()}", accessToken, token);
    }

    private async Task<string> SendRequest(string url, CancellationToken token)
    {
        return await SendRequest(url, _store.GetAvailable()?.AccessToken, token);
    }

    private async Task<string> SendRequest(string url, string? accessToken, CancellationToken token)
    {
        var httpClient = _plugin.GetHttpClient();
        if (!string.IsNullOrEmpty(accessToken))
            httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse("Bearer " + accessToken);
        var response = await httpClient.GetAsync(url, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(token);
    }
}