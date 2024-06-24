using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities;
#if EMBY
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;
#endif

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi
{
    private const int PageSize = 50;
    private const int Offset = 20;

    public Task<List<Subject>> SearchSubject(string keyword, CancellationToken token)
    {
        return SearchSubject(keyword, SubjectType.Anime, token);
    }

    public async Task<List<Subject>> SearchSubject(string keyword, SubjectType? type, CancellationToken token)
    {
        try
        {
            if (Plugin.Instance!.Configuration.UseTestingSearchApi)
            {
                var searchParams = new SearchParams { Keyword = keyword };
                if (type != null)
                    searchParams.Filter.Type = new[] { type.Value };
#if EMBY
                var options = new HttpRequestOptions
                {
                    Url = "https://api.bgm.tv/v0/search/subjects",
                    RequestHttpContent = new JsonContent(searchParams)
                };
                var jsonString = await SendRequest("POST", options);
#else
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.bgm.tv/v0/search/subjects");
                request.Content = new JsonContent(searchParams);
                var jsonString = await SendRequest(request, token);
#endif
                var searchResult = JsonSerializer.Deserialize<SearchResult<Subject>>(jsonString, Options);
                var list = searchResult?.Data ?? new List<Subject>();
                return Subject.SortBySimilarity(list, keyword);
            }
            else
            {
                var url = $"https://api.bgm.tv/search/subject/{Uri.EscapeDataString(keyword)}?responseGroup=large";
                if (type != null)
                    url += $"&type={(int)type}";
                var searchResult = await SendRequest<SearchResult<Subject>>(url, token);
                var list = searchResult?.List ?? new List<Subject>();
                return Subject.SortBySimilarity(list, keyword);
            }
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
        if (result.Total <= PageSize)
            return result.Data;
        if (episodeNumber <= result.Data.Max(episode => episode.Order) && episodeNumber >= result.Data.Min(episode => episode.Order))
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

    public async Task<List<RelatedSubject>?> GetSubjectRelations(int id, CancellationToken token)
    {
        return await SendRequest<List<RelatedSubject>>($"https://api.bgm.tv/v0/subjects/{id}/subjects", token);
    }

    public async Task<Subject?> SearchNextSubject(int id, CancellationToken token)
    {
        bool SeriesSequelUnqualified(Subject subject)
        {
            return subject?.Platform == SubjectPlatform.Movie || subject?.Platform == SubjectPlatform.OVA
                                                              || subject?.PopularTags.Contains("OVA") == true 
                                                              || subject?.PopularTags.Contains("剧场版") == true;
        }
        var relatedSubjects = await GetSubjectRelations(id, token);
        var subjectsQueue = new Queue<RelatedSubject>(relatedSubjects?.Where(item => item.Relation == SubjectRelation.Sequel) ?? []);
        while (subjectsQueue.Any())
        {
            var relatedSubject = subjectsQueue.Dequeue();
            var subjectCandidate = await GetSubject(relatedSubject.Id, token);
            if (subjectCandidate != null && SeriesSequelUnqualified(subjectCandidate)) 
            {
                var nextRelatedSubjects = await GetSubjectRelations(subjectCandidate.Id, token);
                foreach (var nextRelatedSubject in nextRelatedSubjects?.Where(item => item.Relation == SubjectRelation.Sequel) ?? [])
                {
                    subjectsQueue.Enqueue(nextRelatedSubject);
                }
            }
            else
            {
                // BFS until meets criteria
                return subjectCandidate;
            }
        }
        
        return null;
        // var relatedSubjects = await GetSubjectRelations(id, token);
        // var subjectInfo = relatedSubjects?.FirstOrDefault(item => item.Relation == SubjectRelation.Sequel);
        // if (subjectInfo == null)
        //     return null;
        // var subject = await GetSubject(subjectInfo.Id, token);
        // if (subject?.Platform == SubjectPlatform.Movie || subject?.Platform == SubjectPlatform.OVA
        //     || subject?.PopularTags.Contains("OVA") == true || subject?.PopularTags.Contains("剧场版") == true)
        //     subject = await SearchNextSubject(subject.Id, token);
        // return subject;
    }

    public async Task<List<PersonInfo>> GetSubjectCharacters(int id, CancellationToken token)
    {
        var result = new List<PersonInfo>();
        var characters = await SendRequest<List<RelatedCharacter>>($"https://api.bgm.tv/v0/subjects/{id}/characters", token);
        characters?.ForEach(character => result.AddRange(character.ToPersonInfos()));
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
        if (persons?.Count > 0)
            result.AddRange(persons.Select(person => person.ToPersonInfo()).Where(info => info != null)!);
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
}