using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Bangumi.Parser.BasicParser;
public partial class BasicEpisodeParser(EpisodeParserContext context, Logger<BasicEpisodeParser> log) : IEpisodeParser
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
        new(@"\[([\d\.]+)\]"),
        new(@"^(\d+(\.\d+)?)\.[a-zA-Z]+$")
    ];

    private static readonly Regex[] _allSpecialEpisodeFileNameRegex =
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

    public static bool IsSpecial(string filePath, ILibraryManager libraryManager, bool checkParent = true)
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

    public static int GetSubjectId<T>(EpisodeParserContext context, Logger<T> log)
    {
        // 从本地配置获取
        var subjectId = context.LocalConfiguration.Id;
        if (subjectId != 0)
        {
            log.Info("get subject id {Id} from local configuration", subjectId);
            return subjectId;
        }

        // 从Season目录获取
        var parent = context.LibraryManager.FindByPath(Path.GetDirectoryName(context.Info.Path)!, true);
        if (parent is Season)
        {
            if (int.TryParse(parent.ProviderIds.GetValueOrDefault(Constants.ProviderName), out var seasonId))
            {
                log.Info("get subject id {Id} from parent", seasonId);
                return seasonId;
            }
        }

        // 从Series目录获取
        if (int.TryParse(context.Info.SeriesProviderIds?.GetValueOrDefault(Constants.ProviderName), out var seriesId))
        {
            log.Info("get subject id {Id} from series provider ids", seriesId);
            return seriesId;
        }

        log.Warn("cannot find subject id from context, return 0");
        return 0;
    }

    public static async Task<Model.Episode?> GetEpisodeFromProviderId<T>(EpisodeParserContext context, Logger<T> log, int subjectId, double episodeIndex)
    {
        if (int.TryParse(context.Info.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var episodeId))
        {
            log.Info("fetching episode info using saved id: {EpisodeId}", episodeId);

            var episode = await context.Api.GetEpisode(episodeId, context.Token);

            // return if episode still not found
            if (episode == null)
                return null;

            if (context.Configuration.TrustExistedBangumiId)
            {
                log.Info("trust exists bangumi id is enabled, skip further checks");
                return episode;
            }

            if (episode.Type != EpisodeType.Normal || _allSpecialEpisodeFileNameRegex.Any(x => x.IsMatch(context.Info.Path)))
            {
                log.Info("current episode is special episode, skip further checks");
                return episode;
            }

            if (episode.ParentId == subjectId && Math.Abs(episode.Order - episodeIndex) < 0.1)
                return episode;

            log.Info("episode is not belongs to series {SeriesId}, ignoring result", subjectId);
        }

        return null;
    }

    public static async Task<Model.Episode?> SearchEpisodes<T>(EpisodeParserContext context, Logger<T> log, EpisodeType? type, int subjectId, double episodeIndex)
    {
        var fileName = Path.GetFileName(context.Info.Path);

        log.Info("searching episode in series episode list");
        var episodeListData = await context.Api.GetSubjectEpisodeList(subjectId, type, episodeIndex, context.Token);

        // 部分OVA独立一个条目页面，没有区分正篇或特典剧集
        if (episodeListData != null
            && !episodeListData.Any()
            && type == EpisodeType.Special)
        {
            var subject = await context.Api.GetSubject(subjectId, context.Token);
            if (subject != null &&
                (subject.Platform == SubjectPlatform.OVA || subject.GenreTags.Contains("OVA")))
            {
                episodeListData = await context.Api.GetSubjectEpisodeList(subjectId, EpisodeType.Normal, episodeIndex, context.Token);
            }
        }

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
                context,
                log,
                episodeIndex + context.LocalConfiguration.Offset,
                fileName,
                maxEpisodeNumber + context.LocalConfiguration.Offset
            ) - context.LocalConfiguration.Offset;
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
            return await SearchEpisodes(context, log, null, subjectId, episodeIndex);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async Task<Model.Episode?> GetEpisode()
    {
        Model.Episode? result = null;

        var fileName = Path.GetFileName(context.Info.Path);
        var type = IsSpecial(context.Info.Path, context.LibraryManager) ? EpisodeType.Special : GuessEpisodeTypeFromFileName(fileName);
        if (type == EpisodeType.Special)
        {
            result = new Model.Episode()
            {
                ParentIndexNumber = 0,
            };
        }

        if (string.IsNullOrEmpty(fileName))
            return result;

        var subjectId = GetSubjectId(context, log);
        var episodeIndexNumber = ExtractEpisodeNumberFromPath(context, log) ?? 0;

        result = await GetEpisodeFromProviderId(context, log, subjectId, episodeIndexNumber)
            ?? await SearchEpisodes(context, log, type, subjectId, episodeIndexNumber);
        if (result != null)
        {
            if (type == EpisodeType.Special || result.Type == EpisodeType.Special)
            {
                result.ParentIndexNumber = 0;
            }
        }

        return result;
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

    private static double GuessEpisodeNumber<T>(EpisodeParserContext context, Logger<T> log, double? current, string fileName, double max = double.PositiveInfinity)
    {
        var tempName = fileName;
        var episodeIndex = current ?? 0;
        var episodeIndexFromFilename = episodeIndex;

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

        if (context.Configuration.AlwaysReplaceEpisodeNumber)
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

    public static double? ExtractSeasonNumberFromPath<T>(EpisodeParserContext context, Logger<T> log)
    {
        throw new NotImplementedException();
    }

    public static double? ExtractEpisodeNumberFromPath<T>(EpisodeParserContext context, Logger<T> log)
    {
        var fileName = Path.GetFileName(context.Info.Path);
        double episodeIndexNumber = context.Info.IndexNumber ?? 0;

        if (context.Configuration.AlwaysReplaceEpisodeNumber)
        {
            log.Info("guess episode number from filename {FileName} because of plugin configuration", fileName);
            episodeIndexNumber = GuessEpisodeNumber(context, log, episodeIndexNumber, fileName);
        }
        else if (episodeIndexNumber == 0)
        {
            log.Info("guess episode number from filename {FileName} because it's empty", fileName);
            episodeIndexNumber = GuessEpisodeNumber(context, log, episodeIndexNumber, fileName);
        }

        if (context.LocalConfiguration.Offset != 0)
        {
            log.Info("applying offset {Offset} to episode index {EpisodeIndex}", -context.LocalConfiguration.Offset, episodeIndexNumber);
            episodeIndexNumber -= context.LocalConfiguration.Offset;
        }

        return episodeIndexNumber;
    }

    public static string? ExtractAnimeTitleFromPath<T>(EpisodeParserContext context, Logger<T> log)
    {
        throw new NotImplementedException();
    }

    public static EpisodeType? ExtractEpisodeTypeFromPath<T>(EpisodeParserContext context, Logger<T> log)
    {
        return GuessEpisodeTypeFromFileName(Path.GetFileName(context.Info.Path)) ?? EpisodeType.Normal;
    }
}
