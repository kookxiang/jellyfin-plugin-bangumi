
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;

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

    public static double? ExtractSeasonNumberFromPath<T>(EpisodeParserContext context, Logger<T> log)
    {
        string[] names = IEpisodeParser.SplitFilePathParts(context);
        if (names.Length < 2)
        {
            log.Error("Failed to extract season number from path: {Path}", context.Info.Path);
            return null;
        }

        foreach (var name in names)
        {
            var anitomy = new Anitomy(name);
            if (double.TryParse(anitomy.ExtractAnimeSeason(), out double num))
            {
                return num;
            }
        }
        return null;
    }

    public static double? ExtractEpisodeNumberFromPath<T>(EpisodeParserContext context, Logger<T> log)
    {
        var path = context.Info.Path;
        var filename = Path.GetFileName(path);

        var anitomy = new Anitomy(filename);
        if (double.TryParse(anitomy.ExtractEpisodeNumber(), out double num))
        {
            return IEpisodeParser.OffsetEpisodeIndexNumberByLocalConfiguration(context, log, num);
        }
        else
        {
            return null;
        }
    }

    public static string? ExtractAnimeTitleFromPath<T>(EpisodeParserContext context, Logger<T> log)
    {
        string[] names = IEpisodeParser.SplitFilePathParts(context);
        if (names.Length < 2)
        {
            log.Error("Failed to extract anime title from path: {Path}", context.Info.Path);
            return null;
        }

        foreach (var name in names)
        {
            var anitomy = new Anitomy(name);
            var title = anitomy.ExtractAnimeTitle();
            if (!string.IsNullOrEmpty(title))
            {
                return title;
            }
        }
        return null;
    }

    public static EpisodeType? ExtractEpisodeTypeFromPath<T>(EpisodeParserContext context, Logger<T> log)
    {
        string[] names = IEpisodeParser.SplitFilePathParts(context);
        if (names.Length < 2)
        {
            log.Error("Failed to extract episode type from path: {Path}", context.Info.Path);
            return null;
        }

        foreach (var name in names)
        {
            var anitomy = new Anitomy(name);
            var type = AnitomyEpisodeTypeMapping.GetAnitomyAndBangumiEpisodeType(anitomy.ExtractAnimeType());
            if (type.Item2 != null)
            {
                return type.Item2;
            }
        }
        return null;
    }
}
