using System;
using System.Linq;
using System.Threading;
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

        var result = new FixResult();

        var itemIds = library.GetItemIds(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
        });
        foreach (var itemId in itemIds)
        {
            // add a small delay to reduce resource usage
            await Task.Delay(TimeSpan.FromMilliseconds(50), HttpContext.RequestAborted);

            // obtain library item
            var item = library.GetItemById(itemId);
            if (item == null) continue;

            // obtain bangumi episode id
            var bangumiId = item.GetProviderId(Constants.ProviderName);

            // update episode metadata
            if (bangumiId == "0")
            {
                item.ProviderIds.Remove(Constants.ProviderName);
                log.Info("remove ProviderId #{id} for episode {Name}", bangumiId, item.Name);

                // save episode metadata to library
                await library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None);
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

        result.TotalCount = itemIds.Count;
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