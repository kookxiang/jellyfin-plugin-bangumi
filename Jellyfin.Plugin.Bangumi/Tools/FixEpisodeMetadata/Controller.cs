using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Bangumi.Tools.FixEpisodeMetadata;

[ApiController]
[Route("Plugins/Bangumi/Tools/FixEpisodeMetadata")]
public class Controller(Logger<Controller> log, ILibraryManager library, IAuthorizationContext authorizationContext)
    : ControllerBase
{

    [HttpPost("Run")]
    [Authorize]
    public async Task<ActionResult<FixResult>> FixEpisodeMetadata()
    {
        // check permission
        var authorizationInfo = await authorizationContext.GetAuthorizationInfo(Request);
        var user = authorizationInfo.User;
        if (user?.Permissions.FirstOrDefault(x => x.Kind == PermissionKind.EnableContentDeletion)?.Value != true)
            return Forbid();

        log.Info("开始修正剧集元数据任务……");

        var episodes = library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
        }).ToList();

        var result = new FixResult()
        {
            TotalCount = episodes.Count,
        };

        log.Info("共找到 {Count} 个剧集", result.TotalCount);

        foreach (var episode in episodes)
        {
            if (HttpContext.RequestAborted.IsCancellationRequested)
            {
                log.Warn("用户取消任务，已处理 {Processed} 个剧集", result.RemovedCount + result.ValidCount + result.NoIdCount);
                break;
            }

            // obtain bangumi episode id
            var bangumiId = episode.GetProviderId(Constants.ProviderName);

            // update episode metadata
            if (bangumiId == "0")
            {
                episode.ProviderIds.Remove(Constants.ProviderName);
                log.Info("移除剧集 {Name} 的无效 ID #{id}", episode.Name, bangumiId);

                // save episode metadata to library
                await library.UpdateItemAsync(episode, episode.GetParent(), ItemUpdateType.MetadataEdit, HttpContext.RequestAborted);
                await Task.Delay(TimeSpan.FromMilliseconds(50), HttpContext.RequestAborted);
                result.RemovedCount++;
            }
            else if (!string.IsNullOrEmpty(bangumiId))
            {
                result.ValidCount++;
            }
            else
            {
                result.NoIdCount++;
            }
        }
        log.Info("剧集元数据修正完成。总共: {Total}, 移除无效 ID: {Removed}, 有效 ID: {Valid}, 无 ID: {NoId}", result.TotalCount, result.RemovedCount, result.ValidCount, result.NoIdCount);

        return Ok(result);
    }
}

public class FixResult
{
    public int TotalCount { get; set; }
    public int RemovedCount { get; set; }
    public int ValidCount { get; set; }
    public int NoIdCount { get; set; }
}