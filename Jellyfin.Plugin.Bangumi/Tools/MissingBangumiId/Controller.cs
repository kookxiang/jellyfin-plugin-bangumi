using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Api;
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
                };
            })
            .ToList());
    }

    [HttpPost("Refresh")]
    public ActionResult<RefreshResult> Refresh()
    {
        var result = new RefreshResult();

        foreach (var item in FindMissingItems())
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
            "Queued metadata refresh for {QueuedCount} videos missing Bangumi IDs; {FailedCount} failed",
            result.QueuedCount,
            result.FailedCount);

        return Ok(result);
    }

    private List<BaseItem> FindMissingItems()
    {
        return library.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Episode, BaseItemKind.Movie],
                IsVirtualItem = false,
            })
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
}

public class MissingBangumiIdItem
{
    public Guid Id { get; set; }

    public string? Name { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? Path { get; set; }

    public string? SeriesName { get; set; }

    public string? SeasonName { get; set; }
}

public class RefreshResult
{
    public int QueuedCount { get; set; }

    public int FailedCount { get; set; }
}
