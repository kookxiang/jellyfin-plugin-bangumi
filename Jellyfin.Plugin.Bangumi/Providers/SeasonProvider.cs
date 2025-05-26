using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers;

public partial class SeasonProvider(BangumiApi api, Logger<EpisodeProvider> log, ILibraryManager libraryManager)
    : IRemoteMetadataProvider<Season, SeasonInfo>, IHasOrder
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;

    public string Name => Constants.ProviderName;

    private static readonly Dictionary<int, string> chineseOrdinalChars = new()
    {
        { 1, "一" },
        { 2, "二" },
        { 3, "三" },
        { 4, "四" },
        { 5, "五" },
        { 6, "六" },
        { 7, "七" },
        { 8, "八" },
        { 9, "九" },
        { 10, "十" },
    };

    public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Subject? subject = null;

        if (string.IsNullOrEmpty(info.Path))
            return new MetadataResult<Season>();

        var baseName = Path.GetFileName(info.Path);
        var result = new MetadataResult<Season> { ResultLanguage = Constants.Language };
        var localConfiguration = await LocalConfiguration.ForPath(info.Path);

        var seasonPath = Path.GetDirectoryName(info.Path);

        if (IsMiscFolder(info.Path))
        {
            log.Info($"{info.Path} matches misc folder, skip searching");

            // 清除之前获取的元数据
            result.HasMetadata = true;
            result.Item = new Season()
            {
                Name = baseName
            };

            return result;
        }

        var subjectId = 0;
        if (localConfiguration.Id != 0)
        {
            subjectId = localConfiguration.Id;
        }
        else if (int.TryParse(baseName.GetAttributeValue("bangumi"), out var subjectIdFromAttribute))
        {
            subjectId = subjectIdFromAttribute;
        }
        else if (int.TryParse(info.ProviderIds.GetOrDefault(Constants.ProviderName), out var subjectIdFromInfo))
        {
            subjectId = subjectIdFromInfo;
        }
        else if (info.IndexNumber == 1 &&
                 int.TryParse(info.SeriesProviderIds.GetOrDefault(Constants.ProviderName), out var subjectIdFromParent))
        {
            subjectId = subjectIdFromParent;
        }
        else if (seasonPath is not null && libraryManager.FindByPath(seasonPath, true) is Series series)
        {
            log.Info($"Guessing season id by folder path:  {info.Path}");
            subject = await SearchSubjectByFolderPath(series, info.Path, cancellationToken);

            if (subject != null)
            {
                subjectId = subject.Id;
                log.Info("Guessed result: {Name} (#{ID})", subject.Name, subject.Id);
            }
            else
            {
                var previousSeason = series.Children
                    // Search "Season 2" for "Season 1" and "Season 2 Part X"
                    .Where(x => x.IndexNumber == info.IndexNumber - 1 || x.IndexNumber == info.IndexNumber)
                    .MaxBy(x => int.Parse(x.GetProviderId(Constants.ProviderName) ?? "0"));
                if (previousSeason?.Path == info.Path)
                {
                    //This is the first season to be matched, which means season 1 and any other possible previous season is missing. We can just try match it by name.
                    string[] searchNames = [$"{series.Name} 第{chineseOrdinalChars[info.IndexNumber ?? 1]}季", $"{series.Name} Season {info.IndexNumber}"];
                    foreach (var searchName in searchNames)
                    {
                        log.Info($"Guessing season id by name:  {searchName}");
                        var searchResult = await api.SearchSubject(searchName, cancellationToken);
                        if (int.TryParse(info.SeriesProviderIds.GetOrDefault(Constants.ProviderName), out var parentId))
                        {
                            searchResult = searchResult.Where(x => x.Id != parentId);
                        }
                        if (info.Year != null)
                        {
                            searchResult = searchResult.Where(x => x.ProductionYear == null || x.ProductionYear == info.Year?.ToString());
                        }
                        if (searchResult.Any())
                            subjectId = searchResult.First().Id;
                    }
                    log.Info("Guessed result: {Name} (#{ID})", subject?.Name, subject?.Id);
                }
                if (int.TryParse(previousSeason?.GetProviderId(Constants.ProviderName), out var previousSeasonId) && previousSeasonId > 0)
                {
                    log.Info("Guessing season id from previous season #{ID}", previousSeasonId);
                    subject = await api.SearchNextSubject(previousSeasonId, cancellationToken);
                    if (subject != null)
                    {
                        log.Info("Guessed result: {Name} (#{ID})", subject.Name, subject.Id);
                        subjectId = subject.Id;
                    }
                }
            }
        }

        if (subjectId <= 0)
            return result;

        subject ??= await api.GetSubject(subjectId, cancellationToken);

        // return if subject still not found
        if (subject == null)
            return result;

        result.Item = new Season();
        result.HasMetadata = true;

        result.Item.ProviderIds.Add(Constants.ProviderName, subject.Id.ToString());
        result.Item.CommunityRating = subject.Rating?.Score;
        if (Configuration.UseBangumiSeasonTitle)
        {
            result.Item.Name = subject.Name;
            result.Item.OriginalTitle = subject.OriginalName;
        }

        result.Item.Overview = string.IsNullOrEmpty(subject.Summary) ? null : subject.Summary;
        result.Item.Tags = subject.PopularTags.ToArray();
        result.Item.Genres = subject.GenreTags.ToArray();

        if (DateTime.TryParse(subject.AirDate, out var airDate))
        {
            result.Item.PremiereDate = airDate;
            result.Item.ProductionYear = airDate.Year;
        }

        if (subject.ProductionYear?.Length == 4)
            result.Item.ProductionYear = int.Parse(subject.ProductionYear);

        result.Item.HomePageUrl = subject.OfficialWebSite;
        result.Item.EndDate = subject.EndDate;

        if (subject.IsNSFW)
            result.Item.OfficialRating = "X";

        // 获取到的季号可能不准确（例如fsn在Bangumi中前作是fz，但实际上季号一般是独立计算的），因此只在没有设置季号时尝试猜测
        if (int.TryParse(info.ProviderIds.GetOrDefault(Constants.SeasonNumberProviderName), out var seasonNumber))
        {
            result.Item.ProviderIds.Add(Constants.SeasonNumberProviderName, seasonNumber.ToString());
        }
        else
        {
            if (IsSpecialFolder(info.Path))
            {
                result.Item.ProviderIds.Add(Constants.SeasonNumberProviderName, "0");
            }
            else
            {
                var num = await GuessSeasonNumber(subject, cancellationToken);
                if (num.HasValue)
                {
                    result.Item.ProviderIds.Add(Constants.SeasonNumberProviderName, num.ToString());
                }
            }
        }

        (await api.GetSubjectPersonInfos(subject.Id, cancellationToken)).ToList().ForEach(result.AddPerson);
        (await api.GetSubjectCharacters(subject.Id, cancellationToken)).ToList().ForEach(result.AddPerson);

        return result;
    }

    private bool IsMiscFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return false;

        bool result = PluginConfiguration.MatchExcludeRegexes(
            Plugin.Instance!.Configuration.MiscExcludeRegexFullPath,
            folderPath,
            (p, e) => log.Error($"Guessing \"{folderPath}\" season id using regex \"{p}\" failed:  {e.Message}"));

        var folderName = Path.GetFileName(folderPath);
        if (result || string.IsNullOrEmpty(folderName)) return result;

        // 忽略根目录名称
        if (libraryManager.FindByPath(folderPath, true) is not Series)
        {
            result |= PluginConfiguration.MatchExcludeRegexes(
            Plugin.Instance!.Configuration.MiscExcludeRegexFolderName,
            folderName,
            (p, e) => log.Error($"Guessing \"{folderName}\" season id using regex \"{p}\" failed:  {e.Message}"));
        }

        return result;
    }

    private bool IsSpecialFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return false;

        bool result = PluginConfiguration.MatchExcludeRegexes(
            Plugin.Instance!.Configuration.SpExcludeRegexFullPath,
            folderPath,
            (p, e) => log.Error($"Guessing \"{folderPath}\" season id using regex \"{p}\" failed:  {e.Message}"));

        var folderName = Path.GetFileName(folderPath);
        if (result || string.IsNullOrEmpty(folderName)) return result;

        // 忽略根目录名称
        if (libraryManager.FindByPath(folderPath, true) is not Series)
        {
            result |= PluginConfiguration.MatchExcludeRegexes(
            Plugin.Instance!.Configuration.SpExcludeRegexFolderName,
            folderName,
            (p, e) => log.Error($"Guessing \"{folderName}\" season id using regex \"{p}\" failed:  {e.Message}"));
        }

        return result;
    }

    private async Task<Subject?> SearchSubjectByFolderPath(Series series, string folderPath, CancellationToken cancellationToken)
    {
        var folderName = Path.GetFileName(folderPath);
        var anitomy = new Anitomy(folderName);
        var searchName = anitomy.ExtractAnimeTitle();
        if (string.IsNullOrEmpty(searchName))
        {
            log.Error($"Failed to extract anime title from folder path: {folderPath}");
            return null;
        }

        if (!IsValidAnimeTitle(searchName))
        {
            // 仅sp文件夹名无法判断番剧，添加番剧名称作为前缀
            searchName = $"{series.Name} {searchName}";
        }

        log.Info($"Guessing season id by folder name: {searchName}");
        var subjects = await api.SearchSubjectRaw(searchName, SubjectType.Anime, cancellationToken);

        return await api.GetBestMatchSubjectWithKeywords(subjects, [searchName], cancellationToken);
    }

    [GeneratedRegex(@"^(" +
        @"第?([零一二三四五六七八九十\d]+)[季部期]?" + "|" + // 纯数字, 第一季, 二期, 第3部
        @"S\d+" + "|" + // S1, S01
        @"Season\s*\d+" + "|" + // Season 1
        @"I|II|III|IV|V|VI|VII|VIII|IX|X|XI|XII|XIII|XIV|XV|XVI|XVII|XVIII|XIX|XX" + "|" + // 罗马数字1-20
        @"\d+(st|nd|rd|th)(\s*Season)?" + "|" + // 1st Season
        @"Season\s*(One|Two|Three|Four|Five|Six|Seven|Eight|Nine|Ten)" + // 英文单词
        @")$", RegexOptions.IgnoreCase)]
    public static partial Regex OnlySeasonNumberRegex();

    /// <summary>
    /// 判断文件夹名是否有效的番剧名称
    /// </summary>
    /// <param name="folderName">文件夹名</param>
    /// <returns></returns>
    private static bool IsValidAnimeTitle(string folderName)
    {
        var type = AnitomyEpisodeTypeMapping.GetAnitomyAndBangumiEpisodeType([folderName]);
        if (type.Item1 != null && folderName == type.Item1)
        {
            // 只包含剧集类型关键词，如 "SP", "OVA" 等
            return false;
        }

        if (OnlySeasonNumberRegex().IsMatch(folderName))
        {
            // 只包含季号或类似的关键词，如 "S1", "Season 2" 等
            return false;
        }

        return true;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
    {
        return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return api.GetHttpClient().GetAsync(url, cancellationToken);
    }

    private async Task<int?> GuessSeasonNumber(Subject subject, CancellationToken cancellationToken)
    {
        if (BangumiApi.IsOVAOrMovie(subject)) return 0;

        log.Info($"Guessing season number for {subject.Name} ({subject.Id})");

        int maxRequestCount = 10;
        // 查找所有前传条目
        var subjects = await api.SearchPreviousSubjects(subject.Id, maxRequestCount, cancellationToken);

        // 达到最大请求次数，可能是前传条目过多
        if (subjects.Count == maxRequestCount + 1)
        {
            Subject earliest = subjects.Last().OrderBy(s => s.Id).First();
            var prev = api.SearchPreviousSubject(earliest.Id, 1, cancellationToken);

            if (prev == null) return null;
            // 还能继续查找前传条目，超出最大请求次数，无法判断季号
            if (prev.Id != earliest.Id) return null;
        }

        // 根据前传数量判断季号
        return subjects.Count switch
        {
            0 => null,// 找不到当前条目，无法判断
            _ => subjects.Count,// 
        };
    }
}
