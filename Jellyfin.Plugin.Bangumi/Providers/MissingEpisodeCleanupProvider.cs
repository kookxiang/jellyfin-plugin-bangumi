using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Bangumi.Providers;

/// <summary>Reconciles placeholders after a newly scanned file has acquired its final metadata.</summary>
public class MissingEpisodeCleanupProvider(ILibraryManager library) : ICustomMetadataProvider<Episode>, IHasOrder
{
    public string Name => Constants.ProviderName;
    public int Order => 100;

    public Task<ItemUpdateType> FetchAsync(Episode item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        if (item.IsVirtualItem || string.IsNullOrEmpty(item.Path) || item.SeriesId == System.Guid.Empty)
            return Task.FromResult(ItemUpdateType.None);

        var placeholders = library.GetItemList(new InternalItemsQuery { ParentId = item.SeriesId, Recursive = true })
            .OfType<Episode>().Where(MissingEpisodeProvider.IsOwned).ToArray();
        foreach (var placeholder in placeholders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!int.TryParse(placeholder.GetProviderId(Constants.ProviderName), out var id)
                || !placeholder.IndexNumber.HasValue
                || !MissingEpisodeProvider.Covers(item, id, placeholder.ParentIndexNumber, placeholder.IndexNumber.Value))
                continue;
            library.DeleteItem(placeholder, new DeleteOptions { DeleteFileLocation = false }, false);
        }
        // No metadata on the physical item was changed.
        return Task.FromResult(ItemUpdateType.None);
    }
}
