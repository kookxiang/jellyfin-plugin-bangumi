using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;
using Jellyfin.Plugin.Bangumi.Parser.TorrentParser;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.Parser;

public static class EpisodeParserFactory
{
    public static IEpisodeParser CreateParser(PluginConfiguration config, EpisodeParserContext context, ILoggerFactory loggerFactory) =>
        config.EpisodeParser switch
        {
            EpisodeParserType.Basic => new BasicEpisodeParser(
                context, new Logger<BasicEpisodeParser>(loggerFactory.CreateLogger<BasicEpisodeParser>())),
            EpisodeParserType.AnitomySharp => new AnitomyEpisodeParser(
                context, new Logger<AnitomyEpisodeParser>(loggerFactory.CreateLogger<AnitomyEpisodeParser>())),
            EpisodeParserType.Torrent => new TorrentEpisodeParser(
                context, new Logger<TorrentEpisodeParser>(loggerFactory.CreateLogger<TorrentEpisodeParser>())),
            _ => throw new System.ArgumentOutOfRangeException(nameof(config), config.EpisodeParser, "Unknown episode parser"),
        };
}
