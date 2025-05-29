using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;
using Jellyfin.Plugin.Bangumi.Parser.MixParser;

namespace Jellyfin.Plugin.Bangumi.Parser;
public static class EpisodeParserFactory
{
    public static IEpisodeParser CreateParser(
        PluginConfiguration config,
        EpisodeParserContext context,
        Logger<AnitomyEpisodeParser> anitomyLogger,
        Logger<BasicEpisodeParser> basicLogger,
        Logger<MixEpisodeParser> mixLogger)
    {
        return config.EpisodeParser switch
        {
            EpisodeParserType.Basic => new BasicEpisodeParser(context, basicLogger),
            EpisodeParserType.AnitomySharp => new AnitomyEpisodeParser(context, anitomyLogger),
            EpisodeParserType.Mix => new MixEpisodeParser(context, mixLogger),
            _ => throw new System.NotImplementedException(),
        };
    }
}
