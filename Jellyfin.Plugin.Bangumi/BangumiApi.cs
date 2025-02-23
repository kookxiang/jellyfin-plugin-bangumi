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
using User = Jellyfin.Plugin.Bangumi.Model.User;
#if EMBY
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;
#endif

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi
{
    private const int PageSize = 50;
    private const int Offset = 20;

    private static string BaseUrl =>
        string.IsNullOrEmpty(Plugin.Instance?.Configuration?.BaseServerUrl)
            ? "https://api.bgm.tv"
            : Plugin.Instance!.Configuration.BaseServerUrl.TrimEnd('/');

    public Task<IEnumerable<Subject>> SearchSubject(string keyword, CancellationToken token)
    {
        return SearchSubject(keyword, SubjectType.Anime, token);
    }

    public async Task<IEnumerable<Subject>> SearchSubject(string keyword, SubjectType? type, CancellationToken token)
    {
        if (string.IsNullOrEmpty(keyword))
            return [];
        try
        {
            if (Plugin.Instance!.Configuration.UseTestingSearchApi)
            {
                var searchParams = new SearchParams { Keyword = keyword };
                if (type != null)
                    searchParams.Filter.Type = [type.Value];
                var searchResult = await Post<SearchResult<Subject>>($"{BaseUrl}/v0/search/subjects", new JsonContent(searchParams), token);
                return searchResult?.Data ?? [];
            }
            else
            {
                // remove `-` in keyword
                keyword = keyword.Replace(" -", " ");

                var url = $"{BaseUrl}/search/subject/{Uri.EscapeDataString(keyword)}?responseGroup=large";
                if (type != null)
                    url += $"&type={(int)type}";
                var searchResult = await Get<SearchResult<Subject>>(url, token);
                var list = searchResult?.List ?? [];

                if (Plugin.Instance.Configuration.SortByFuzzScore && list.Count() > 2)
                {
                    // 仅使用前 5 个条目获取别名并排序
                    var num = 5;
                    var tasks = list.Take(num).Select(subject => GetSubject(subject.Id, token));
                    var subjectWithInfobox = await Task.WhenAll(tasks);

                    var sortedSubjects =
                        Subject.SortByFuzzScore(subjectWithInfobox.Where(s => s != null).Cast<Subject>().ToList(), keyword);
                    return sortedSubjects.Concat(list.Skip(num)).ToList();
                }

                return Subject.SortBySimilarity(list, keyword);
            }
        }
        catch (JsonException)
        {
            // 404 Not Found Anime
            return [];
        }
    }

    public async Task<Subject?> GetSubject(int id, CancellationToken token)
    {
        if (id <= 0) return null;
#if !EMBY
        var subject = await archive.Subject.FindById(id);
        if (subject != null)
            return subject.ToSubject();
#endif
        return await Get<Subject>($"{BaseUrl}/v0/subjects/{id}", token);
    }

    public Task<string?> GetSubjectImage(int id, CancellationToken token)
    {
        return GetSubjectImage(id, "large", token);
    }

    public async Task<string?> GetSubjectImage(int id, string type, CancellationToken token)
    {
        var imageUrl = await FollowRedirection($"{BaseUrl}/v0/subjects/{id}/image?type={type}", token);
        return imageUrl == "https://lain.bgm.tv/img/no_icon_subject.png" ? null : imageUrl;
    }

    public async Task<IEnumerable<Episode>?> GetSubjectEpisodeList(int id, EpisodeType? type, double episodeNumber, CancellationToken token)
    {
        if (id <= 0) return null;
#if !EMBY
        var episodeList = (await archive.SubjectEpisodeRelation.GetEpisodes(id))
            .Where(x => x.Type == type || type == null)
            .Select(x => x.ToEpisode());
        if (episodeList.Any()) return episodeList;
#endif

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

        if (result.Data.Any(x => (int)x.Order == (int)episodeNumber))
            return result.Data;

        var filteredEpisodeList = result.Data.Where(x => x.Type == (type ?? EpisodeType.Normal));
        if (!filteredEpisodeList.Any())
            filteredEpisodeList = result.Data;

        if (filteredEpisodeList.Min(x => x.Order) > episodeNumber)
            offset -= PageSize;
        else
            offset += PageSize;

        goto RequestEpisodeList;
    }

    public async Task<DataList<Episode>?> GetSubjectEpisodeListWithOffset(int id, EpisodeType? type, double offset, CancellationToken token)
    {
        if (id <= 0) return null;
        var url = $"{BaseUrl}/v0/episodes?subject_id={id}&limit={PageSize}";
        if (type != null)
            url += $"&type={(int)type}";
        if (offset > 0)
            url += $"&offset={offset}";
        return await Get<DataList<Episode>>(url, token);
    }

    public async Task<IEnumerable<RelatedSubject>?> GetSubjectRelations(int id, CancellationToken token)
    {
        if (id <= 0) return null;
#if !EMBY
        var relations = await archive.SubjectRelations.Get(id);
        if (relations.Any())
            return relations;
#endif
        return await Get<IEnumerable<RelatedSubject>>($"{BaseUrl}/v0/subjects/{id}/subjects", token);
    }

    public async Task<Subject?> SearchNextSubject(int id, CancellationToken token)
    {
        if (id <= 0) return null;

        bool SeriesSequelUnqualified(Subject subject)
        {
            return subject.Platform == SubjectPlatform.Movie
                   || subject.Platform == SubjectPlatform.OVA
                   || subject.GenreTags.Contains("OVA")
                   || subject.GenreTags.Contains("剧场版");
        }

        var requestCount = 0;
        //What would happen in Emby if I use `_plugin`?
        var maxRequestCount = Plugin.Instance?.Configuration?.SeasonGuessMaxSearchCount ?? 2;
        var relatedSubjects = await GetSubjectRelations(id, token);
        var subjectsQueue = new Queue<RelatedSubject>(relatedSubjects?.Where(item => item.Relation == SubjectRelation.Sequel) ?? []);
        while (subjectsQueue.Count > 0 && requestCount < maxRequestCount)
        {
            var relatedSubject = subjectsQueue.Dequeue();
            var subjectCandidate = await GetSubject(relatedSubject.Id, token);
            requestCount++;
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
                Console.WriteLine($"BangumiApi: Season guess of id #{id} end with {requestCount} searches");
                return subjectCandidate;
            }
        }

        Console.WriteLine($"BangumiApi: Season guess of id #{id} failed with {requestCount} searches");
        return null;
    }

    public async Task<IEnumerable<PersonInfo>> GetSubjectCharacters(int id, CancellationToken token)
    {
        if (id <= 0) return [];

        var characters = await Get<IEnumerable<RelatedCharacter>>($"{BaseUrl}/v0/subjects/{id}/characters", token);

        return characters?
            .OrderBy(c => c.Relation switch
            {
                "主角" => 0,
                "配角" => 1,
                "客串" => 2,
                _ => 3
            })
            .SelectMany(character => character.ToPersonInfos()) ?? [];
    }

    public async Task<IEnumerable<RelatedPerson>?> GetSubjectPersons(int id, CancellationToken token)
    {
        if (id <= 0) return null;
#if !EMBY
        var relatedPerson = await archive.SubjectPersonRelation.Get(id);
        if (relatedPerson.Any())
            return relatedPerson;
#endif
        return await Get<IEnumerable<RelatedPerson>>($"{BaseUrl}/v0/subjects/{id}/persons", token);
    }

    public async Task<IEnumerable<PersonInfo>> GetSubjectPersonInfos(int id, CancellationToken token)
    {
        if (id <= 0) return [];
        var persons = await GetSubjectPersons(id, token);
        return (persons ?? []).Select(person => person.ToPersonInfo()).Where(info => info != null)!;
    }

    public async Task<Episode?> GetEpisode(int id, CancellationToken token)
    {
        if (id <= 0) return null;
#if !EMBY
        var episode = await archive.Episode.FindById(id);
        if (episode != null && DateTime.TryParse(episode.AirDate, out var airDate))
            if (_plugin.Configuration.DaysBeforeUsingArchiveData == 0 ||
                airDate < DateTime.Now.Subtract(TimeSpan.FromDays(_plugin.Configuration.DaysBeforeUsingArchiveData)))
                return episode.ToEpisode();
#endif
        return await Get<Episode>($"{BaseUrl}/v0/episodes/{id}", token);
    }

    public async Task<PersonDetail?> GetPerson(int id, CancellationToken token)
    {
        if (id <= 0) return null;
#if !EMBY
        var person = await archive.Person.FindById(id);
        if (person != null)
            return person.ToPersonDetail();
#endif
        return await Get<PersonDetail>($"{BaseUrl}/v0/persons/{id}", token);
    }

    public Task<string?> GetPersonImage(int id, CancellationToken token)
    {
        return GetPersonImage(id, "large", token);
    }

    public async Task<string?> GetPersonImage(int id, string type, CancellationToken token)
    {
        var person = await Get<PersonDetail>($"{BaseUrl}/v0/persons/{id}", token);
        return person?.DefaultImage;
    }

    public async Task<User?> GetAccountInfo(string accessToken, CancellationToken token)
    {
        return await Get<User>($"{BaseUrl}/v0/me", accessToken, token);
    }

    public async Task<DataList<EpisodeCollectionInfo>?> GetEpisodeCollectionInfo(string accessToken, int subjectId, int episodeType, CancellationToken token)
    {
        return await Get<DataList<EpisodeCollectionInfo>>($"{BaseUrl}/v0/users/-/collections/{subjectId}/episodes?episode_type={episodeType}", accessToken, token, false);
    }

    public async Task UpdateCollectionStatus(string accessToken, int subjectId, CollectionType type, CancellationToken token)
    {
        await Post($"{BaseUrl}/v0/users/-/collections/{subjectId}", new JsonContent(new CollectionStatus { Type = type }), accessToken, token);
    }

    public async Task<EpisodeCollectionInfo?> GetEpisodeStatus(string accessToken, int episodeId, CancellationToken token)
    {
        return await Get<EpisodeCollectionInfo>($"{BaseUrl}/v0/users/-/collections/-/episodes/{episodeId}", accessToken, token);
    }

    public async Task UpdateEpisodeStatus(string accessToken, int episodeId, EpisodeCollectionType status, CancellationToken token)
    {
#if EMBY
        var options = new HttpRequestOptions
        {
            Url = $"{BaseUrl}/v0/users/-/collections/-/episodes/{episodeId}",
            RequestHttpContent = new JsonContent(new EpisodeCollectionInfo { Type = status }),
            RequestHeaders = { { "Authorization", "Bearer " + accessToken } },
            ThrowOnErrorResponse = false
        };
        await Send("PUT", options, token);
#else
        var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/v0/users/-/collections/-/episodes/{episodeId}");
        request.Content = new JsonContent(new EpisodeCollectionInfo { Type = status });
        await Send(request, accessToken, token);
#endif
    }
}
