using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class EpisodePreRefreshProvider : ICustomMetadataProvider<Episode>, IPreRefreshProvider
{
    public string Name => Constants.ProviderName;

    public Task<ItemUpdateType> FetchAsync(Episode item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        if (item.IsVirtualItem || string.IsNullOrEmpty(item.Path))
            return Task.FromResult(ItemUpdateType.None);

        // Let the selected metadata parser set the season instead of Jellyfin's filename guess.
        // Version grouping must not override episode numbers or multipart ranges here.
        item.ParentIndexNumber = null;
        return Task.FromResult(ItemUpdateType.None);
    }
}
