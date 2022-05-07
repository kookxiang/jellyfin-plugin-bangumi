using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities;
using JellyfinPersonType = MediaBrowser.Model.Entities.PersonType;

namespace Jellyfin.Plugin.Bangumi
{
    public class BangumiApi
    {
        private readonly JsonSerializerOptions _options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly Plugin _plugin;

        public BangumiApi(Plugin plugin)
        {
            _plugin = plugin;
        }

        public async Task<List<Subject>> SearchSubject(string keyword, CancellationToken token)
        {
            var jsonString = await SendRequest($"https://api.bgm.tv/search/subject/{Uri.EscapeDataString(keyword)}?type=2", token);
            var searchResult = JsonSerializer.Deserialize<SearchResult<Subject>>(jsonString, _options);
            return searchResult?.List ?? new List<Subject>();
        }

        public async Task<Subject?> GetSubject(string id, CancellationToken token)
        {
            var jsonString = await SendRequest($"https://api.bgm.tv/v0/subjects/{id}", token);
            return JsonSerializer.Deserialize<Subject>(jsonString, _options);
        }

        public async Task<DataList<Episode>?> GetSubjectEpisodeList(string seriesId, CancellationToken token)
        {
            var jsonString = await SendRequest($"https://api.bgm.tv/v0/episodes?subject_id={seriesId}", token);
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
            return await SendRequest(url, null, token);
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
}