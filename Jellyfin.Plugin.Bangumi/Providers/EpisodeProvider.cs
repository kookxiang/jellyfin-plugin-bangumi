using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Utils;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class EpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{
    private static readonly Regex[] NonEpisodeFileNameRegex =
    {
        new(@"S\d{2,}", RegexOptions.IgnoreCase),
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
        new(@"([\d\.]{2,})")
    };

    private static readonly Regex OpeningEpisodeFileNameRegex = new(@"(NC)?OP\d");
    private static readonly Regex EndingEpisodeFileNameRegex = new(@"(NC)?ED\d");
    private static readonly Regex SpecialEpisodeFileNameRegex = new(@"[^\w](SP|OVA|OAD)\d*[^\w]");
    private static readonly Regex PreviewEpisodeFileNameRegex = new(@"[^\w]PV\d*[^\w]");

    private static readonly Regex[] AllSpecialEpisodeFileNameRegex =
    {
        SpecialEpisodeFileNameRegex,
        PreviewEpisodeFileNameRegex,
        OpeningEpisodeFileNameRegex,
        EndingEpisodeFileNameRegex
    };

    private readonly BangumiApi _api;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<EpisodeProvider> _log;

    private readonly Plugin _plugin;

    public EpisodeProvider(Plugin plugin, BangumiApi api, ILogger<EpisodeProvider> log, ILibraryManager libraryManager)
    {
        _plugin = plugin;
        _api = api;
        _log = log;
        _libraryManager = libraryManager;
    }

    public int Order => -5;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        EpisodeType? type = null;
        Model.Episode? episode = null;
        var result = new MetadataResult<Episode> { ResultLanguage = Constants.Language };

        var fileName = Path.GetFileName(info.Path);
        if (string.IsNullOrEmpty(fileName))
            return result;

        if (OpeningEpisodeFileNameRegex.IsMatch(fileName))
            type = EpisodeType.Opening;
        else if (EndingEpisodeFileNameRegex.IsMatch(fileName))
            type = EpisodeType.Ending;
        else if (SpecialEpisodeFileNameRegex.IsMatch(fileName))
            type = EpisodeType.Special;
        else if (PreviewEpisodeFileNameRegex.IsMatch(fileName))
            type = EpisodeType.Preview;

        var seriesId = info.SeriesProviderIds?.GetValueOrDefault(Constants.ProviderName);

        var parent = _libraryManager.FindByPath(Path.GetDirectoryName(info.Path), true);
        if (parent is Season)
        {
            var seasonId = parent.ProviderIds.GetValueOrDefault(Constants.ProviderName);
            if (!string.IsNullOrEmpty(seasonId))
                seriesId = seasonId;
        }

        if (string.IsNullOrEmpty(seriesId))
            return result;

        var episodeId = info.ProviderIds?.GetValueOrDefault(Constants.ProviderName);
        if (!string.IsNullOrEmpty(episodeId))
        {
            episode = await _api.GetEpisode(episodeId, token);
            if (episode != null)
                if (episode.Type == EpisodeType.Normal && !AllSpecialEpisodeFileNameRegex.Any(x => x.IsMatch(info.Path)))
                    if ($"{episode.ParentId}" != seriesId)
                    {
                        _log.LogWarning("episode #{Episode} is not belong to series #{Series}, ignored", episodeId, seriesId);
                        episode = null;
                    }
        }

        double? episodeIndex = info.IndexNumber;

        if (_plugin.Configuration.AlwaysReplaceEpisodeNumber)
        {
            episodeIndex = GuessEpisodeNumber(episodeIndex, fileName);
            if ((int)episodeIndex != info.IndexNumber)
                episode = null;
        }

        episodeIndex ??= GuessEpisodeNumber(episodeIndex, fileName);

        if (episode == null)
        {
            var episodeListData = await _api.GetSubjectEpisodeList(seriesId, type, episodeIndex.Value, token);
            if (episodeListData == null)
                return result;
            if (type is null or EpisodeType.Normal)
                episodeIndex = GuessEpisodeNumber(episodeIndex, fileName, episodeListData.Max(x => x.Order));
            try
            {
                episode = episodeListData.OrderBy(x => x.Type).First(x => x.Order.Equals(episodeIndex));
            }
            catch (InvalidOperationException)
            {
                return result;
            }
        }

        result.Item = new Episode();
        result.HasMetadata = true;
        result.Item.ProviderIds.Add(Constants.ProviderName, $"{episode.Id}");
        if (!string.IsNullOrEmpty(episode.AirDate))
        {
            result.Item.PremiereDate = DateTime.Parse(episode.AirDate);
            result.Item.ProductionYear = DateTime.Parse(episode.AirDate).Year;
        }

        result.Item.Name = episode.GetName(_plugin.Configuration);
        result.Item.OriginalTitle = episode.OriginalName;
        result.Item.IndexNumber = (int)episode.Order;
        result.Item.Overview = episode.Description;
        result.Item.ParentIndexNumber = 1;

        if (parent is Season season)
        {
            result.Item.SeasonId = season.Id;
            result.Item.ParentIndexNumber = season.IndexNumber;
        }

        if (episode.Type == EpisodeType.Normal)
            return result;

        // mark episode as special
        result.Item.ParentIndexNumber = 0;

        var series = await _api.GetSubject(episode.ParentId, token);
        if (series == null)
            return result;

        var seasonNumber = parent is Season ? parent.IndexNumber : 1;
        if (string.Compare(episode.AirDate, series.AirDate, StringComparison.Ordinal) < 0)
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
        return _plugin.GetHttpClient().GetAsync(url, token);
    }

    private double GuessEpisodeNumber(double? current, string fileName, double max = double.PositiveInfinity)
    {
        var tempName = fileName;
        var episodeIndex = current ?? 0;
        var episodeIndexFromFilename = episodeIndex;

        // 临时测试，待改造 #TODO
        if (_plugin.Configuration.AlwaysGetEpisodeByAnitomySharp) return double.Parse(AnitomyHelper.ExtractEpisodeNumber(fileName));

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
            episodeIndexFromFilename = double.Parse(regex.Match(tempName).Groups[1].Value);
            break;
        }

        if (_plugin.Configuration.AlwaysReplaceEpisodeNumber && episodeIndexFromFilename != episodeIndex)
        {
            _log.LogWarning("use episode index {NewIndex} instead of {Index} for {FileName}",
                episodeIndexFromFilename, episodeIndex, fileName);
            return episodeIndexFromFilename;
        }

        if (episodeIndex > max)
        {
            _log.LogWarning("file {FileName} has incorrect episode index {Index}, set to {NewIndex}",
                fileName, episodeIndex, episodeIndexFromFilename);
            return episodeIndexFromFilename;
        }

        if (episodeIndexFromFilename > 0 && episodeIndex <= 0)
        {
            _log.LogWarning("file {FileName} may has incorrect episode index {Index}, should be {NewIndex}",
                fileName, episodeIndex, episodeIndexFromFilename);
            return episodeIndexFromFilename;
        }

        _log.LogInformation("use exists episode number {Index} from file name {FileName}", episodeIndex, fileName);
        return episodeIndex;
    }
}