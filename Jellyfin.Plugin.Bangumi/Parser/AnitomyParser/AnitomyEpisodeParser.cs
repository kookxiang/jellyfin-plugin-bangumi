
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
public class AnitomyEpisodeParser : IEpisodeParser
{
    private readonly EpisodeParserContext context;
    private readonly Logger<AnitomyEpisodeParser> log;
    private readonly string fileName;
    private readonly Anitomy anitomy;

    public AnitomyEpisodeParser(EpisodeParserContext parserContext, Logger<AnitomyEpisodeParser> logger)
    {
        context = parserContext;
        log = logger;
        fileName = Path.GetFileName(context.Info.Path);
        anitomy = new Anitomy(fileName);
    }

    public Task<Episode?> GetEpisode()
    {
        log.Info("AnitomyEpisodeParser.GetEpisode is still under development, context: {Context}", context);
        throw new NotImplementedException();
    }

}