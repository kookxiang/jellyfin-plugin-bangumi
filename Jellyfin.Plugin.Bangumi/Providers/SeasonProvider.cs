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

public class SeasonProvider(BangumiApi api, Logger<SeasonProvider> log, ILibraryManager libraryManager)
    : IRemoteMetadataProvider<Season, SeasonInfo>, IHasOrder
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;

    public string Name => Constants.ProviderName;

    private static readonly Dictionary<int, string> ChineseOrdinalChars = new()
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
            return await GetMetadataForVirtualSeason(info, cancellationToken);

        var baseName = Path.GetFileName(info.Path);
        var result = new MetadataResult<Season> { ResultLanguage = Constants.Language };
        var localConfiguration = await LocalConfiguration.ForPath(info.Path);

        var seasonPath = Path.GetDirectoryName(info.Path);

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
        else if (seasonPath is not null && libraryManager.FindByPath(seasonPath, true) is Series series && info.IndexNumber is not null)
        {
            var children = libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                Parent = series,
                IncludeItemTypes = new[] { Data.Enums.BaseItemKind.Season }
            });
            var previousSeason = children
                // Search "Season 2" for "Season 1" and "Season 2 Part X"
                .Where(x => x.IndexNumber == info.IndexNumber - 1 || x.IndexNumber == info.IndexNumber)
                .MaxBy(x => int.Parse(x.GetProviderId(Constants.ProviderName) ?? "0"));

            var infoPath = info.Path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (previousSeason?.Path == infoPath)
            {
                try
                {
                    //This is the first season to be matched, which means season 1 and any other possible previous season is missing. We can just try match it by name.
                    string[] searchNames =
                    [
                        $"{series.Name} 第{ChineseOrdinalChars[info.IndexNumber ?? 1]}季",
                        $"{series.Name} Season {info.IndexNumber}"
                    ];
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
                            searchResult = searchResult.Where(x =>
                                x.ProductionYear == null || x.ProductionYear == info.Year?.ToString());
                        }

                        if (searchResult.Any())
                            subjectId = searchResult.First().Id;
                    }

                    log.Info("Guessed result: {Name} (#{ID})", subject?.Name, subject?.Id);
                }
                catch (Exception ex)
                {
                    log.Error("Error occurred while guessing season id by name: {Error}", ex);
                }
            }

            if (int.TryParse(previousSeason?.GetProviderId(Constants.ProviderName), out var previousSeasonId) &&
                previousSeasonId > 0)
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

        if (subjectId <= 0 && Configuration.ProcessMultiSeasonFolderByAnitomySharp)
            subjectId = await ProcessMultiSeasonFolder(subjectId, info, cancellationToken);

        if (subjectId <= 0)
            return result;

        subject ??= await api.GetSubject(subjectId, cancellationToken);

        // return if subject still not found
        if (subject == null)
            return result;

        FillSeasonMetadata(result, subject);

        if (info.IndexNumber != null)
        {
            log.Info("Use exist Season {seasonNumber} for {parent}", info.IndexNumber, seasonPath);
            result.Item.IndexNumber = info.IndexNumber;
        }
        else if (Configuration.UseBangumiRelationChainForSeasonNumber)
        {
            var chain = await api.GetPrequelSeriesSubjectIds(subjectId, cancellationToken);
            result.Item.IndexNumber = chain.Count;
            log.Info("Use chain Season {seasonNumber} for {parent}", chain.Count, seasonPath);
        }

        (await api.GetSubjectPersonInfos(subject.Id, cancellationToken)).ToList().ForEach(result.AddPerson);
        (await api.GetSubjectCharacters(subject.Id, cancellationToken)).ToList().ForEach(result.AddPerson);

        return result;
    }

    private static void FillSeasonMetadata(MetadataResult<Season> result, Subject subject)
    {
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
    }

    private async Task<MetadataResult<Season>> GetMetadataForVirtualSeason(SeasonInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Season> { ResultLanguage = Constants.Language };
        if (!int.TryParse(info.ProviderIds.GetOrDefault(Constants.ProviderName), out var subjectId))
            return result;

        var subject = await api.GetSubject(subjectId, cancellationToken);
        if (subject == null)
            return result;

        FillSeasonMetadata(result, subject);
        result.Item.IndexNumber = info.IndexNumber;
        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        using var httpClient = api.GetHttpClient();
        return await httpClient.GetAsync(url, cancellationToken);
    }


    /// <summary>
    /// 处理多季度文件夹
    /// 根据文件夹名称搜索，或者使用已存在的 id
    /// </summary>
    /// <param name="seasonId"></param>
    /// <returns></returns>
    private async Task<int> ProcessMultiSeasonFolder(int seasonId, SeasonInfo info, CancellationToken cancellationToken)
    {
        // 获取当前目录
        var folderItem = libraryManager.FindByPath(info.Path!, true);
        log.Debug("Jellyfin folder name: {folder}", folderItem);

        // 限制目录类型
        switch (folderItem)
        {
            // Series 类型
            case MediaBrowser.Controller.Entities.TV.Series series:
                log.Debug("{folder} is Series Folder", folderItem);
                break;

            // Season 类型
            case MediaBrowser.Controller.Entities.TV.Season season:
                log.Debug("{folder} is Season Folder", folderItem);
                break;

            // 普通文件夹，但其父级是 Series（应视为 Season）
            case MediaBrowser.Controller.Entities.Folder folder
                when folder.GetParent() is MediaBrowser.Controller.Entities.TV.Series:
                log.Debug("{folder} is a folder under a Series, treating as Season", folderItem);
                break;

            // 其他类型或不符合条件，跳过
            // 比如多层嵌套（other）：series/season/other
            default:
                log.Debug("{folder} is not a recognized type or not under Series, skip", folderItem);
                return seasonId;
        }

        // 如果在 Jellyfin 中已配置，则直接返回此配置值
        _ = int.TryParse(folderItem!.ProviderIds.GetOrDefault(Constants.ProviderName), out var folderId);
        if (folderId > 0)
        {
            log.Debug("Multi season folder, use exist id: {folderId}", folderId);
            return folderId;
        }

        // 检查是否应跳过处理
        if (ShouldSkipFolder(folderItem.Name) || folderItem.IsVirtualItem)
        {
            log.Debug("Skip special folder: {folderItem}", folderItem);
            return seasonId;
        }

        _ = int.TryParse(info.SeriesProviderIds.GetOrDefault(Constants.ProviderName), out var seriesId);
        if (seriesId <= 0)
        {
            log.Warn("Multi season folder, no series id found for {folderItem}, skip", folderItem);
            return seasonId;
        }
        // 搜索
        var searchName = folderItem.Name;
        if (IsSeasonNameFolder(searchName))
            // 路径名
            searchName = folderItem.FileNameWithoutExtension;
        string? animeYear = null;
        if (Configuration.AlwaysGetTitleByAnitomySharp)
        {
            var anitomyParent = new Anitomy(searchName);
            searchName = anitomyParent.ExtractAnimeTitle();
            animeYear = anitomyParent.ExtractAnimeYear();
        }
        if (searchName is null) return seasonId;
        log.Info("Multi season folder, Searching {Name} in bgm.tv", searchName);

        var searchResult = await api.SearchSubject(searchName, cancellationToken);

        if (animeYear != null)
            searchResult = searchResult.Where(x => x.ProductionYear == animeYear);

        if (searchResult.Any())
        {
            var searchResultSubjectId = searchResult.First().Id;
            // 检查与旧 seriesId 的关联性，如果无联系则说明可能匹配错误
            // 获取此 id 对应的系列所有 id
            var bangumiSeriesIds = await api.GetAllAnimeSeriesSubjectIds(seriesId, cancellationToken);
            if (bangumiSeriesIds.Any(id => id == searchResultSubjectId))
            {
                log.Info("Multi season folder, Use subject id: {id}", searchResultSubjectId);
                return searchResultSubjectId;
            }

        }

        return seasonId;
    }
    private static readonly Regex _chineseSeasonRegex = new Regex(
    @"第\s*[0-9一二三四五六七八九十百千]+\s*季",
    RegexOptions.Compiled | RegexOptions.IgnoreCase
);

    private static readonly Regex _englishSeasonRegex = new Regex(
        @"season\s*\d+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly HashSet<string> _confusingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SP", "ED", "OP", "IV"
    };
    private static readonly string[] _effectiveSkipWords = AnitomyEpisodeTypeMapping.SkipWords
        .Where(k => !_confusingWords.Contains(k))
        .ToArray();
    private static bool ShouldSkipFolder(string folderName)
    {
        return _effectiveSkipWords
            .Any(keyword => folderName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
    private static bool IsSeasonNameFolder(string folderName)
    {
        if (_chineseSeasonRegex.IsMatch(folderName))
            return true;
        if (_englishSeasonRegex.IsMatch(folderName))
            return true;
        return false;
    }
}
