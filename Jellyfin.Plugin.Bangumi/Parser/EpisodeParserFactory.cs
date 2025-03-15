using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;

namespace Jellyfin.Plugin.Bangumi.Parser;
public static class EpisodeParserFactory
{
    public static IEpisodeParser CreateParser(
        PluginConfiguration config,
        EpisodeParserContext context,
        Logger<AnitomyEpisodeParser> anitomyLogger,
        Logger<BasicEpisodeParser> basicLogger)
    {
        return config.EpisodeParser switch
        {
            EpisodeParserType.Basic => new BasicEpisodeParser(context, basicLogger),
            EpisodeParserType.AnitomySharp => new AnitomyEpisodeParser(context, anitomyLogger),
            _ => throw new System.NotImplementedException(),
        };
    }
}