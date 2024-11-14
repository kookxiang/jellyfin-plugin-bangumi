using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Episode = Jellyfin.Plugin.Bangumi.Model.Episode;

namespace Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
public class AnitomyEpisodeParser : IEpisodeParser
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;
    private readonly BangumiApi _api;
    private readonly ILogger<AnitomyEpisodeParser> _log;
    private readonly ILibraryManager _libraryManager;
    private readonly EpisodeInfo _info;
    private readonly CancellationToken _token;
    private readonly IFileSystem _fileSystem;


    public AnitomyEpisodeParser(BangumiApi api, EpisodeInfo info, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IFileSystem fileSystem, CancellationToken token)
    {
        _api = api;
        _log = loggerFactory.CreateLogger<AnitomyEpisodeParser>();
        _libraryManager = libraryManager;
        _info = info;
        _token = token;
        _fileSystem = fileSystem;
    }

    public Task<Episode?> GetEpisode()
    {
        throw new NotImplementedException();
    }

    public Task<object?> GetEpisodeProperty(EpisodeProperty episodeProperty)
    {
        throw new NotImplementedException();
    }
}