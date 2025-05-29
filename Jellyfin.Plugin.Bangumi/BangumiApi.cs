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

    public Task<IEnumerable<Subject>> SearchSubject(string keyword, CancellationToken token, int? seasonNumber = null)
    {
        return SearchSubject(keyword, SubjectType.Anime, token, seasonNumber);
    }

    public async Task<IEnumerable<Subject>> SearchSubject(string keyword, SubjectType? type, CancellationToken token, int? seasonNumber = null)
    {
        var result = await SearchSubjectSorted(keyword, type, token, seasonNumber);

        return result.Select(s => s.Item1);
    }

    public async Task<List<Subject>> SearchSubjectRaw(string keyword, SubjectType? type, CancellationToken token)
    {
        if (string.IsNullOrEmpty(keyword))
            return [];

        try
        {
            SearchResult<Subject>? searchResult;
            List<Subject> list;

            if (Plugin.Instance!.Configuration.UseTestingSearchApi)
            {
                var searchParams = new SearchParams { Keyword = keyword };
                if (type != null)
                    searchParams.Filter.Type = [type.Value];

                searchResult = await Post<SearchResult<Subject>>($"{BaseUrl}/v0/search/subjects", new JsonContent(searchParams), token);
                list = searchResult?.Data?.ToList() ?? [];
            }
            else
            {
                // remove `-` in keyword
                keyword = keyword.Replace(" -", " ");

                var url = $"{BaseUrl}/search/subject/{Uri.EscapeDataString(keyword)}?responseGroup=large";
                if (type != null)
                    url += $"&type={(int)type}";

                searchResult = await Get<SearchResult<Subject>>(url, token);
                list = searchResult?.List?.ToList() ?? [];
            }

            return list;
        }
        catch (JsonException)
        {
            // 404 Not Found Anime
            return [];
        }
    }

    public async Task<IEnumerable<(Subject, int)>> SortSubjects(IEnumerable<Subject> list, string keyword, CancellationToken token, int? seasonNumber = null)
    {
        Subject[] array = list?.ToArray() ?? [];

        // 仅使用前 5 个条目获取别名用于排序
        var num = Math.Min(array.Length, 5);

        List<Subject> subjectWithInfobox = [];
        for (int i = 0; i < num; i++)
        {
            var subject = array[i];
            // 一些条目能搜索到，但是获取详情时会报错
            try
            {
                var s = await GetSubject(subject.Id, token);
                if (s != null)
                {
                    subjectWithInfobox.Add(s);
                }
                else
                {
                    subjectWithInfobox.Add(subject);
                }
            }
            catch (Exception e)
            {
#if EMBY
                Console.WriteLine($"Failed to get subject {subject.Name}（{subject.Id}） alias info for sorting: {e.Message}");
#else
                logger.Error($"Failed to get subject {subject.Name}（{subject.Id}） alias info for sorting: {e.Message}");
#endif
                subjectWithInfobox.Add(subject);
            }
        }

        // 拼接剩余条目
        subjectWithInfobox.AddRange(array.Skip(num));

        return Subject.GetSortedScores(subjectWithInfobox, keyword, seasonNumber);
    }

    public async Task<IEnumerable<(Subject, int)>> SearchSubjectSorted(string keyword, SubjectType? type, CancellationToken token, int? seasonNumber = null)
    {
        var list = await SearchSubjectRaw(keyword, type, token);

        return await SortSubjects(list, keyword, token, seasonNumber);
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

    public async Task<IEnumerable<RelatedSubject>?> GetRelatedSubjects(int id, CancellationToken token)
    {
        if (id <= 0) return null;
#if !EMBY
        var relations = await archive.SubjectRelations.Get(id);
        if (relations.Any())
            return relations;
#endif
        return await Get<IEnumerable<RelatedSubject>>($"{BaseUrl}/v0/subjects/{id}/subjects", token);
    }

    public static bool IsOVAOrMovie(Subject subject)
    {
        return subject.Platform == SubjectPlatform.Movie
               || subject.Platform == SubjectPlatform.OVA
               || subject.GenreTags.Contains("OVA")
               || subject.GenreTags.Contains("剧场版");
    }

    public async Task<Subject?> SearchNextSubject(int id, CancellationToken token)
    {
        if (id <= 0) return null;

        var requestCount = 0;
        //What would happen in Emby if I use `_plugin`?
        var maxRequestCount = Plugin.Instance?.Configuration?.SeasonGuessMaxSearchCount ?? 2;
        var relatedSubjects = await GetRelatedSubjects(id, token);
        var subjectsQueue = new Queue<RelatedSubject>(relatedSubjects?.Where(item => item.Relation == SubjectRelation.Sequel) ?? []);
        while (subjectsQueue.Count > 0 && requestCount < maxRequestCount)
        {
            var relatedSubject = subjectsQueue.Dequeue();
            var subjectCandidate = await GetSubject(relatedSubject.Id, token);
            requestCount++;
            if (subjectCandidate != null && IsOVAOrMovie(subjectCandidate))
            {
                var nextRelatedSubjects = await GetRelatedSubjects(subjectCandidate.Id, token);
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

    /// <summary>
    /// 获取前传条目
    /// </summary>
    /// <param name="id">Bangumi条目id</param>
    /// <param name="maxRequestCount">最大查找层数</param>
    /// <param name="token"></param>
    /// <returns>
    /// 返回距离 <paramref name="id"/> 条目最多前 <paramref name="maxRequestCount"/> 季的条目直至第一季，
    /// <paramref name="id"/> 本身是第一季的话则返回自身，
    /// 如果 <paramref name="id"/> 条目不存在则返回null
    /// </returns>

    public async Task<Subject?> SearchPreviousSubject(int id, int maxRequestCount, CancellationToken token)
    {
        var subjects = await SearchPreviousSubjects(id, maxRequestCount, token, true);
        return subjects.Last().FirstOrDefault();
    }

    /// <summary>
    /// 获取前传条目
    /// </summary>
    /// <param name="id">Bangumi条目id</param>
    /// <param name="maxRequestCount">最大查找层数</param>
    /// <param name="token"></param>
    /// <param name="searchOneRelatedOnly">有多个前传时只查询其中一个</param>
    /// <returns>前传条目列表，第一个数组是 <paramref name="id"/> 的条目，之后每个数组是上一个数组的前传条目集合</returns>
    public async Task<List<Subject[]>> SearchPreviousSubjects(int id, int maxRequestCount, CancellationToken token, bool searchOneRelatedOnly = false)
    {
        if (id <= 0 || maxRequestCount <= 0) return [];

        // 获取当前 Subject
        var currentSubject = await GetSubject(id, token);
        if (currentSubject == null) return [];

        var result = new List<Subject[]> { new Subject[] { currentSubject } };

        for (int i = 0; i < maxRequestCount; i++)
        {
            Subject[] lastLoopSubjects = result.Last();

            List<Subject> currentLoopResult = [];
            foreach (var subject in lastLoopSubjects)
            {
                // 获取相关条目
                var relatedSubjects = await GetRelatedSubjects(subject.Id, token);
                if (relatedSubjects == null) continue;

                // 过滤出前传类型的条目
                var prequels = relatedSubjects.Where(item => item.Relation == SubjectRelation.Prequel).ToArray();
                if (prequels.Length == 0) continue;

                if (searchOneRelatedOnly)
                {
                    // 默认取最早创建的条目
                    prequels = prequels.OrderBy(item => item.Id).Take(1).ToArray();
                }

                // 转换为 Subject
                foreach (var item in prequels)
                {
                    if (currentLoopResult.Any(s => s.Id == item.Id)) continue;

                    var s = await GetSubject(item.Id, token);
                    if (s == null) continue;

                    currentLoopResult.Add(s);
                }
            }

            // 找不到更多前传条目，提前结束循环
            if (currentLoopResult.Count == 0) break;

            result.Add([.. currentLoopResult]);
        }

        return result;
    }

    /// <summary>
    /// 获取此条目的所有关联动画条目
    /// </summary>
    public async Task<List<int>> GetAllAnimeSeriesSubjectIds(int seriesId, CancellationToken token)
    {
        HashSet<int> allSubjectIds = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(seriesId);

        int requestCount = 0;
        int maxRequestCount = 1024; // 最多请求数


        while (queue.Count > 0 && requestCount < maxRequestCount)
        {
            var currentSeriesId = queue.Dequeue();
            // 将 id 添加进集合
            if (allSubjectIds.Add(currentSeriesId))
            {
                // 获取关联条目
                var results = await GetRelatedSubjects(currentSeriesId, token);
                if (results is null)
                    continue;

                // 遍历条目，判断关系，仅处理动画
                // 不同世界观、不同演绎……等视作单独系列，故未作判断
                foreach (var result in results.Where(r => r.Type == SubjectType.Anime))
                {
                    switch (result.Relation)
                    {
                        // 衍生、主线故事
                        case SubjectRelation.Sequel:
                        case SubjectRelation.Prequel:
                            queue.Enqueue(result.Id);
                            break;

                        // 无更多关联条目，直接添加进列表
                        case SubjectRelation.Extra:
                        case SubjectRelation.Summary:
                            allSubjectIds.Add(result.Id);
                            break;
                        default:
                            break;
                    }
                }
                requestCount++;
            }

        }
        return allSubjectIds.ToList();
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
