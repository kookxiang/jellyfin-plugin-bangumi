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
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class EpisodeProvider(BangumiApi api, ILogger<EpisodeProvider> log, ILibraryManager libraryManager)
    : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{
    private static readonly Regex[] NonEpisodeFileNameRegex =
    {
        new(@"[\[\(][0-9A-F]{8}[\]\)]", RegexOptions.IgnoreCase),
        new(@"S\d{2,}", RegexOptions.IgnoreCase),
        new(@"yuv[4|2|0]{3}p(10|8)?", RegexOptions.IgnoreCase),
        new(@"\d{3,4}p", RegexOptions.IgnoreCase),
        new(@"\d{3,4}x\d{3,4}", RegexOptions.IgnoreCase),
        new(@"(Hi)?10p", RegexOptions.IgnoreCase),
        new(@"(8|10)bit", RegexOptions.IgnoreCase),
        new(@"(x|h)(264|265)", RegexOptions.IgnoreCase)
    };

    private static readonly Regex[] EpisodeFileNameRegex =
    {
        new(@"\[([\d\.]{2,})\]"),
        new(@"- ?([\d\.]{2,})"),
        new(@"EP?([\d\.]{2,})", RegexOptions.IgnoreCase),
        new(@"\[([\d\.]{2,})"),
        new(@"#([\d\.]{2,})"),
        new(@"(\d{2,})"),
        new(@"\[([\d\.]+)\]")
    };

    private static readonly Regex OpeningEpisodeFileNameRegex = new(@"(NC)?OP([^a-zA-Z]|$)");
    private static readonly Regex EndingEpisodeFileNameRegex = new(@"(NC)?ED([^a-zA-Z]|$)");
    private static readonly Regex SpecialEpisodeFileNameRegex = new(@"(SPs?|Specials?|OVA|OAD)([^a-zA-Z]|$)");
    private static readonly Regex PreviewEpisodeFileNameRegex = new(@"[^\w]PV([^a-zA-Z]|$)");

    private static readonly Regex[] AllSpecialEpisodeFileNameRegex =
    {
        SpecialEpisodeFileNameRegex,
        PreviewEpisodeFileNameRegex,
        OpeningEpisodeFileNameRegex,
        EndingEpisodeFileNameRegex
    };

    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var localConfiguration = await LocalConfiguration.ForPath(info.Path);
        var episode = await GetEpisode(info, localConfiguration, token);

        log.LogInformation("metadata for {FilePath}: {EpisodeInfo}", Path.GetFileName(info.Path), episode);

        var result = new MetadataResult<Episode> { ResultLanguage = Constants.Language };

        if (episode == null)
            return result;

        result.Item = new Episode();
        result.HasMetadata = true;
        result.Item.ProviderIds.Add(Constants.ProviderName, $"{episode.Id}");

        if (DateTime.TryParse(episode.AirDate, out var airDate))
            result.Item.PremiereDate = airDate;
        if (episode.AirDate.Length == 4)
            result.Item.ProductionYear = int.Parse(episode.AirDate);

        result.Item.Name = episode.Name;
        result.Item.OriginalTitle = episode.OriginalName;
        result.Item.IndexNumber = (int)episode.Order + localConfiguration.Offset;
        result.Item.Overview = string.IsNullOrEmpty(episode.Description) ? null : episode.Description;
        result.Item.ParentIndexNumber = info.ParentIndexNumber ?? 1;

        var parent = libraryManager.FindByPath(Path.GetDirectoryName(info.Path), true);
        if (IsSpecial(info.Path, false) || episode.Type == EpisodeType.Special || info.ParentIndexNumber == 0)
        {
            result.Item.ParentIndexNumber = 0;
        }
        else if (parent is Season season)
        {
            result.Item.SeasonId = season.Id;
            if (season.IndexNumber != null)
                result.Item.ParentIndexNumber = season.IndexNumber;
        }

        if (episode.Type == EpisodeType.Normal && result.Item.ParentIndexNumber > 0)
            return result;

        // mark episode as special
        result.Item.ParentIndexNumber = 0;

        // use title and overview from special episode subject if episode data is empty
        var series = await api.GetSubject(episode.ParentId, token);
        if (series == null)
            return result;

        // use title and overview from special episode subject if episode data is empty
        if (string.IsNullOrEmpty(result.Item.Name))
            result.Item.Name = series.Name;
        if (string.IsNullOrEmpty(result.Item.OriginalTitle))
            result.Item.OriginalTitle = series.OriginalName;
        if (string.IsNullOrEmpty(result.Item.Overview))
            result.Item.Overview = series.Summary;

        var seasonNumber = parent is Season ? parent.IndexNumber : 1;
        if (!string.IsNullOrEmpty(episode.AirDate) && string.Compare(episode.AirDate, series.AirDate, StringComparison.Ordinal) < 0)
            result.Item.AirsBeforeEpisodeNumber = seasonNumber;
        else
            result.Item.AirsAfterSeasonNumber = seasonNumber;

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        return api.GetHttpClient().GetAsync(url, token);
    }

    private static bool IsSpecial(string filePath, bool checkParent = true)
    {
        var fileName = Path.GetFileName(filePath);
        var parentPath = Path.GetDirectoryName(filePath);
        var folderName = Path.GetFileName(parentPath);
        return SpecialEpisodeFileNameRegex.IsMatch(fileName) ||
               (checkParent && SpecialEpisodeFileNameRegex.IsMatch(folderName ?? ""));
    }

    private async Task<Model.Episode?> GetEpisode(EpisodeInfo info, LocalConfiguration localConfiguration, CancellationToken token)
    {
        var fileName = Path.GetFileName(info.Path);
        if (string.IsNullOrEmpty(fileName))
            return null;

        var type = IsSpecial(info.Path) ? EpisodeType.Special : GuessEpisodeTypeFromFileName(fileName);
        var seriesId = localConfiguration.Id;

        var parent = libraryManager.FindByPath(Path.GetDirectoryName(info.Path), true);
        if (parent is Season)
            if (int.TryParse(parent.ProviderIds.GetValueOrDefault(Constants.ProviderName), out var seasonId))
                seriesId = seasonId;

        if (seriesId == 0)
            if (!int.TryParse(info.SeriesProviderIds?.GetValueOrDefault(Constants.ProviderName), out seriesId))
                return null;

        if (localConfiguration.Id != 0)
            seriesId = localConfiguration.Id;

        double? episodeIndex = info.IndexNumber;

        if (Configuration.AlwaysReplaceEpisodeNumber)
            episodeIndex = GuessEpisodeNumber(episodeIndex, fileName);
        else if (episodeIndex is null or 0)
            episodeIndex = GuessEpisodeNumber(episodeIndex, fileName);

        if (localConfiguration.Offset != 0)
        {
            log.LogInformation("applying offset {Offset} to episode index {EpisodeIndex}", -localConfiguration.Offset, episodeIndex);
            episodeIndex -= localConfiguration.Offset;
        }

        if (int.TryParse(info.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var episodeId))
        {
            var episode = await api.GetEpisode(episodeId, token);
            if (episode == null)
                goto SkipBangumiId;

            if (Configuration.TrustExistedBangumiId)
                return episode;

            if (episode.Type != EpisodeType.Normal || AllSpecialEpisodeFileNameRegex.Any(x => x.IsMatch(info.Path)))
                return episode;

            if (episode.ParentId == seriesId && Math.Abs(episode.Order - episodeIndex.Value) < 0.1)
                return episode;
        }

        SkipBangumiId:
        var episodeListData = await api.GetSubjectEpisodeList(seriesId, type, episodeIndex.Value, token);
        if (episodeListData == null)
            return null;
        if (episodeListData.Count == 1 && type is null or EpisodeType.Normal)
            return episodeListData.First();
        if (type is null or EpisodeType.Normal)
            episodeIndex = GuessEpisodeNumber(
                episodeIndex + localConfiguration.Offset,
                fileName,
                episodeListData.Max(x => x.Order) + localConfiguration.Offset
            ) - localConfiguration.Offset;
        try
        {
            var episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(episodeIndex));
            if (episode != null || type is null or EpisodeType.Normal)
                return episode;
            log.LogWarning("cannot find episode {index} with type {type}, searching all types", episodeIndex, type);
            type = null;
            goto SkipBangumiId;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private EpisodeType? GuessEpisodeTypeFromFileName(string fileName)
    {
        var tempName = fileName;
        foreach (var regex in NonEpisodeFileNameRegex)
        {
            if (!regex.IsMatch(tempName))
                continue;
            tempName = regex.Replace(tempName, "");
        }

        if (OpeningEpisodeFileNameRegex.IsMatch(tempName))
            return EpisodeType.Opening;
        if (EndingEpisodeFileNameRegex.IsMatch(tempName))
            return EpisodeType.Ending;
        if (SpecialEpisodeFileNameRegex.IsMatch(tempName))
            return EpisodeType.Special;
        if (PreviewEpisodeFileNameRegex.IsMatch(tempName))
            return EpisodeType.Preview;
        return null;
    }

    private double GuessEpisodeNumber(double? current, string fileName, double max = double.PositiveInfinity)
    {
        var tempName = fileName;
        var episodeIndex = current ?? 0;
        var episodeIndexFromFilename = episodeIndex;

        if (Configuration.AlwaysGetEpisodeByAnitomySharp)
        {
            var anitomyIndex = Anitomy.ExtractEpisodeNumber(fileName);
            if (!string.IsNullOrEmpty(anitomyIndex))
                return double.Parse(anitomyIndex);
        }

        foreach (var regex in NonEpisodeFileNameRegex)
        {
            if (!regex.IsMatch(tempName))
                continue;
            tempName = regex.Replace(tempName, "");
        }

        foreach (var regex in EpisodeFileNameRegex)
        {
            if (!regex.IsMatch(tempName))
                continue;
            if (!double.TryParse(regex.Match(tempName).Groups[1].Value.Trim('.'), out var index))
                continue;
            episodeIndexFromFilename = index;
            break;
        }

        if (Configuration.AlwaysReplaceEpisodeNumber)
        {
            log.LogWarning("use episode index {NewIndex} from filename {FileName}", episodeIndexFromFilename, fileName);
            return episodeIndexFromFilename;
        }

        if (episodeIndexFromFilename.Equals(episodeIndex))
        {
            log.LogInformation("use exists episode number {Index} for {FileName}", episodeIndex, fileName);
            return episodeIndex;
        }

        if (episodeIndex > max)
        {
            log.LogWarning("file {FileName} has incorrect episode index {Index} (max {Max}), set to {NewIndex}",
                fileName, episodeIndex, max, episodeIndexFromFilename);
            return episodeIndexFromFilename;
        }

        if (episodeIndexFromFilename > 0 && episodeIndex <= 0)
        {
            log.LogWarning("file {FileName} may has incorrect episode index {Index}, should be {NewIndex}",
                fileName, episodeIndex, episodeIndexFromFilename);
            return episodeIndexFromFilename;
        }

        log.LogInformation("use exists episode number {Index} from file name {FileName}", episodeIndex, fileName);
        return episodeIndex;
    }
}