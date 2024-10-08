using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;
using Episode = Jellyfin.Plugin.Bangumi.Model.Episode;
using MediaBrowser.Model.IO;

namespace Jellyfin.Plugin.Bangumi.Parser.BasicParser;
public partial class BasicEpisodeParser : IEpisodeParser
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;
    private readonly BangumiApi _api;
    private readonly ILogger<BasicEpisodeParser> _log;
    private readonly ILibraryManager _libraryManager;
    private readonly EpisodeInfo _info;
    private readonly CancellationToken _token;
    private readonly IFileSystem _fileSystem;


    public BasicEpisodeParser(BangumiApi api, EpisodeInfo info, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IFileSystem fileSystem, CancellationToken token)
    {
        _api = api;
        _log = loggerFactory.CreateLogger<BasicEpisodeParser>();
        _libraryManager = libraryManager;
        _info = info;
        _token = token;
        _fileSystem = fileSystem;
    }

    public async Task<Episode?> GetEpisode()
    {

        var localConfiguration = await LocalConfiguration.ForPath(_info.Path);

        var fileName = Path.GetFileName(_info.Path);
        if (string.IsNullOrEmpty(fileName))
            return null;

        var type = IsSpecial(_info.Path) ? EpisodeType.Special : GuessEpisodeTypeFromFileName(fileName);
        var seriesId = localConfiguration.Id;

        var parent = _libraryManager.FindByPath(Path.GetDirectoryName(_info.Path)!, true);
        if (parent is Season)
            if (int.TryParse(parent.ProviderIds.GetValueOrDefault(Constants.ProviderName), out var seasonId))
            {
                _log.LogInformation("used session id {SeasonId} from parent", seasonId);
                seriesId = seasonId;
            }

        if (seriesId == 0)
            if (!int.TryParse(_info.SeriesProviderIds?.GetValueOrDefault(Constants.ProviderName), out seriesId))
                return null;

        if (localConfiguration.Id != 0)
        {
            _log.LogInformation("used session id {SeasonId} from local configuration", localConfiguration.Id);
            seriesId = localConfiguration.Id;
        }

        double? episodeIndex = _info.IndexNumber;

        if (Configuration.AlwaysReplaceEpisodeNumber)
        {
            _log.LogInformation("guess episode number from filename {FileName} because of plugin configuration", fileName);
            episodeIndex = GuessEpisodeNumber(episodeIndex, fileName);
        }
        else if (episodeIndex is null or 0)
        {
            _log.LogInformation("guess episode number from filename {FileName} because it's empty", fileName);
            episodeIndex = GuessEpisodeNumber(episodeIndex, fileName);
        }

        if (localConfiguration.Offset != 0)
        {
            _log.LogInformation("applying offset {Offset} to episode index {EpisodeIndex}", -localConfiguration.Offset, episodeIndex);
            episodeIndex -= localConfiguration.Offset;
        }

        if (int.TryParse(_info.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var episodeId))
        {
            _log.LogInformation("fetching episode info using saved id: {EpisodeId}", episodeId);
            var episode = await _api.GetEpisode(episodeId, _token);
            if (episode == null)
                goto SkipBangumiId;

            if (Configuration.TrustExistedBangumiId)
            {
                _log.LogInformation("trust exists bangumi id is enabled, skip further checks");
                return episode;
            }

            if (episode.Type != EpisodeType.Normal || AllSpecialEpisodeFileNameRegex.Any(x => x.IsMatch(_info.Path)))
            {
                _log.LogInformation("current episode is special episode, skip further checks");
                return episode;
            }

            if (episode.ParentId == seriesId && Math.Abs(episode.Order - episodeIndex.Value) < 0.1)
                return episode;

            _log.LogInformation("episode is not belongs to series {SeriesId}, ignoring result", seriesId);
        }

    SkipBangumiId:
        _log.LogInformation("searching episode in series episode list");
        var episodeListData = await _api.GetSubjectEpisodeList(seriesId, type, episodeIndex.Value, _token);
        if (episodeListData == null)
        {
            _log.LogWarning("search failed: no episode found in episode");
            return null;
        }

        if (episodeListData.Count == 1 && type is null or EpisodeType.Normal)
        {
            _log.LogInformation("only one episode found");
            return episodeListData.First();
        }

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
            {
                _log.LogInformation("found matching episode {index} with type {type}", episodeIndex, type);
                return episode;
            }

            _log.LogWarning("cannot find episode {index} with type {type}, searching all types", episodeIndex, type);
            type = null;
            goto SkipBangumiId;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public Task<object?> GetEpisodeProperty(EpisodeProperty episodeProperty)
    {
        switch (episodeProperty)
        {
            case EpisodeProperty.Index:
                return null;
            default:
                return null;
        }
    }


    private static readonly Regex[] NonEpisodeFileNameRegex =
    [
        new(@"[\[\(][0-9A-F]{8}[\]\)]", RegexOptions.IgnoreCase),
        new(@"S\d{2,}", RegexOptions.IgnoreCase),
        new(@"yuv[4|2|0]{3}p(10|8)?", RegexOptions.IgnoreCase),
        new(@"\d{3,4}p", RegexOptions.IgnoreCase),
        new(@"\d{3,4}x\d{3,4}", RegexOptions.IgnoreCase),
        new(@"(Hi)?10p", RegexOptions.IgnoreCase),
        new(@"(8|10)bit", RegexOptions.IgnoreCase),
        new Regex(@"(x|h)(264|265)", RegexOptions.IgnoreCase),
        new Regex(@"\[\d{2}(0[1-9]|1[0-2])(0[1-9]|1[0-9]|2[0-9]|3[0-1])]"),
        new Regex(@"(?<=[^P])V\d+")
    ];

    private static readonly Regex[] EpisodeFileNameRegex =
    [
        new(@"\[([\d\.]{2,})\]"),
        new(@"- ?([\d\.]{2,})"),
        new(@"EP?([\d\.]{2,})", RegexOptions.IgnoreCase),
        new(@"\[([\d\.]{2,})"),
        new(@"#([\d\.]{2,})"),
        new(@"(\d{2,})"),
        new(@"\[([\d\.]+)\]")
    ];

    private static readonly Regex[] AllSpecialEpisodeFileNameRegex =
    [
        SpecialEpisodeFileNameRegex(),
        PreviewEpisodeFileNameRegex(),
        OpeningEpisodeFileNameRegex(),
        EndingEpisodeFileNameRegex()
    ];

    [GeneratedRegex(@"(NC)?OP([^a-zA-Z]|$)")]
    private static partial Regex OpeningEpisodeFileNameRegex();

    [GeneratedRegex(@"(NC)?ED([^a-zA-Z]|$)")]
    private static partial Regex EndingEpisodeFileNameRegex();

    [GeneratedRegex(@"(SPs?|Specials?|OVA|OAD)([^a-zA-Z]|$)")]
    private static partial Regex SpecialEpisodeFileNameRegex();

    [GeneratedRegex(@"[^\w]PV([^a-zA-Z]|$)")]
    private static partial Regex PreviewEpisodeFileNameRegex();

    public static bool IsSpecial(string filePath, bool checkParent = true)
    {
        var fileName = Path.GetFileName(filePath);
        var parentPath = Path.GetDirectoryName(filePath);
        var folderName = Path.GetFileName(parentPath);
        return SpecialEpisodeFileNameRegex().IsMatch(fileName) ||
               (checkParent && SpecialEpisodeFileNameRegex().IsMatch(folderName ?? ""));
    }



    private static EpisodeType? GuessEpisodeTypeFromFileName(string fileName)
    {
        var tempName = fileName;
        foreach (var regex in NonEpisodeFileNameRegex)
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
                _log.LogInformation("used episode number {index} from anitomy", anitomyIndex);
                return double.Parse(anitomyIndex);
            }
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
            _log.LogInformation("used episode number {index} from filename because it matches {pattern}", index, regex);
            break;
        }

        if (Configuration.AlwaysReplaceEpisodeNumber)
        {
            _log.LogWarning("use episode number {NewIndex} from filename {FileName}", episodeIndexFromFilename, fileName);
            return episodeIndexFromFilename;
        }

        if (episodeIndexFromFilename.Equals(episodeIndex))
        {
            _log.LogInformation("use exists episode number {Index} because it's same", episodeIndex);
            return episodeIndex;
        }

        if (episodeIndex > max)
        {
            _log.LogWarning("{FileName} has incorrect episode index {Index} (max {Max}), set to {NewIndex}",
                fileName, episodeIndex, max, episodeIndexFromFilename);
            return episodeIndexFromFilename;
        }

        if (episodeIndexFromFilename > 0 && episodeIndex <= 0)
        {
            _log.LogWarning("{FileName} may has incorrect episode index {Index}, should be {NewIndex}",
                fileName, episodeIndex, episodeIndexFromFilename);
            return episodeIndexFromFilename;
        }

        _log.LogInformation("use exists episode number {Index}", episodeIndex);
        return episodeIndex;
    }

}

