using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.BaseItemManager;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Bangumi.Tools.MissingBangumiId;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/Bangumi/Tools/MissingBangumiId")]
public class Controller(
    Logger<Controller> log,
    ILibraryManager library,
    IBaseItemManager baseItemManager,
    IProviderManager providerManager,
    IDirectoryService directoryService) : ControllerBase
{
    [HttpGet("Items")]
    public ActionResult<List<MissingBangumiIdItem>> GetItems()
    {
        return Ok(FindMissingItems()
            .Select(item =>
            {
                var episode = item as Episode;
                return new MissingBangumiIdItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    Type = item.GetBaseItemKind().ToString(),
                    Path = item.Path,
                    SeriesName = episode?.SeriesName,
                    SeasonName = episode?.Season?.Name,
                    LibraryName = string.Join("、", library.GetCollectionFolders(item)
                        .Select(folder => folder.Name)
                        .Distinct()),
                    BangumiProviderEnabled = IsBangumiMetadataProviderEnabled(item),
                };
            })
            .ToList());
    }

    [HttpPost("Refresh")]
    public ActionResult<RefreshResult> Refresh([FromForm] string? items)
    {
        if (string.IsNullOrWhiteSpace(items))
            return BadRequest("请至少选择一个需要刷新的视频。");

        var itemIds = new List<Guid>();
        foreach (var itemId in items.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(itemId, out var parsedItemId))
                return BadRequest($"无效的视频 ID：{itemId}");

            if (!itemIds.Contains(parsedItemId))
                itemIds.Add(parsedItemId);
        }

        if (itemIds.Count == 0)
            return BadRequest("请至少选择一个需要刷新的视频。");

        var result = new RefreshResult();
        var missingItems = FindMissingItems(itemIds.ToArray());
        result.SkippedCount = itemIds.Count - missingItems.Count;
        var refreshableItems = missingItems
            .Where(item => IsBangumiMetadataProviderEnabled(item))
            .ToList();
        result.ProviderDisabledCount = missingItems.Count - refreshableItems.Count;

        foreach (var item in refreshableItems)
        {
            try
            {
                providerManager.QueueRefresh(
                    item.Id,
                    new MetadataRefreshOptions(directoryService)
                    {
                        MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                        ImageRefreshMode = MetadataRefreshMode.Default,
                        ReplaceAllMetadata = true,
                    },
                    RefreshPriority.High);
                result.QueuedCount++;
                result.QueuedItemIds.Add(item.Id);
            }
            catch (Exception exception)
            {
                result.FailedCount++;
                log.Error(
                    "Failed to queue metadata refresh for {Name} (#{Id}): {Exception}",
                    item.Name,
                    item.Id,
                    exception);
            }
        }

        log.Info(
            "Queued metadata refresh for {QueuedCount} selected videos missing Bangumi IDs; {FailedCount} failed, {ProviderDisabledCount} had the Bangumi provider disabled, and {SkippedCount} skipped",
            result.QueuedCount,
            result.FailedCount,
            result.ProviderDisabledCount,
            result.SkippedCount);

        return Ok(result);
    }

    private List<BaseItem> FindMissingItems(Guid[]? itemIds = null)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode, BaseItemKind.Movie],
            IsVirtualItem = false,
        };
        if (itemIds is not null)
            query.ItemIds = itemIds;

        return library.GetItemList(query)
            .Where(item => !HasValidBangumiId(item))
            .OrderBy(item => item.GetBaseItemKind())
            .ThenBy(item => (item as Episode)?.SeriesName)
            .ThenBy(item => item.ParentIndexNumber)
            .ThenBy(item => item.IndexNumber)
            .ThenBy(item => item.Name)
            .ToList();
    }

    private static bool HasValidBangumiId(BaseItem item)
    {
        return int.TryParse(item.GetProviderId(Constants.ProviderName), out var bangumiId) && bangumiId > 0;
    }

    private bool IsBangumiMetadataProviderEnabled(BaseItem item)
    {
        var typeOptions = library.GetLibraryOptions(item).GetTypeOptions(item.GetBaseItemKind().ToString());
        return typeOptions is not null
               && baseItemManager.IsMetadataFetcherEnabled(item, typeOptions, Constants.ProviderName);
    }
}

public class MissingBangumiIdItem
{
    public Guid Id { get; set; }

    public string? Name { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? Path { get; set; }

    public string? SeriesName { get; set; }

    public string? SeasonName { get; set; }

    public string LibraryName { get; set; } = string.Empty;

    public bool BangumiProviderEnabled { get; set; }
}

public class RefreshResult
{
    public int QueuedCount { get; set; }

    public int FailedCount { get; set; }

    public int SkippedCount { get; set; }

    public int ProviderDisabledCount { get; set; }

    public Collection<Guid> QueuedItemIds { get; } = [];
}
