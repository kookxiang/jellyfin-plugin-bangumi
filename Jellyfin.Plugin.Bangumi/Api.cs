using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Bangumi
{
    public static class Api
    {
        private static readonly JsonSerializerOptions DefaultJsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static async Task<List<Subject>> SearchSubject(string keyword, CancellationToken token)
        {
            var jsonString = await SendRequest($"https://api.bgm.tv/search/subject/{Uri.EscapeUriString(keyword)}?type=2", token);
            try
            {
                var searchResult = JsonSerializer.Deserialize<SearchResult<Subject>>(jsonString, DefaultJsonSerializerOptions);
                return searchResult?.List ?? new List<Subject>();
            }
            catch
            {
                return new List<Subject>();
            }
        }

        public static async Task<Subject?> GetSubject(string id, CancellationToken token)
        {
            var jsonString = await SendRequest($"https://api.bgm.tv/v0/subjects/{id}", token);
            return JsonSerializer.Deserialize<Subject>(jsonString, DefaultJsonSerializerOptions);
        }

        public static async Task<DataList<Episode>?> GetSubjectEpisodeList(string seriesId, CancellationToken token)
        {
            var jsonString = await SendRequest($"https://api.bgm.tv/v0/episodes?subject_id={seriesId}", token);
            return JsonSerializer.Deserialize<DataList<Episode>>(jsonString, DefaultJsonSerializerOptions);
        }

        public static async Task<List<PersonInfo>> GetSubjectCharacters(string seriesId, CancellationToken token)
        {
            var result = new List<PersonInfo>();
            var jsonString = await SendRequest($"https://api.bgm.tv/v0/subjects/{seriesId}/characters", token);
            var characters = JsonSerializer.Deserialize<List<RelatedCharacter>?>(jsonString, DefaultJsonSerializerOptions);
            characters?.ForEach(character =>
            {
                result.Add(new PersonInfo
                {
                    Name = "",
                    Role = character.Name,
                    ImageUrl = string.IsNullOrEmpty(character.DefaultImage) ? null : character.DefaultImage,
                    Type = PersonType.Actor,
                    ProviderIds = new Dictionary<string, string>()
                });
            });
            return result;
        }

        [Obsolete("use GetSubjectCharacters when new api available")]
        public static async Task<List<PersonInfo>> GetSubjectCharactersLegacy(string seriesId, CancellationToken token)
        {
            var result = new List<PersonInfo>();
            var jsonString = await SendRequest($"https://api.bgm.tv/subject/{seriesId}?responseGroup=medium", token);
            var subject = JsonSerializer.Deserialize<Legacy.SubjectMedium?>(jsonString, DefaultJsonSerializerOptions);
            subject?.Characters?.ForEach(character =>
            {
                if (character.Actors.Count == 0)
                    return;
                var actor = character.Actors[0];
                result.Add(new PersonInfo
                {
                    Name = actor.Name,
                    Role = character.Name,
                    ImageUrl = string.IsNullOrEmpty(actor.DefaultImage) ? null : actor.DefaultImage,
                    Type = PersonType.Actor,
                    ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, $"{actor.Id}" } }
                });
            });
            return result;
        }

        public static async Task<List<PersonInfo>> GetSubjectPeople(string seriesId, CancellationToken token)
        {
            var result = new List<PersonInfo>();
            var jsonString = await SendRequest($"https://api.bgm.tv/v0/subjects/{seriesId}/persons", token);
            var persons = JsonSerializer.Deserialize<List<RelatedPerson>>(jsonString, DefaultJsonSerializerOptions);
            persons?.ForEach(person =>
            {
                var item = new PersonInfo
                {
                    Name = person.Name,
                    ImageUrl = person.DefaultImage,
                    Type = person.Relation switch
                    {
                        "导演" => PersonType.Director,
                        "制片人" => PersonType.Producer,
                        "系列构成" => PersonType.Composer,
                        "脚本" => PersonType.Writer,
                        "演出" => PersonType.Actor,
                        _ => ""
                    },
                    ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, $"{person.Id}" } }
                };
                if (!string.IsNullOrEmpty(item.Type))
                    result.Add(item);
            });
            return result;
        }

        public static async Task<Episode?> GetEpisode(string episodeId, CancellationToken token)
        {
            var jsonString = await SendRequest($"https://api.bgm.tv/v0/episodes/{episodeId}", token);
            return JsonSerializer.Deserialize<Episode>(jsonString, DefaultJsonSerializerOptions);
        }

        public static async Task<PersonDetail?> GetPerson(string personId, CancellationToken token)
        {
            var jsonString = await SendRequest($"https://api.bgm.tv/v0/persons/{personId}", token);
            return JsonSerializer.Deserialize<PersonDetail>(jsonString, DefaultJsonSerializerOptions);
        }

        private static async Task<string> SendRequest(string url, CancellationToken token)
        {
            var httpClient = Plugin.Instance!.GetHttpClient();
            var response = await httpClient.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(token);
        }
    }
}