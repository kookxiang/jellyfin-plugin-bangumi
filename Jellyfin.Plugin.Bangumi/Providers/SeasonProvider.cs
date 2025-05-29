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
using Jellyfin.Plugin.Bangumi.Utils;
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
        var searchNameSeason = GetValidAnimeTitleAndSeason(log, series, folderPath);
        if (searchNameSeason == default)
        {
            log.Error($"Failed to extract anime title from folder path: {folderPath}");
            return null;
        }
        else
        {
            log.Info($"Search subject by folder path: {folderPath}, name: {searchNameSeason.Item1}, season: {searchNameSeason.Item2}");
        }

        var subjects = await api.SearchSubject(searchNameSeason.Item1, cancellationToken, searchNameSeason.Item2);

        return subjects?.FirstOrDefault();
    }

    /// <summary>
    /// 从文件夹路径获取有效的番剧名称用于搜索，包含季号
    /// </summary>
    /// <param name="folderPath">文件夹路径</param>
    /// <returns></returns>
    private static (string, int?) GetValidAnimeTitleAndSeason<T>(Logger<T> log, Series series, string folderPath)
    {
        var folderName = Path.GetFileName(folderPath);

        // 只包含剧集类型关键词，如 "SP", "OVA" 等
        var type = AnitomyEpisodeTypeMapping.GetAnitomyAndBangumiEpisodeType([folderName]);
        if (type.Item1 != null && folderName == type.Item1)
        {
            folderName = $"{series.Name} {folderName}";
            return default;
        }

        var anitomy = new Anitomy(folderName);
        // 使用 Anitomy 清理文件名
        var searchName = anitomy.ExtractAnimeTitle();
        if (string.IsNullOrEmpty(searchName))
        {
            return default;
        }

        (string, int?) result = default;
        if (int.TryParse(anitomy.ExtractAnimeSeason(), out var season))
        {
            // 如果包含季号，直接返回
            result = (searchName, season);
        }
        else if (int.TryParse(anitomy.ExtractEpisodeNumber(), out season))
        {
            // 有时会解析成集号，如：[VCB-Studio] Log Horizon 2 [Ma10p_1080p]
            result = (searchName, season);
        }
        else
        {
            // Anitomy只能获取部分数字季号，使用其他方法再次尝试获取季号
            var split = FileNameParser.SplitAnimeTitleAndSeason(searchName, false);
            result = (split.Item1, (int?)split.Item2);
        }

        if (result.Item2 != null && result.Item2 < 1)
            return (folderName, null);
        else
            return result;
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
