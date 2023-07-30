using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.Parser.Basic
{
    public class BasicEpisodeParser : IEpisodeParser
    {
        private readonly BangumiApi _api;
        private readonly ILogger<BasicEpisodeParser> _log;
        private readonly ILibraryManager _libraryManager;
        private readonly PluginConfiguration _configuration;
        private readonly EpisodeInfo _info;
        private readonly LocalConfiguration _localConfiguration;
        private readonly CancellationToken _token;
        private readonly IFileSystem _fileSystem;


        public BasicEpisodeParser(BangumiApi api, ILoggerFactory loggerFactory, ILibraryManager libraryManager, PluginConfiguration Configuration, EpisodeInfo info, LocalConfiguration localConfiguration, CancellationToken token, IFileSystem fileSystem)
        {
            _api = api;
            _log = loggerFactory.CreateLogger<BasicEpisodeParser>();
            _libraryManager = libraryManager;
            _configuration = Configuration;
            _info = info;
            _localConfiguration = localConfiguration;
            _token = token;
            _fileSystem = fileSystem;
        }

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
            new(@"(\d{2,})")
        };

        private static readonly Regex OpeningEpisodeFileNameRegex = new(@"(NC)?OP([^a-zA-Z]|$)");
        private static readonly Regex EndingEpisodeFileNameRegex = new(@"(NC)?ED([^a-zA-Z]|$)");
        private static readonly Regex SpecialEpisodeFileNameRegex = new(@"(SPs?|Specials?|OVA|OAD)([^a-zA-Z]|$)");
        private static readonly Regex PreviewEpisodeFileNameRegex = new(@"[^\w]PV([^a-zA-Z]|$)");

        public static readonly Regex[] AllSpecialEpisodeFileNameRegex =
        {
            SpecialEpisodeFileNameRegex,
            PreviewEpisodeFileNameRegex,
            OpeningEpisodeFileNameRegex,
            EndingEpisodeFileNameRegex
        };


        public static bool IsSpecial(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var parentPath = Path.GetDirectoryName(filePath);
            var folderName = Path.GetFileName(parentPath);
            return SpecialEpisodeFileNameRegex.IsMatch(fileName) || SpecialEpisodeFileNameRegex.IsMatch(folderName ?? "");
        }

        public async Task<Model.Episode?> GetEpisode(int seriesId, double? episodeIndex)
        {
            var fileName = Path.GetFileName(_info.Path);
            // 剧集类型
            var type = IsSpecial(_info.Path) ? EpisodeType.Special : GuessEpisodeTypeFromFileName(fileName);

            if (int.TryParse(_info.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var episodeId))
            {
                var episode = await _api.GetEpisode(episodeId, _token);
                if (episode == null)
                    goto SkipBangumiId;

                if (episode.Type != EpisodeType.Normal || AllSpecialEpisodeFileNameRegex.Any(x => x.IsMatch(_info.Path)))
                    return episode;

                if (episode.ParentId == seriesId && Math.Abs(episode.Order - episodeIndex.Value) < 0.1)
                    return episode;
            }

        SkipBangumiId:
            var episodeListData = await _api.GetSubjectEpisodeList(seriesId, type, episodeIndex.Value, _token);
            if (episodeListData == null)
                return null;
            if (type is null or EpisodeType.Normal)
                episodeIndex = GuessEpisodeNumber(
                    episodeIndex + _localConfiguration.Offset,
                    fileName,
                    episodeListData.Max(x => x.Order) + _localConfiguration.Offset
                ) - _localConfiguration.Offset;
            try
            {
                var episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(episodeIndex));
                if (episode != null || type is null or EpisodeType.Normal)
                    return episode;
                _log.LogWarning("cannot find episode {index} with type {type}, searching all types", episodeIndex, type);
                type = null;
                goto SkipBangumiId;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
        public double? GetEpisodeIndex(string fileName, double? episodeIndex)
        {
            return GuessEpisodeNumber(episodeIndex, fileName);
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

            if (_configuration.AlwaysReplaceEpisodeNumber)
            {
                _log.LogWarning("use episode index {NewIndex} from filename {FileName}", episodeIndexFromFilename, fileName);
                return episodeIndexFromFilename;
            }

            if (episodeIndexFromFilename.Equals(episodeIndex))
            {
                _log.LogInformation("use exists episode number {Index} for {FileName}", episodeIndex, fileName);
                return episodeIndex;
            }

            if (episodeIndex > max)
            {
                _log.LogWarning("file {FileName} has incorrect episode index {Index} (max {Max}), set to {NewIndex}",
                    fileName, episodeIndex, max, episodeIndexFromFilename);
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

}
