using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Bangumi.Tools.DuplicatedEpisodesDetector;

[ApiController]
[Route("Plugins/Bangumi/Tools/DuplicatedEpisodesDetector")]
public class Controller(Logger<Controller> logger, ILibraryManager library, IAuthorizationContext authorizationContext) : ControllerBase
{
    [HttpPost("Scan")]
    [Authorize]
    public async Task<List<DuplicatedEpisode>> Scan()
    {
        return await Task.Run(() => ScanSync(
            checkLength: Request.Form["length"] == "true",
            skipSpecials: Request.Form["specials"] == "false"
        ));
    }

    [HttpPost("Delete")]
    [Authorize]
    public async Task<ActionResult> Delete([FromForm] string items)
    {
        if (string.IsNullOrEmpty(items))
            return BadRequest();

        // check permission
        var authorizationInfo = await authorizationContext.GetAuthorizationInfo(Request);
        var user = authorizationInfo.User;
        if (user?.Permissions.FirstOrDefault(x => x.Kind == PermissionKind.EnableContentDeletion)?.Value != true)
            return Forbid();

        // search items
        var itemIds = items.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var baseItems = library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            ItemIds = itemIds.Select(Guid.Parse).ToArray(),
        });

        // delete found items
        foreach (var item in baseItems)
            library.DeleteItem(item, new DeleteOptions { DeleteFileLocation = true }, true);

        return Accepted();
    }

    private List<DuplicatedEpisode> ScanSync(bool skipSpecials, bool checkLength)
    {
        List<DuplicatedEpisode> result = [];

        Dictionary<string, List<BaseItem>> map = new();

        var query = new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.Episode] };
        var episodeList = library.GetItemList(query)
            .Where(o => o.ProviderIds.ContainsKey(Constants.PluginName))
            .Distinct()
            .ToList();

        logger.Info($"Found {episodeList.Count} episodes with Bangumi IDs.");

        foreach (var item in episodeList)
        {
            var id = item.GetProviderId(Constants.PluginName);
            if (id is null) continue;
            if (!map.ContainsKey(id)) map[id] = [];
            if (item is not Episode episode) continue;
            if ((episode.Season?.IndexNumber == 0 || episode.ParentIndexNumber == 0) && skipSpecials) continue;
            map[id].Add(episode);
        }

        foreach (var (id, list) in map)
        {
            if (list.Count <= 1) continue;

            var items = list.Select(item => new DuplicatedEpisodeItem
            {
                Id = item.Id,
                Path = item.Path,
                LastModified = new FileInfo(item.Path).LastWriteTime,
                Ticks = item.RunTimeTicks,
            }).OrderByDescending(item => item.LastModified).ToList();

            // sometimes, there are multiple entries with the same path
            if (items.GroupBy(x => x.Path).Any(g => g.Count() > 1)) continue;

            if (checkLength)
            {
                double minTicks = items.Select(x => x.Ticks).Where(x => x != null).Min() ?? 0;
                double maxTicks = items.Select(x => x.Ticks).Where(x => x != null).Max() ?? 0;
                if (minTicks == 0 || maxTicks == 0) continue;
                if ((maxTicks - minTicks) / minTicks > 0.01 && (maxTicks - minTicks) > TimeSpan.FromSeconds(30).Ticks) continue;
            }

            if (items.Count <= 1) continue;

            result.Add(new DuplicatedEpisode
            {
                BangumiId = int.Parse(id),
                Title = list.First().Name,
                Items = items
            });
        }

        return result;
    }
}
