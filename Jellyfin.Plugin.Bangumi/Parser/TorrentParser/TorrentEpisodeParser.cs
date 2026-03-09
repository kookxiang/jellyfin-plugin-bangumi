using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;

namespace Jellyfin.Plugin.Bangumi.Parser.TorrentParser
{
    public partial class TorrentEpisodeParser : IEpisodeParser
    {
        private readonly EpisodeParserContext _context;
        private readonly Logger<TorrentEpisodeParser> _log;

        public TorrentEpisodeParser(EpisodeParserContext context, Logger<TorrentEpisodeParser> log)
        {
            _context = context;
            _log = log;
        }

        public Task<Episode?> GetEpisode()
        {
            throw new NotImplementedException();
        }

        public static double ExtractSeasonNumberFromPath(string path)
        {
            throw new NotImplementedException();
        }

        public static double ExtractEpisodeNumberFromPath(string path)
        {
            throw new NotImplementedException();
        }

        public static string ExtractBangumiTitleFromPath(string path)
        {
            throw new NotImplementedException();
        }

        public static EpisodeType ExtractEpisodeTypeFromPath(string path)
        {
            throw new NotImplementedException();
        }
    }
}
