using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities;
using Person = Jellyfin.Plugin.Bangumi.Model.Person;
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

    /// <summary>
    /// 搜索动画条目信息
    /// </summary>
    /// <param name="keyword">搜索关键词，不区分大小写</param>
    /// <param name="token"></param>
    /// <param name="seasonNumber">季号，由于季号存在多种格式（如第二季、Season 2、II），直接放到关键词中搜索可能不准确，因此单独传入进行辅助判断
    ///     <br/>如果为null，仅对关键词进行常规匹配；
    ///     <br/>如果为0，只匹配OVA、剧场版条目；
    ///     <br/>如果为其他，则同时匹配候选列表及关键词中的季号，降低不匹配候选项分数
    ///     <br/><br/>不为null时，需要确保 <paramref name="keyword"/> 不包含季号信息，否则匹配度可能降低
    /// </param>
    /// <returns>条目信息集合</returns>
    public Task<IEnumerable<Subject>> SearchSubject(string keyword, CancellationToken token, int? seasonNumber = null)
    {
        return SearchSubject(keyword, SubjectType.Anime, token, seasonNumber);
    }

    /// <summary>
    /// 搜索条目信息
    /// </summary>
    /// <param name="keyword">搜索关键词，不区分大小写</param>
    /// <param name="type">条目类型</param>
    /// <param name="token"></param>
    /// <param name="seasonNumber">季号，由于季号存在多种格式（如第二季、Season 2、II），直接放到关键词中搜索可能不准确，因此单独传入进行辅助判断
    ///     <br/>如果为null，仅对关键词进行常规匹配；
    ///     <br/>如果为0，只匹配OVA、剧场版条目；
    ///     <br/>如果为其他，则同时匹配候选列表及关键词中的季号，降低不匹配候选项分数
    ///     <br/><br/>不为null时，需要确保 <paramref name="keyword"/> 不包含季号信息，否则匹配度可能降低
    /// </param>
    /// <returns>条目信息集合</returns>
    public async Task<IEnumerable<Subject>> SearchSubject(string keyword, SubjectType? type, CancellationToken token, int? seasonNumber = null)
    {
        var result = await SearchSubjectSorted(keyword, type, token, seasonNumber);

        return result.Select(s => s.Item1);
    }

    /// <summary>
    /// 搜索条目信息
    /// </summary>
    /// <param name="keyword">搜索关键词，不区分大小写</param>
    /// <param name="type">条目类型</param>
    /// <param name="token"></param>
    /// <param name="seasonNumber">季号，由于季号存在多种格式（如第二季、Season 2、II），直接放到关键词中搜索可能不准确，因此单独传入进行辅助判断
    ///     <br/>如果为null，仅对关键词进行常规匹配；
    ///     <br/>如果为0，只匹配OVA、剧场版条目；
    ///     <br/>如果为其他，则同时匹配候选列表及关键词中的季号，降低不匹配候选项分数
    ///     <br/><br/>不为null时，需要确保 <paramref name="keyword"/> 不包含季号信息，否则匹配度可能降低
    /// </param>
    /// <returns>条目信息集合</returns>
    public async Task<IEnumerable<(Subject, int)>> SearchSubjectSorted(string keyword, SubjectType? type, CancellationToken token, int? seasonNumber = null)
    {
        var list = await SearchSubjectRaw(keyword, type, token);

        return await SortSubjects(list, keyword, token, seasonNumber);
    }

    /// <summary>
    /// 搜索条目信息
    /// </summary>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="type">条目类型</param>
    /// <param name="token"></param>
    /// <returns>接口返回的原始条目信息集合</returns>
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

    /// <summary>
    /// 对条目搜索结果进行排序
    /// </summary>
    /// <param name="list">搜索结果</param>
    /// <param name="keyword">搜索关键词，不区分大小写</param>
    /// <param name="token"></param>
    /// <param name="seasonNumber">季号，由于季号存在多种格式（如第二季、Season 2、II），直接放到关键词中搜索可能不准确，因此单独传入进行辅助判断
    ///     <br/>如果为null，仅对关键词进行常规匹配；
    ///     <br/>如果为0，只匹配OVA、剧场版条目；
    ///     <br/>如果为其他，则同时匹配候选列表及关键词中的季号，降低不匹配候选项分数
    ///     <br/><br/>不为null时，需要确保 <paramref name="keyword"/> 不包含季号信息，否则匹配度可能降低
    /// </param>
    /// <returns>（条目、分数）元组集合，按匹配度由高到低排序。
    ///     <br/>分数最高为100，表示完全匹配；分数最低为0，表示完全不匹配。
    /// </returns>
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
                // 搜索结果不包含别名信息，尝试获取详情补全信息用于排序
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

    public async Task<Subject?> GetSubject(int id, CancellationToken token)
    {
        if (id <= 0) return null;
#if !EMBY
        var subject = await archive.Subject.FindById(id, token);
        if (subject != null)
            return subject.ToSubject();
        if (!_plugin.Configuration.FallbackToOnlineWhenArchiveMiss)
            return null;
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
        var episodeList = (await archive.SubjectEpisodeRelation.GetEpisodes(id, token))
            .Where(x => x.Type == type || type == null)
            .Select(x => x.ToEpisode());
        if (episodeList.Any()) return episodeList;
        if (!_plugin.Configuration.FallbackToOnlineWhenArchiveMiss)
            return episodeList;
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
        var result = await Get<DataList<Episode>>(url, token);
        if (result is { Total: > 0 })
            return result;

        // Fallback to legacy API for older music subjects or empty v0 results
        var legacyUrl = $"{BaseUrl}/subject/{id}?responseGroup=large";
        var legacyResult = await Get<LegacySubject>(legacyUrl, token);
        if (legacyResult?.Eps == null || legacyResult.Eps.Count == 0)
            return result;

        var eps = legacyResult.Eps
            .Where(ep => type == null || ep.Type == type)
            .Select(ep => new Episode
            {
                Id = ep.Id,
                ParentId = id,
                Type = ep.Type,
                OriginalNameRaw = ep.Name,
                ChineseNameRaw = ep.NameCn,
                Order = ep.Sort,
                Disc = ep.Disc,
                AirDate = ep.AirDate,
                DescriptionRaw = ep.Desc
            }).ToList();

        return new DataList<Episode>
        {
            Total = eps.Count,
            Limit = PageSize,
            Offset = (int)offset,
            Data = eps.Skip((int)offset).Take(PageSize).ToList()
        };
    }

    private class LegacySubject
    {
        [JsonPropertyName("eps")]
        public List<LegacyEpisode>? Eps { get; set; }
    }

    private class LegacyEpisode
    {
        public int Id { get; set; }
        public EpisodeType Type { get; set; }
        public string Name { get; set; } = "";
        [JsonPropertyName("name_cn")]
        public string? NameCn { get; set; }
        public double Sort { get; set; }
        public int Disc { get; set; }
        [JsonPropertyName("airdate")]
        public string AirDate { get; set; } = "";
        public string? Desc { get; set; }
    }

    public async Task<IEnumerable<RelatedSubject>?> GetRelatedSubjects(int id, CancellationToken token)
    {
        if (id <= 0) return null;
#if !EMBY
        var relations = await archive.SubjectRelations.Get(id, token);
        if (relations.Any())
            return relations;
        if (!_plugin.Configuration.FallbackToOnlineWhenArchiveMiss)
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

    /// <summary>
    /// 获取续集条目
    /// </summary>
    /// <param name="id">Bangumi条目id</param>
    /// <param name="token"></param>
    /// <returns>返回下一季正篇条目，找不到则返回null</returns>
    public async Task<Subject?> SearchNextSubject(int id, CancellationToken token)
    {
        if (id <= 0) return null;

        //What would happen in Emby if I use `_plugin`?
        var maxRequestCount = Plugin.Instance?.Configuration?.SeasonGuessMaxSearchCount ?? 2;
        var relatedSubjects = await GetRelatedSubjects(id, token);
        var currentLevel = relatedSubjects?.Where(item => item.Relation == SubjectRelation.Sequel).ToArray() ?? [];

        for (int i = 0; i < maxRequestCount && currentLevel.Length > 0; i++)
        {
            // 获取所有续集条目
            var subjects = await Task.WhenAll(currentLevel.Select(rs => GetSubject(rs.Id, token)));
            var validSubjects = subjects.OfType<Subject>().ToArray();

            // 如果有正篇则直接返回
            var candidate = validSubjects.FirstOrDefault(s => !IsOVAOrMovie(s));
            if (candidate != null)
            {
                Console.WriteLine($"BangumiApi: Season guess of id #{id} end at level {i + 1}");
                return candidate;
            }

            // 如果续集都非正篇，则继续往下一层查询，示例：https://bangumi.tv/subject/152091
            var nextLevel = new List<RelatedSubject>();
            foreach (var subject in validSubjects)
            {
                var nextRelated = await GetRelatedSubjects(subject.Id, token);
                if (nextRelated != null)
                    nextLevel.AddRange(nextRelated.Where(rs => rs.Relation == SubjectRelation.Sequel));
            }
            currentLevel = [.. nextLevel];
        }

        Console.WriteLine($"BangumiApi: Season guess of id #{id} failed after {maxRequestCount} levels");
        return null;
    }

    /// <summary>
    /// 获取前传条目
    /// </summary>
    /// <param name="id">Bangumi条目id</param>
    /// <param name="maxRequestCount">最大查找层数</param>
    /// <param name="token"></param>
    /// <returns>
    ///     返回距离 <paramref name="id"/> 条目最多前 <paramref name="maxRequestCount"/> 季的条目直至第一季，
    ///     <paramref name="id"/> 本身是第一季的话则返回自身，
    ///     如果 <paramref name="id"/> 条目不存在则返回null
    /// </returns>
    public async Task<Subject?> SearchPreviousSubject(int id, int maxRequestCount, CancellationToken token)
    {
        var subjects = await SearchPreviousSubjects(id, maxRequestCount, token);
        return subjects.LastOrDefault()?.FirstOrDefault();
    }

    /// <summary>
    /// 获取前传条目
    /// </summary>
    /// <param name="id">Bangumi条目id</param>
    /// <param name="maxRequestCount">最大查找层数</param>
    /// <param name="token"></param>
    /// <returns>前传条目列表，第一个数组是 <paramref name="id"/> 的条目，之后每个数组是上一个数组的前传条目集合</returns>
    public async Task<List<Subject[]>> SearchPreviousSubjects(int id, int maxRequestCount, CancellationToken token)
    {
        if (id <= 0 || maxRequestCount <= 0) return [];

        // 获取当前条目
        var currentSubject = await GetSubject(id, token);
        if (currentSubject == null) return [];

        var result = new List<Subject[]> { new Subject[] { currentSubject } };

        for (int i = 0; i < maxRequestCount; i++)
        {
            // 获取最上层条目列表
            Subject[] lastLoopSubjects = result[^1];

            // 对于每个条目，获取其前传条目并加入当前层结果
            List<Subject> currentLoopResult = [];
            foreach (var subject in lastLoopSubjects)
            {
                await AddPrequelSubjectsFromSubject(subject, currentLoopResult, token);
            }

            // 找不到更多前传条目，提前结束循环
            if (currentLoopResult.Count == 0) break;

            result.Add([.. currentLoopResult]);
        }

        return result;
    }

    /// <summary>
    /// 处理单个条目，提取其前传并补全为 <see cref="Subject"/> 后加入当前层结果。
    /// </summary>
    /// <param name="subject">当前待处理条目。</param>
    /// <param name="currentLoopResult">当前层前传结果集合。</param>
    /// <param name="token">取消令牌。</param>
    private async Task AddPrequelSubjectsFromSubject(Subject subject, List<Subject> currentLoopResult, CancellationToken token)
    {
        // 获取相关条目
        var relatedSubjects = await GetRelatedSubjects(subject.Id, token);
        if (relatedSubjects == null) return;

        // 过滤出前传类型的条目
        var prequels = relatedSubjects.Where(item => item.Relation == SubjectRelation.Prequel).ToArray();
        if (prequels.Length == 0) return;

        // 获取前传条目id列表，排除已在当前层结果中的条目id
        var idsToFetch = prequels
            .Select(item => item.Id)
            .Where(id => !currentLoopResult.Any(s => s.Id == id))
            .Distinct()
            .ToArray();

        if (idsToFetch.Length == 0) return;

        var subjects = await Task.WhenAll(idsToFetch.Select(id => GetSubject(id, token)));
        var validSubjects = subjects.OfType<Subject>().ToArray();

        // 过滤非正篇，由于前传条目可能都非正篇，因此只在有正篇的情况下过滤，示例：https://bangumi.tv/subject/283643、https://bangumi.tv/subject/152091
        if (validSubjects.Any(s => !IsOVAOrMovie(s)))
        {
            validSubjects = [.. validSubjects.Where(s => !IsOVAOrMovie(s))];
        }

        currentLoopResult.AddRange(validSubjects);
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
        var relatedPerson = await archive.SubjectPersonRelation.Get(id, token);
        if (relatedPerson.Any())
            return relatedPerson;
        if (!_plugin.Configuration.FallbackToOnlineWhenArchiveMiss)
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
        var episode = await archive.Episode.FindById(id, token);
        if (episode != null && DateTime.TryParse(episode.AirDate, out var airDate))
            if (_plugin.Configuration.DaysBeforeUsingArchiveData == 0 ||
                airDate < DateTime.Now.Subtract(TimeSpan.FromDays(_plugin.Configuration.DaysBeforeUsingArchiveData)))
                return episode.ToEpisode();
        if (!_plugin.Configuration.FallbackToOnlineWhenArchiveMiss)
            return episode?.ToEpisode();

#endif
        return await Get<Episode>($"{BaseUrl}/v0/episodes/{id}", token);
    }

    public async Task<PersonDetail?> GetPerson(int id, CancellationToken token)
    {
        if (id <= 0) return null;
#if !EMBY
        var person = await archive.Person.FindById(id, token);
        if (person != null)
            return person.ToPersonDetail();
        if (!_plugin.Configuration.FallbackToOnlineWhenArchiveMiss)
            return null;
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

    public async Task<IEnumerable<Person>?> SearchPerson(string keyword, CancellationToken token)
    {
        var searchResult = await Post<DataList<Person>>($"{BaseUrl}/v0/search/persons", new JsonContent(new SearchParams { Keyword = keyword }), token);
        return searchResult?.Data;
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
