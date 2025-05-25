using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;

namespace Jellyfin.Plugin.Bangumi.Parser.MixParser
{
    public partial class MixEpisodeParser(EpisodeParserContext context, Logger<MixEpisodeParser> log) : IEpisodeParser
    {
        public Task<Episode?> GetEpisode()
        {
            log.Info("AnitomyEpisodeParser.GetEpisode is still under development, context: {Context}", context);
            throw new NotImplementedException();
        }

        public static double? ExtractSeasonNumberFromPath<T>(EpisodeParserContext context, Logger<T> log)
        {
            return AnitomyEpisodeParser.ExtractSeasonNumberFromPath(context, log);
        }

        public static double? ExtractEpisodeNumberFromPath<T>(EpisodeParserContext context, Logger<T> log)
        {
            var num = AnitomyEpisodeParser.ExtractEpisodeNumberFromPath(context, log);
            if (num != null)
            {
                return num;
            }

            return BasicEpisodeParser.ExtractEpisodeNumberFromPath(context, log);
        }

        public static string? ExtractAnimeTitleFromPath<T>(EpisodeParserContext context, Logger<T> log)
        {
            return AnitomyEpisodeParser.ExtractAnimeTitleFromPath(context, log);
        }

        public static EpisodeType? ExtractEpisodeTypeFromPath<T>(EpisodeParserContext context, Logger<T> log)
        {
            var type = AnitomyEpisodeParser.ExtractEpisodeTypeFromPath(context, log);
            if (type != null)
            {
                return type;
            }

            return BasicEpisodeParser.ExtractEpisodeTypeFromPath(context, log);
        }
    }
}
