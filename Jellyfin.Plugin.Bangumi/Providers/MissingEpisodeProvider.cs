using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Archive;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser;
using MediaBrowser.Controller.BaseItemManager;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Providers;

/// <summary>Maintains metadata-only episodes using the installed Bangumi archive.</summary>
public class MissingEpisodeProvider(ArchiveData archive, ILibraryManager library, IBaseItemManager baseItemManager)
    : ICustomMetadataProvider<Series>, IHasItemChangeMonitor, IHasOrder
{
    // A Bangumi ID alone does not prove we created an item (another provider may add it).
    internal const string OwnershipKey = "BangumiMissingEpisode";
    public string Name => Constants.ProviderName;
    public int Order => 100;

    // Also run when disabled, so the next scan can clean up our placeholders.
    public bool HasChanged(BaseItem item, IDirectoryService directoryService) => item is Series;

    public async Task<ItemUpdateType> FetchAsync(Series item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = Plugin.Instance!.Configuration;
        var children = library.GetItemList(new InternalItemsQuery { Parent = item, Recursive = true });
        var episodes = children.OfType<Episode>().ToArray();
        var ours = episodes.Where(IsOwned).ToArray();
        var episodeOptions = library.GetLibraryOptions(item).GetTypeOptions(nameof(Episode));
        var enabled = episodeOptions != null && baseItemManager.IsMetadataFetcherEnabled(new Episode(), episodeOptions, Constants.ProviderName)
            && (configuration.ImportMissingEpisodes || configuration.ImportUnairedEpisodes)
            && library.GetCollectionFolders(item).Any(folder => configuration.EnabledMissingEpisodeLibraries
                .Any(id => MatchesLibrary(id, folder)));
        if (!enabled)
            return Remove(item, ours);

        // No online fallback. Missing/incomplete offline data must not erase existing placeholders.
        if (!archive.Episode.Exists() || !archive.SubjectEpisodeRelation.Exists())
            return ItemUpdateType.None;

        var relation = archive.SubjectEpisodeRelation;
        var seasons = children.OfType<Season>().Where(season => season.IndexNumber > 0).ToList();
        var changed = false;
        // Flat series get their normal season from Jellyfin. Defer creation until that season exists.
        foreach (var season in seasons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = !string.IsNullOrEmpty(season.Path) ? season.Path : season.IndexNumber == 1 ? item.Path : null;
            var local = string.IsNullOrEmpty(path) ? new LocalConfiguration() : await LocalConfiguration.ForPath(path);
            var subjectId = ResolveSubjectId(item, season, local);
            var seasonOwned = ours.Where(episode => episode.SeasonId == season.Id).ToArray();
            if (local.Skip || local.GetForcedEpisodeType() is EpisodeType.Special)
            {
                changed |= Remove(item, seasonOwned) != ItemUpdateType.None;
                continue;
            }
            if (subjectId <= 0)
                continue;

            // Materialize all data before mutating the library. A failed read leaves the season intact.
            var data = (await relation.GetEpisodes(subjectId, cancellationToken)).Select(episode => episode.ToEpisode()).ToArray();
            if (data.Length == 0)
                continue;
            var desired = data.Where(episode => ShouldImport(episode, local, configuration, DateTime.UtcNow.Date))
                .GroupBy(episode => episode.Id).Select(group => group.First()).ToArray();
            var physical = episodes.Where(episode => !episode.IsVirtualItem && !string.IsNullOrEmpty(episode.Path)).ToArray();
            var targetIds = new HashSet<Guid>();
            foreach (var source in desired)
            {
                var number = LocalConfigurationHelper.GetDisplayEpisodeIndex(source.Order, local);
                if (physical.Any(episode => Covers(episode, source.Id, season.IndexNumber, number)))
                    continue;
                // Respect placeholders supplied by other providers, too.
                if (episodes.Any(episode => !IsOwned(episode) && episode.IsVirtualItem
                    && episode.ParentIndexNumber == season.IndexNumber && episode.ContainsEpisodeNumber(number)))
                    continue;

                var id = library.GetNewItemId($"BangumiMissing:{item.Id:N}:{season.Id:N}:{source.Id}", typeof(Episode));
                targetIds.Add(id);
                var existing = seasonOwned.FirstOrDefault(episode => episode.Id == id);
                if (existing != null)
                {
                    if (ApplyMetadata(existing, source, season, local))
                    {
                        await library.UpdateItemAsync(existing, season, ItemUpdateType.MetadataImport, cancellationToken);
                        changed = true;
                    }
                    continue;
                }

                var placeholder = new Episode
                {
                    Id = id,
                    IsVirtualItem = true,
                    SeriesId = item.Id,
                    SeriesName = item.Name,
                    SeasonId = season.Id,
                    SeasonName = season.Name,
                    SeriesPresentationUniqueKey = item.GetPresentationUniqueKey()
                };
                ApplyMetadata(placeholder, source, season, local);
                placeholder.SetProviderId(OwnershipKey, "1");
                placeholder.PresentationUniqueKey = placeholder.CreatePresentationUniqueKey();
                season.AddChild(placeholder);
                changed = true;
            }
            changed |= Remove(item, seasonOwned.Where(episode => !targetIds.Contains(episode.Id))) != ItemUpdateType.None;
        }
        if (!changed) return ItemUpdateType.None;
        item.Children = null;
        return ItemUpdateType.MetadataImport;
    }

    internal static bool MatchesLibrary(string id, Folder folder)
    {
        // The media-library endpoint falls back to name:<Name> when ItemId is unavailable.
        // Keep those saved selections valid even if a later scan assigns the folder an ID.
        return Guid.TryParse(id, out var parsed)
            ? parsed == folder.Id
            : string.Equals(id, "name:" + folder.Name, StringComparison.Ordinal);
    }

    internal static int ResolveSubjectId(Series series, Season season, LocalConfiguration local)
    {
        if (local.Id > 0) return local.Id;
        if (int.TryParse(season.GetProviderId(Constants.ProviderName), out var id) && id > 0) return id;
        return season.IndexNumber == 1 && int.TryParse(series.GetProviderId(Constants.ProviderName), out id) ? id : 0;
    }

    internal static bool IsOwned(Episode episode) => episode.IsVirtualItem
        && string.IsNullOrEmpty(episode.Path) && episode.GetProviderId(OwnershipKey) == "1";

    internal static bool ShouldImport(Model.Episode episode, LocalConfiguration local, PluginConfiguration configuration, DateTime today)
    {
        if (local.Skip || local.GetForcedEpisodeType() is EpisodeType.Special
            || episode.Id <= 0 || episode.Type != EpisodeType.Normal || !double.IsFinite(episode.Order)
            || episode.Order != Math.Truncate(episode.Order) || episode.Order < 0 || episode.Order > int.MaxValue)
            return false;
        var display = episode.Order + (local.CorrectIndex ? 0 : local.Offset);
        if (display < 0 || display > int.MaxValue || !TryAirDate(episode.AirDate, out var date)) return false;
        return date >= today.Date ? configuration.ImportUnairedEpisodes : configuration.ImportMissingEpisodes;
    }

    internal static bool Covers(Episode physical, int bangumiId, int? seasonNumber, int number)
    {
        if (physical.IsVirtualItem || string.IsNullOrEmpty(physical.Path)) return false;
        // An exact identity also works before Jellyfin has assigned SeasonId during an initial scan.
        if (physical.GetProviderId(Constants.ProviderName) == bangumiId.ToString(CultureInfo.InvariantCulture)) return true;
        // Jellyfin itself reconciles placeholders by season/number, including multi-episode files.
        return physical.ParentIndexNumber == seasonNumber && physical.ContainsEpisodeNumber(number);
    }

    internal static bool TryAirDate(string value, out DateTime date) => DateTime.TryParseExact(
        value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);

    internal static bool ApplyMetadata(Episode target, Model.Episode source, Season season, LocalConfiguration local)
    {
        var number = LocalConfigurationHelper.GetDisplayEpisodeIndex(source.Order, local);
        TryAirDate(source.AirDate, out var date);
        var name = string.IsNullOrEmpty(source.Name) ? $"第 {number} 集" : source.Name;
        var overview = string.IsNullOrEmpty(source.Description) ? null : source.Description;
        var id = source.Id.ToString(CultureInfo.InvariantCulture);
        var changed = target.Name != name || target.OriginalTitle != source.OriginalName || target.Overview != overview
            || target.PremiereDate != date || target.IndexNumber != number || target.ParentIndexNumber != season.IndexNumber
            || target.GetProviderId(Constants.ProviderName) != id;
        target.Name = name;
        target.OriginalTitle = source.OriginalName;
        target.Overview = overview;
        target.PremiereDate = date;
        target.ProductionYear = date.Year;
        target.IndexNumber = number;
        target.ParentIndexNumber = season.IndexNumber;
        target.SetProviderId(Constants.ProviderName, id);
        if (changed) target.PresentationUniqueKey = target.CreatePresentationUniqueKey();
        return changed;
    }

    private ItemUpdateType Remove(Series series, IEnumerable<Episode> episodes)
    {
        var changed = false;
        foreach (var episode in episodes.Where(IsOwned))
        {
            library.DeleteItem(episode, new DeleteOptions { DeleteFileLocation = false }, false);
            changed = true;
        }
        if (!changed) return ItemUpdateType.None;
        series.Children = null;
        return ItemUpdateType.MetadataImport;
    }
}
