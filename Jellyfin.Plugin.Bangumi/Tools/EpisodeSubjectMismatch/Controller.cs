using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;
using Jellyfin.Plugin.Bangumi.Utils;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http;
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
        [FromQuery] int startIndex = 0, [FromQuery] int limit = 20,
        [FromQuery] bool recentOnly = true, [FromQuery] bool skipSpecials = true,
        CancellationToken cancellationToken = default)
    {
        var folder = FindLibrary(libraryId);
        if (startIndex < 0 || limit is < 1 or > 50)
            return BadRequest("无效的扫描范围，每批最多检查 50 项。");
        if (!string.IsNullOrWhiteSpace(libraryId) && folder is null)
            return BadRequest("所选媒体库不存在，请重新选择。");
        return Ok(await ScanPage(folder, startIndex, limit, recentOnly, skipSpecials, cancellationToken));
    }

    private VirtualFolderInfo? FindLibrary(string? libraryId) => string.IsNullOrWhiteSpace(libraryId) ? null
        : library.GetVirtualFolders().FirstOrDefault(item => LibraryKey(item) == libraryId);

    // A scan belongs to this request. Disconnecting cancels both traversal and API lookups.
    [HttpGet("Scan")]
    public async Task<IActionResult> Scan([FromQuery] string? libraryId = null,
        [FromQuery] bool recentOnly = true, [FromQuery] bool skipSpecials = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folder = FindLibrary(libraryId);
        if (!string.IsNullOrWhiteSpace(libraryId) && folder is null)
            return BadRequest("所选媒体库不存在，请重新选择。");

        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        var totals = new ScanResult();
        var startIndex = 0;
        var clock = Stopwatch.StartNew();
        async Task Send(string kind, object data)
        {
            await Response.WriteAsync($"event: {kind}\ndata: {JsonSerializer.Serialize(data)}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            clock.Restart();
        }
        await Send("progress", totals);
        try
        {
            while (true)
            {
                var emitted = 0;
                async Task Progress(ScanResult page, bool force = false)
                {
                    if (!force && emitted == page.Items.Count && clock.ElapsedMilliseconds < 250) return;
                    await Send("progress", new
                    {
                        ScannedCount = totals.ScannedCount + page.ScannedCount,
                        page.TotalItemCount,
                        CheckedCount = totals.CheckedCount + page.CheckedCount,
                        SkippedCount = totals.SkippedCount + page.SkippedCount,
                        Items = page.Items.Skip(emitted).ToArray(),
                    });
                    emitted = page.Items.Count;
                }
                var page = await ScanPage(folder, startIndex, 50, recentOnly, skipSpecials, cancellationToken,
                    result => Progress(result));
                await Progress(page, true);
                totals.ScannedCount += page.ScannedCount;
                totals.TotalItemCount = page.TotalItemCount;
                totals.CheckedCount += page.CheckedCount;
                totals.SkippedCount += page.SkippedCount;
                if (page.NextStartIndex is not { } next) break;
                if (next <= startIndex) throw new InvalidOperationException("扫描分页未前进，请重新扫描。");
                startIndex = next;
            }
            await Send("complete", totals);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            log.Warn("Episode subject scan failed: {Error}", exception.Message);
            await Send("error", new { Message = "扫描中断，请重新扫描。" });
        }
        return new EmptyResult();
    }

    private async Task<ScanResult> ScanPage(VirtualFolderInfo? folder, int startIndex, int limit,
        bool recentOnly, bool skipSpecials, CancellationToken cancellationToken,
        Func<ScanResult, Task>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        var result = new ScanResult { TotalItemCount = page.TotalRecordCount };
        var next = startIndex + page.Items.Count;
        if (page.Items.Count > 0 && next < page.TotalRecordCount) result.NextStartIndex = next;

        // Reused IDs only need one lookup per page. Failed lookups are cached too.
        var episodes = new Dictionary<int, Task<Model.Episode?>>();
        var minimumModifiedDate = DateTime.UtcNow.AddMonths(-1);
        foreach (var item in page.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is Episode episode)
                await CheckEpisode(episode, result, episodes, minimumModifiedDate, folder, pathFallback,
                    recentOnly, skipSpecials, cancellationToken);
            else result.SkippedCount++;
            cancellationToken.ThrowIfCancellationRequested();
            result.ScannedCount++;
            if (progress is not null) await progress(result);
        }
        return result;
    }

    private async Task CheckEpisode(Episode item, ScanResult result,
        Dictionary<int, Task<Model.Episode?>> episodes, DateTime minimumModifiedDate,
        VirtualFolderInfo? folder, bool pathFallback, bool recentOnly, bool skipSpecials,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item.IsVirtualItem || string.IsNullOrWhiteSpace(item.Path)
            || recentOnly && item.DateModified < minimumModifiedDate
            || skipSpecials && item.ParentIndexNumber == 0
            || !int.TryParse(item.GetProviderId(Constants.ProviderName), out var episodeId) || episodeId <= 0
            || pathFallback && !(folder!.Locations ?? []).Any(location => MissingBangumiId.Controller.IsInLibrary(item.Path, location)))
        {
            result.SkippedCount++;
            return;
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
            if (local.Skip || skipSpecials && (season?.IndexNumber == 0 || forcedType == EpisodeType.Special
                || forcedType != EpisodeType.Normal && (BasicEpisodeParser.IsSpecial(item.Path, library)
                    || configuration.EpisodeParser == Configuration.EpisodeParserType.Torrent
                    && ExcludeRegexMatcher.IsSpecial(configuration, item.Path, ExcludeRegexPathType.EpisodeFile, library, log))))
            {
                result.SkippedCount++;
                return;
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
                return;
            }
            if (skipSpecials && forcedType != EpisodeType.Normal && episode.Type != EpisodeType.Normal)
            {
                result.SkippedCount++;
                return;
            }
            row.ActualSubjectId = episode.ParentId;
            if (subjectId <= 0)
            {
                row.Reason = "所属季度缺少有效的 Bangumi ID";
                result.Items.Add(row);
                return;
            }
            result.CheckedCount++;
            if (subjectId != episode.ParentId)
            {
                row.Status = "Mismatch";
                row.Reason = "单集实际所属的 Bangumi 条目与季度不同";
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
