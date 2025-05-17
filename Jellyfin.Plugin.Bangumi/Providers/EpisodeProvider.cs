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
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Providers;

public partial class EpisodeProvider(BangumiApi api, Logger<EpisodeProvider> log, ILibraryManager libraryManager)
    : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{
    private static readonly Regex[] _nonEpisodeFileNameRegex =
    [
        new(@"[\[\(][0-9A-F]{8}[\]\)]", RegexOptions.IgnoreCase),
        new(@"S\d{2,}", RegexOptions.IgnoreCase),
        new(@"yuv[4|2|0]{3}p(10|8)?", RegexOptions.IgnoreCase),
        new(@"\d{3,4}p", RegexOptions.IgnoreCase),
        new(@"\d{3,4}x\d{3,4}", RegexOptions.IgnoreCase),
        new(@"(Hi)?10p", RegexOptions.IgnoreCase),
        new(@"(8|10)bit", RegexOptions.IgnoreCase),
        new(@"(x|h)(264|265)", RegexOptions.IgnoreCase),
        new(@"\d{2,}FPS", RegexOptions.IgnoreCase),
        new(@"\[\d{2}(0[1-9]|1[0-2])(0[1-9]|1[0-9]|2[0-9]|3[0-1])]"),
        new(@"(?<=[^P])V\d+")
    ];

    private static readonly Regex[] _episodeFileNameRegex =
    [
        new(@"\[([\d\.]{2,})\]"),
        new(@"- ?([\d\.]{2,})"),
        new(@"EP?([\d\.]{2,})", RegexOptions.IgnoreCase),
        new(@"第(\d+)巻"),
        new(@"\[([\d\.]{2,})"),
        new(@"#([\d\.]{2,})"),
        new(@"(\d{2,})"),
        new(@"\[([\d\.]+)\]")
    ];

    private static readonly Regex[] _allSpecialEpisodeFileNameRegex =
    [
        SpecialEpisodeFileNameRegex(),
        PreviewEpisodeFileNameRegex(),
        OpeningEpisodeFileNameRegex(),
        EndingEpisodeFileNameRegex()
    ];

    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var localConfiguration = await LocalConfiguration.ForPath(info.Path);
        var episode = await GetEpisode(info, localConfiguration, cancellationToken);

        log.Info("metadata for {FilePath}: {EpisodeInfo}", Path.GetFileName(info.Path), episode);

        var result = new MetadataResult<Episode> { ResultLanguage = Constants.Language };

        if (episode == null)
        {
            // remove season number
            if (IsSpecial(info.Path, true))
            {
                result.HasMetadata = true;
                result.Item = new Episode
                {
                    ParentIndexNumber = 0
                };
            }

            return result;
        }

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

        var parent = libraryManager.FindByPath(Path.GetDirectoryName(info.Path)!, true);
        if (IsSpecial(info.Path, true) || episode.Type == EpisodeType.Special || info.ParentIndexNumber == 0)
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
        var series = await api.GetSubject(episode.ParentId, cancellationToken);
        if (series == null)
            return result;

        // use title from special episode subject if episode data is empty
        if (string.IsNullOrEmpty(result.Item.Name))
            result.Item.Name = series.Name;
        if (string.IsNullOrEmpty(result.Item.OriginalTitle))
            result.Item.OriginalTitle = series.OriginalName;

        var seasonNumber = parent is Season ? parent.IndexNumber : 1;
        if (!string.IsNullOrEmpty(episode.AirDate) && string.Compare(episode.AirDate, series.AirDate, StringComparison.Ordinal) < 0)
            result.Item.AirsBeforeEpisodeNumber = seasonNumber;
        else
            result.Item.AirsAfterSeasonNumber = seasonNumber;

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return api.GetHttpClient().GetAsync(url, cancellationToken);
    }

    [GeneratedRegex(@"(NC)?OP([^a-zA-Z]|$)")]
    private static partial Regex OpeningEpisodeFileNameRegex();

    [GeneratedRegex(@"(NC)?ED([^a-zA-Z]|$)")]
    private static partial Regex EndingEpisodeFileNameRegex();

    [GeneratedRegex(@"(SPs?|Specials?|OVA|OAD)([^a-zA-Z]|$)")]
    private static partial Regex SpecialEpisodeFileNameRegex();

    [GeneratedRegex(@"[^\w]PV([^a-zA-Z]|$)")]
    private static partial Regex PreviewEpisodeFileNameRegex();

    private bool IsSpecial(string filePath, bool checkParent = true)
    {
        var fileName = Path.GetFileName(filePath);
        var parentPath = Path.GetDirectoryName(filePath);
        var folderName = Path.GetFileName(parentPath);

        if (checkParent)
        {
            if (parentPath == null)
            {
                checkParent = false;
            }
            else
            {
                // check if parent is a season(subfolder), otherwise it is a series(root folder), check on root folder is not needed
                checkParent = libraryManager.FindByPath(parentPath, true) is Season;
            }
        }

        return SpecialEpisodeFileNameRegex().IsMatch(fileName) ||
               checkParent && SpecialEpisodeFileNameRegex().IsMatch(folderName ?? "");
    }

    private async Task<Model.Episode?> GetEpisode(EpisodeInfo info, LocalConfiguration localConfiguration, CancellationToken token)
    {
        var fileName = Path.GetFileName(info.Path);
        if (string.IsNullOrEmpty(fileName))
            return null;

        var type = IsSpecial(info.Path) ? EpisodeType.Special : GuessEpisodeTypeFromFileName(fileName);
        var seriesId = localConfiguration.Id;

        var parent = libraryManager.FindByPath(Path.GetDirectoryName(info.Path)!, true);
        if (parent is Season)
            if (int.TryParse(parent.ProviderIds.GetValueOrDefault(Constants.ProviderName), out var seasonId))
            {
                log.Info("used session id {SeasonId} from parent", seasonId);
                seriesId = seasonId;
            }

        if (seriesId == 0)
            if (!int.TryParse(info.SeriesProviderIds?.GetValueOrDefault(Constants.ProviderName), out seriesId))
                return null;

        if (localConfiguration.Id != 0)
        {
            log.Info("used session id {SeasonId} from local configuration", localConfiguration.Id);
            seriesId = localConfiguration.Id;
        }

        double? episodeIndex = info.IndexNumber;

        if (Configuration.AlwaysReplaceEpisodeNumber)
        {
            log.Info("guess episode number from filename {FileName} because of plugin configuration", fileName);
            episodeIndex = GuessEpisodeNumber(episodeIndex, fileName);
        }
        else if (episodeIndex is null or 0)
        {
            log.Info("guess episode number from filename {FileName} because it's empty", fileName);
            episodeIndex = GuessEpisodeNumber(episodeIndex, fileName);
        }

        if (localConfiguration.Offset != 0)
        {
            log.Info("applying offset {Offset} to episode index {EpisodeIndex}", -localConfiguration.Offset, episodeIndex);
            episodeIndex -= localConfiguration.Offset;
        }

        if (int.TryParse(info.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var episodeId))
        {
            log.Info("fetching episode info using saved id: {EpisodeId}", episodeId);

            var episode = await api.GetEpisode(episodeId, token);

            // return if episode still not found
            if (episode == null)
                goto SkipBangumiId;

            if (Configuration.TrustExistedBangumiId)
            {
                log.Info("trust exists bangumi id is enabled, skip further checks");
                return episode;
            }

            if (episode.Type != EpisodeType.Normal || _allSpecialEpisodeFileNameRegex.Any(x => x.IsMatch(info.Path)))
            {
                log.Info("current episode is special episode, skip further checks");
                return episode;
            }

            if (episode.ParentId == seriesId && Math.Abs(episode.Order - episodeIndex.Value) < 0.1)
                return episode;

            log.Info("episode is not belongs to series {SeriesId}, ignoring result", seriesId);
        }

SkipBangumiId:
        log.Info("searching episode in series episode list");
        var episodeListData = await api.GetSubjectEpisodeList(seriesId, type, episodeIndex.Value, token);

        if (episodeListData == null)
        {
            log.Warn("search failed: no episode found in episode");
            return null;
        }

        if (episodeListData.Count() == 1 && type is null or EpisodeType.Normal)
        {
            log.Info("only one episode found");
            return episodeListData.First();
        }

        if (type is null or EpisodeType.Normal)
        {
            var maxEpisodeNumber = episodeListData.Any() ? episodeListData.Max(x => x.Order) : double.PositiveInfinity;
            episodeIndex = GuessEpisodeNumber(
                episodeIndex + localConfiguration.Offset,
                fileName,
                maxEpisodeNumber + localConfiguration.Offset
            ) - localConfiguration.Offset;
        }

        try
        {
            var episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(episodeIndex));
            if (episode != null || type is null or EpisodeType.Normal)
            {
                log.Info("found matching episode {index} with type {type}", episodeIndex, type);
                return episode;
            }

            log.Warn("cannot find episode {index} with type {type}, searching all types", episodeIndex, type);
            type = null;
            goto SkipBangumiId;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static EpisodeType? GuessEpisodeTypeFromFileName(string fileName)
    {
        var tempName = fileName;
        foreach (var regex in _nonEpisodeFileNameRegex)
        {
            if (!regex.IsMatch(tempName))
                continue;
            tempName = regex.Replace(tempName, "");
        }

        if (OpeningEpisodeFileNameRegex().IsMatch(tempName))
            return EpisodeType.Opening;
        if (EndingEpisodeFileNameRegex().IsMatch(tempName))
            return EpisodeType.Ending;
        if (SpecialEpisodeFileNameRegex().IsMatch(tempName))
            return EpisodeType.Special;
        if (PreviewEpisodeFileNameRegex().IsMatch(tempName))
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
            var anitomy = new Anitomy(fileName);
            var anitomyIndex = anitomy.ExtractEpisodeNumber();
            if (!string.IsNullOrEmpty(anitomyIndex))
            {
                log.Info("used episode number {index} from anitomy", anitomyIndex);
                return double.Parse(anitomyIndex);
            }
        }

        foreach (var regex in _nonEpisodeFileNameRegex)
        {
            if (!regex.IsMatch(tempName))
                continue;
            tempName = regex.Replace(tempName, "");
        }

        foreach (var regex in _episodeFileNameRegex)
        {
            if (!regex.IsMatch(tempName))
                continue;
            if (!double.TryParse(regex.Match(tempName).Groups[1].Value.Trim('.'), out var index))
                continue;
            episodeIndexFromFilename = index;
            log.Info("used episode number {index} from filename because it matches {pattern}", index, regex);
            break;
        }

        if (Configuration.AlwaysReplaceEpisodeNumber)
        {
            log.Warn("use episode number {NewIndex} from filename {FileName}", episodeIndexFromFilename, fileName);
            return episodeIndexFromFilename;
        }

        if (episodeIndexFromFilename.Equals(episodeIndex))
        {
            log.Info("use exists episode number {Index} because it's same", episodeIndex);
            return episodeIndex;
        }

        if (episodeIndex > max)
        {
            log.Warn("{FileName} has incorrect episode index {Index} (max {Max}), set to {NewIndex}", fileName, episodeIndex, max, episodeIndexFromFilename);
            return episodeIndexFromFilename;
        }

        if (episodeIndexFromFilename > 0 && episodeIndex <= 0)
        {
            log.Warn("{FileName} may has incorrect episode index {Index}, should be {NewIndex}", fileName, episodeIndex, episodeIndexFromFilename);
            return episodeIndexFromFilename;
        }

        log.Info("use exists episode number {Index}", episodeIndex);
        return episodeIndex;
    }
}
