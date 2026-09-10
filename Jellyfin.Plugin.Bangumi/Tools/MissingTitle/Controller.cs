using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Naming.Common;
using Emby.Naming.Video;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.BaseItemManager;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LocalConfiguration = Jellyfin.Plugin.Bangumi.Model.LocalConfiguration;

namespace Jellyfin.Plugin.Bangumi.Tools.MissingTitle;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/Bangumi/Tools/MissingTitle")]
public class Controller(
    Logger<Controller> log,
    ILibraryManager library,
    IBaseItemManager baseItemManager,
    IProviderManager providerManager,
    IDirectoryService directoryService,
    NamingOptions namingOptions) : ControllerBase
{
    private static string LibraryKey(VirtualFolderInfo folder) =>
        string.IsNullOrWhiteSpace(folder.ItemId) ? "name:" + folder.Name : folder.ItemId;

    [HttpGet("Libraries")]
    public IActionResult GetLibraries() => Ok(library.GetVirtualFolders()
        .OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
        .Select(folder => new { Id = LibraryKey(folder), folder.Name }));

    [HttpGet("Items")]
    public async Task<ActionResult<List<MissingTitleItem>>> GetItems([FromQuery] string? libraryId = null, CancellationToken cancellationToken = default)
    {
        var locations = string.IsNullOrWhiteSpace(libraryId) ? null : library.GetVirtualFolders()
            .FirstOrDefault(folder => LibraryKey(folder) == libraryId)?.Locations;
        if (!string.IsNullOrWhiteSpace(libraryId) && locations is null)
            return BadRequest("所选媒体库不存在，请重新选择。");

        var items = library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode, BaseItemKind.Movie],
            IsVirtualItem = false,
        });
        var result = new List<MissingTitleItem>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (locations is not null && !locations.Any(location => MissingBangumiId.Controller.IsInLibrary(item.Path, location)))
                continue;
            var reason = GetMatchReason(item, namingOptions);
            if (reason is null || !await CanRefresh(item)) continue;
            result.Add(new MissingTitleItem
            {
                Id = item.Id,
                Name = item.Name,
                Path = item.Path,
                BangumiId = item.GetProviderId(Constants.ProviderName),
                SeriesId = (item as Episode)?.SeriesId,
                SeriesName = (item as Episode)?.SeriesName,
                Reason = reason,
            });
        }
        return Ok(result.OrderBy(item => item.SeriesName).ThenBy(item => item.Path).ToList());
    }

    [HttpPost("Refresh")]
    public async Task<ActionResult<MissingBangumiId.RefreshResult>> Refresh([FromForm] string? items, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(items)) return BadRequest("请至少选择一个需要刷新的视频。");
        var ids = new HashSet<Guid>();
        foreach (var value in items.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(value, out var id) || id == Guid.Empty) return BadRequest("无效的视频 ID。");
            ids.Add(id);
        }
        if (ids.Count == 0) return BadRequest("请至少选择一个需要刷新的视频。");

        var result = new MissingBangumiId.RefreshResult();
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = library.GetItemById(id);
            // Recheck after scanning: titles, locks, IDs and directory settings may have changed.
            if (item is null || GetMatchReason(item, namingOptions) is null || !await CanRefresh(item))
            {
                result.SkippedCount++;
                continue;
            }
            try
            {
                BangumiApi.RequestFreshMetadata(item.Path);
                providerManager.QueueRefresh(item.Id, new MetadataRefreshOptions(directoryService)
                {
                    MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                    ImageRefreshMode = MetadataRefreshMode.Default,
                    ReplaceAllMetadata = true,
                }, RefreshPriority.High);
                result.QueuedCount++;
                result.QueuedItemIds.Add(item.Id);
            }
            catch (Exception exception)
            {
                BangumiApi.CancelFreshMetadataRequest(item.Path);
                result.FailedCount++;
                log.Error("Failed to queue missing-title refresh for {Id}: {Exception}", item.Id, exception);
            }
        }
        return Ok(result);
    }

    private async Task<bool> CanRefresh(BaseItem item)
    {
        var options = library.GetLibraryOptions(item).GetTypeOptions(item.GetBaseItemKind().ToString());
        return options is not null && baseItemManager.IsMetadataFetcherEnabled(item, options, Constants.ProviderName)
            && !(await LocalConfiguration.ForPath(item.Path)).Skip;
    }

    internal static string? GetMatchReason(BaseItem item, NamingOptions namingOptions)
    {
        if (item is not (Episode or Movie) || item.IsVirtualItem || item.IsLocked
            || item.LockedFields.Contains(MetadataField.Name) || string.IsNullOrWhiteSpace(item.Path)
            || !Path.IsPathRooted(item.Path)
            || !int.TryParse(item.GetProviderId(Constants.ProviderName), out var id) || id <= 0)
            return null;
        if (string.IsNullOrWhiteSpace(item.Name)) return "标题为空";
        if (string.Equals(item.Name, Path.GetFileNameWithoutExtension(item.Path), StringComparison.Ordinal))
            return "标题与文件名一致";
        var parsed = VideoResolver.Resolve(item.Path, false, namingOptions, true);
        return !string.IsNullOrWhiteSpace(parsed?.Name) && string.Equals(item.Name, parsed.Name, StringComparison.Ordinal)
            ? "标题与 Jellyfin 文件名解析结果一致" : null;
    }
}

public class MissingTitleItem
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? BangumiId { get; set; }
    public Guid? SeriesId { get; set; }
    public string? SeriesName { get; set; }
    public string Reason { get; set; } = "";
}
