using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;
using Jellyfin.Plugin.Bangumi.Utils;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Tools.EpisodeSubjectMismatch;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/Bangumi/Tools/EpisodeSubjectMismatch")]
public class Controller(ILibraryManager library, BangumiApi api, Logger<Controller> log) : ControllerBase
{
    private static string LibraryKey(VirtualFolderInfo folder) =>
        string.IsNullOrWhiteSpace(folder.ItemId) ? "name:" + folder.Name : folder.ItemId;

    [HttpGet("Libraries")]
    public IActionResult GetLibraries() => Ok(library.GetVirtualFolders()
        .OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
        .Select(folder => new { Id = LibraryKey(folder), folder.Name }));

    [HttpGet("Items")]
    public async Task<ActionResult<ScanResult>> GetItems([FromQuery] string? libraryId = null,
        [FromQuery] int startIndex = 0, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (startIndex < 0 || limit is < 1 or > 50)
            return BadRequest("无效的扫描范围，每批最多检查 50 项。");
        var folder = string.IsNullOrWhiteSpace(libraryId) ? null : library.GetVirtualFolders()
            .FirstOrDefault(item => LibraryKey(item) == libraryId);
        if (!string.IsNullOrWhiteSpace(libraryId) && folder is null)
            return BadRequest("所选媒体库不存在，请重新选择。");

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            IsVirtualItem = false,
            Recursive = true,
            GroupByPresentationUniqueKey = false,
            OrderBy = [(ItemSortBy.SortName, SortOrder.Ascending)],
            StartIndex = startIndex,
            Limit = limit,
        };
        var pathFallback = folder is not null && !Guid.TryParse(folder.ItemId, out _);
        if (folder is not null && !pathFallback) query.ParentId = Guid.Parse(folder.ItemId);
        var page = library.GetItemsResult(query);
        var result = new ScanResult { ScannedCount = page.Items.Count, TotalItemCount = page.TotalRecordCount };
        var next = startIndex + page.Items.Count;
        if (page.Items.Count > 0 && next < page.TotalRecordCount) result.NextStartIndex = next;

        // Reused IDs only need one lookup per page. Failed lookups are cached too.
        var episodes = new Dictionary<int, Task<Model.Episode?>>();
        foreach (var item in page.Items.OfType<Episode>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.IsVirtualItem || string.IsNullOrWhiteSpace(item.Path) || item.ParentIndexNumber == 0
                || !int.TryParse(item.GetProviderId(Constants.ProviderName), out var episodeId) || episodeId <= 0
                || pathFallback && !(folder!.Locations ?? []).Any(location => MissingBangumiId.Controller.IsInLibrary(item.Path, location)))
            {
                result.SkippedCount++;
                continue;
            }

            var season = (item.SeasonId == Guid.Empty ? null : library.GetItemById(item.SeasonId)) as Season
                ?? item.GetParent() as Season;
            var subjectId = int.TryParse(season?.GetProviderId(Constants.ProviderName), out var savedSubjectId)
                ? savedSubjectId : 0;
            var row = new MismatchItem
            {
                Id = item.Id, Name = item.Name, Path = item.Path, SeriesId = item.SeriesId,
                SeriesName = item.SeriesName, SeasonId = season?.Id, SeasonName = season?.Name,
                EpisodeBangumiId = episodeId,
                SeasonBangumiId = subjectId > 0 ? subjectId : null,
            };
            try
            {
                var local = await LocalConfiguration.ForPath(item.Path);
                var forcedType = local.GetForcedEpisodeType();
                var configuration = Plugin.Instance!.Configuration;
                if (season?.IndexNumber == 0 || local.Skip || forcedType == EpisodeType.Special
                    || forcedType != EpisodeType.Normal && (BasicEpisodeParser.IsSpecial(item.Path, library)
                        || configuration.EpisodeParser == Configuration.EpisodeParserType.Torrent
                        && ExcludeRegexMatcher.IsSpecial(configuration, item.Path, ExcludeRegexPathType.EpisodeFile, library, log)))
                {
                    result.SkippedCount++;
                    continue;
                }

                if (!episodes.TryGetValue(episodeId, out var lookup))
                {
                    lookup = api.GetEpisode(episodeId, cancellationToken);
                    episodes[episodeId] = lookup;
                }
                var episode = await lookup;
                if (episode is null || episode.ParentId <= 0)
                {
                    row.Reason = "未找到单集的 Bangumi 所属条目";
                    result.Items.Add(row);
                    continue;
                }
                if (forcedType != EpisodeType.Normal && episode.Type != EpisodeType.Normal)
                {
                    result.SkippedCount++;
                    continue;
                }
                row.ActualSubjectId = episode.ParentId;
                if (subjectId <= 0)
                {
                    row.Reason = "所属季度缺少有效的 Bangumi ID";
                    result.Items.Add(row);
                    continue;
                }
                result.CheckedCount++;
                if (subjectId != episode.ParentId)
                {
                    row.Status = "Mismatch";
                    row.Reason = "普通集实际所属的 Bangumi 条目与季度不同";
                    result.Items.Add(row);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                log.Warn("Failed to verify episode subject for {Id}: {Error}", item.Id, exception.Message);
                row.Reason = "读取配置或查询单集失败，请重试";
                result.Items.Add(row);
            }
        }
        return Ok(result);
    }
}

public class ScanResult
{
    public int ScannedCount { get; set; }
    public int TotalItemCount { get; set; }
    public int CheckedCount { get; set; }
    public int SkippedCount { get; set; }
    public int? NextStartIndex { get; set; }
    public Collection<MismatchItem> Items { get; } = [];
}

public class MismatchItem
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Path { get; set; }
    public Guid SeriesId { get; set; }
    public string? SeriesName { get; set; }
    public Guid? SeasonId { get; set; }
    public string? SeasonName { get; set; }
    public int EpisodeBangumiId { get; set; }
    public int? SeasonBangumiId { get; set; }
    public int? ActualSubjectId { get; set; }
    public string Status { get; set; } = "Unverified";
    public string Reason { get; set; } = "";
}
