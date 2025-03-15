
using System.Threading;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.Bangumi.Parser;

public class EpisodeParserContext(
    BangumiApi api,
    ILibraryManager libraryManager,
    EpisodeInfo info,
    IMediaSourceManager mediaSourceManager,
    PluginConfiguration pluginConfiguration,
    LocalConfiguration localConfiguration,
    CancellationToken token
        )
{
    public BangumiApi Api { get; } = api;
    public ILibraryManager LibraryManager { get; } = libraryManager;
    public EpisodeInfo Info { get; } = info;
    public IMediaSourceManager MediaSourceManager { get; } = mediaSourceManager;
    public PluginConfiguration Configuration { get; } = pluginConfiguration;
    public LocalConfiguration LocalConfiguration { get; } = localConfiguration;
    public CancellationToken Token { get; } = token;
}