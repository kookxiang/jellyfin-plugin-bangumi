using System.Threading;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Parser;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;
using Jellyfin.Plugin.Bangumi.Parser.TorrentParser;
using MediaBrowser.Controller.Providers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using JellyfinEpisode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Tools.MediaLibrary;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/Bangumi/Tools/MediaLibrary")]
public class Controller(ILibraryManager library) : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 20;

    [HttpGet("Libraries")]
    public ActionResult<IEnumerable<MediaLibraryInfo>> GetLibraries()
    {
        return Ok(library.GetVirtualFolders()
            .Select(folder => new MediaLibraryInfo
            {
                Id = string.IsNullOrWhiteSpace(folder.ItemId) ? "name:" + folder.Name : folder.ItemId,
                Name = folder.Name ?? string.Empty,
            })
            .OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray());
    }

    [HttpGet("Items")]
    public ActionResult<MediaLibraryItemsResult> GetItems(
        [FromQuery] string? libraryId,
        [FromQuery] string? search,
        [FromQuery] int startIndex = 0,
        [FromQuery] int limit = DefaultPageSize)
    {
        startIndex = Math.Max(startIndex, 0);
        limit = Math.Clamp(limit, 1, MaxPageSize);

        var virtualFolders = library.GetVirtualFolders()
            .Select(folder => new LibraryFolder
            {
                // Virtual folder names are unique directory names. Keep libraries whose
                // collection folder has not supplied an ItemId; filtering uses Locations.
                Id = string.IsNullOrWhiteSpace(folder.ItemId) ? "name:" + folder.Name : folder.ItemId,
                Name = folder.Name ?? string.Empty,
                Locations = folder.Locations ?? [],
            })
            .OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var selectedLibrary = virtualFolders.FirstOrDefault(folder =>
            string.Equals(folder.Id, libraryId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(libraryId) && selectedLibrary is null)
            return Ok(new MediaLibraryItemsResult { StartIndex = startIndex });

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series],
            IsVirtualItem = false,
            Recursive = true,
            GroupByPresentationUniqueKey = false,
            OrderBy = [(ItemSortBy.SortName, SortOrder.Ascending)],
        };
        var pathFallback = selectedLibrary is not null && !Guid.TryParse(selectedLibrary.Id, out _);
        if (selectedLibrary is not null && !pathFallback)
            query.ParentId = Guid.Parse(selectedLibrary.Id);

        List<MediaLibraryItem> page;
        int total;
        if (!pathFallback && string.IsNullOrWhiteSpace(search))
        {
            query.StartIndex = startIndex;
            query.Limit = limit;
            var result = library.GetItemsResult(query);
            total = result.TotalRecordCount;
            page = result.Items.OfType<Series>()
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => CreateItem(item, virtualFolders, false))
                .ToList();
        }
        else
        {
            // Name/path and physical subdirectory search needs index metadata, but
            // never checks media storage until after the matching roots are paged.
            var series = library.GetItemList(query).OfType<Series>()
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .Where(item => !pathFallback || selectedLibrary!.Locations.Any(location => IsPathInDirectory(item.Path, location)))
                .Select(item => CreateItem(item, virtualFolders, false)).ToList();
            var items = new List<MediaLibraryItem>(series);
            if (!string.IsNullOrWhiteSpace(search) && series.Count > 0)
            {
                var episodes = library.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.Episode],
                    IsVirtualItem = false,
                    ParentId = query.ParentId,
                    Recursive = true,
                    AncestorIds = pathFallback ? series.Select(item => item.Id).ToArray() : [],
                    GroupByPresentationUniqueKey = false,
                }).OfType<JellyfinEpisode>();
                items.AddRange(BuildPhysicalFolderItems(series, episodes, false));
            }
            var roots = BuildTree(items, search);
            total = roots.Count;
            page = roots.Skip(startIndex).Take(limit).ToList();
        }
        foreach (var item in page)
        {
            item.Children = [];
            item.HasConfiguration = System.IO.File.Exists(Path.Join(item.Path, "bangumi.ini"));
        }
        return Ok(new MediaLibraryItemsResult
        {
            Libraries = virtualFolders.Select(folder => new MediaLibraryInfo { Id = folder.Id, Name = folder.Name }),
            Items = page,
            TotalRecordCount = total,
            TotalItemCount = total,
            StartIndex = startIndex,
        });
    }

    [HttpGet("Folders/{seriesId:guid}")]
    public ActionResult<MediaLibraryItemsResult> GetFolders(
        Guid seriesId, [FromQuery] string? search = null,
        [FromQuery] int startIndex = 0, [FromQuery] int limit = DefaultPageSize)
    {
        if (library.GetItemById(seriesId) is not Series series || string.IsNullOrWhiteSpace(series.Path))
            return NotFound();
        startIndex = Math.Max(0, startIndex);
        limit = Math.Clamp(limit, 1, MaxPageSize);
        var parent = CreateItem(series, [], false);
        var folders = BuildPhysicalFolderItems([parent], GetSeriesEpisodes(seriesId), false)
            .Where(folder => MatchesSearch(parent, search) || MatchesSearch(folder, search)).ToList();
        var page = folders.Skip(startIndex).Take(limit).ToList();
        foreach (var item in page)
            item.HasConfiguration = System.IO.File.Exists(Path.Join(item.Path, "bangumi.ini"));
        return Ok(new MediaLibraryItemsResult
        {
            Items = page, TotalRecordCount = folders.Count,
            TotalItemCount = folders.Count, StartIndex = startIndex,
        });
    }

    private IEnumerable<JellyfinEpisode> GetSeriesEpisodes(Guid seriesId)
    {
        return library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode], IsVirtualItem = false,
            ParentId = seriesId, Recursive = true, GroupByPresentationUniqueKey = false,
        }).OfType<JellyfinEpisode>();
    }

    [HttpGet("Configuration/{itemId:guid}")]
    public async Task<ActionResult<MediaLibraryConfiguration>> GetConfiguration(Guid itemId, [FromQuery] Guid? seriesId = null)
    {
        var target = GetTarget(itemId, seriesId);
        if (target is null)
            return NotFound();

        var configurationPath = Path.Join(target.Path, "bangumi.ini");
        var configuration = new LocalConfiguration();
        await configuration.ReadFrom(configurationPath);
        return Ok(CreateConfiguration(target, configuration, System.IO.File.Exists(configurationPath)));
    }

    [HttpGet("Preview/{itemId:guid}")]
    public ActionResult Preview(
        Guid itemId,
        [FromServices] BangumiApi api,
        [FromServices] IMediaSourceManager mediaSources,
        [FromServices] Logger<AnitomyEpisodeParser> anitomyLog,
        [FromServices] Logger<BasicEpisodeParser> basicLog,
        [FromServices] Logger<TorrentEpisodeParser> torrentLog,
        CancellationToken cancellationToken,
        [FromQuery] Guid? seriesId = null)
    {
        var target = GetTarget(itemId, seriesId);
        if (target is null) return NotFound();
        var episodes = GetSeriesEpisodes(target.SeriesId)
            .Where(episode => !string.IsNullOrWhiteSpace(episode.Path) && IsPathInDirectory(episode.Path, target.Path))
            .ToList();
        var sample = episodes.Count == 0 ? null : episodes[Random.Shared.Next(episodes.Count)];
        if (sample is null) return Ok(new { Message = "此目录下没有可预览的剧集。" });
        var info = new EpisodeInfo
        {
            Path = sample.Path, Name = sample.Name, IndexNumber = sample.IndexNumber,
            ParentIndexNumber = sample.ParentIndexNumber,
            ProviderIds = new Dictionary<string, string>(sample.ProviderIds),
            SeriesProviderIds = sample.Series is null ? new Dictionary<string, string>() : new Dictionary<string, string>(sample.Series.ProviderIds),
        };
        var config = Plugin.Instance!.Configuration;
        var rawContext = new EpisodeParserContext(api, library, info, mediaSources, config,
            new LocalConfiguration(), cancellationToken);
        double? detected = config.EpisodeParser switch
        {
            EpisodeParserType.Basic => BasicEpisodeParser.ExtractEpisodeNumberFromPath(rawContext, basicLog),
            EpisodeParserType.AnitomySharp => new AnitomyEpisodeParser(rawContext, anitomyLog).GetEpisodeIndex(),
            _ => TorrentEpisodeParser.ExtractEpisodeNumberFromPath(rawContext, torrentLog),
        };
        return Ok(new
        {
            EpisodeId = sample.Id, FileName = Path.GetFileName(sample.Path), Parser = config.EpisodeParser.ToString(),
            DetectedIndex = detected,
            Message = detected is null ? "无法识别此文件的集数，请换一集。" : "基于已保存的解析规则识别；集数映射在本地计算，不查询 Bangumi。",
        });
    }

    [HttpPut("Configuration/{itemId:guid}")]
    public async Task<ActionResult<MediaLibraryConfiguration>> SaveConfiguration(
        Guid itemId,
        [FromBody] UpdateMediaLibraryConfiguration request,
        [FromQuery] Guid? seriesId = null)
    {
        var target = GetTarget(itemId, seriesId);
        if (target is null)
            return NotFound();
        if (request.Id < 0)
            return BadRequest("Bangumi ID 不能小于 0。");

        if (!Enum.IsDefined(request.Type))
            return BadRequest("目录类型无效。");

        if (request.Sections == null || request.Sections.Any(section => section == null ||
                string.IsNullOrWhiteSpace(section.Selector) ||
                section.Selector.IndexOfAny(['/', '\\', '\r', '\n']) >= 0))
            return BadRequest("文件名选择器不能为空，也不能包含路径分隔符或换行符。");

        if (request.Sections.Any(section => section.Id < 0 ||
                section.Type is { } type && !Enum.IsDefined(type)))
            return BadRequest("文件规则中的 Bangumi ID 或目录类型无效。");

        var configuration = new LocalConfiguration
        {
            Id = request.Id,
            Offset = request.Offset,
            Sections = request.Sections,
            Report = request.Report,
            Skip = request.Skip,
            CorrectIndex = request.CorrectIndex,
            Type = request.Type,
        };
        var configurationPath = Path.Join(target.Path, "bangumi.ini");
        await configuration.SaveTo(configurationPath);
        return Ok(CreateConfiguration(target, configuration, true));
    }

    [HttpDelete("Configuration/{itemId:guid}")]
    public ActionResult DeleteConfiguration(Guid itemId, [FromQuery] Guid? seriesId = null)
    {
        var target = GetTarget(itemId, seriesId);
        if (target is null)
            return NotFound();

        var configurationPath = Path.Join(target.Path, "bangumi.ini");
        if (System.IO.File.Exists(configurationPath))
            System.IO.File.Delete(configurationPath);
        return NoContent();
    }

    private static bool IsEditableSeries(Series item)
    {
        return !string.IsNullOrWhiteSpace(item.Path) &&
               Directory.Exists(item.Path);
    }

    private ConfigurationTarget? GetTarget(Guid itemId, Guid? seriesId)
    {
        var item = library.GetItemById(itemId);
        if (item is Series series && IsEditableSeries(series))
            return CreateTarget(series.Id, series.Name, nameof(Series), series.Path, series.Id);

        if (seriesId is { } parentId)
        {
            if (library.GetItemById(parentId) is not Series parent || !IsEditableSeries(parent))
                return null;
            var candidate = BuildPhysicalFolderItems([CreateItem(parent, [], false)], GetSeriesEpisodes(parentId), false)
                .FirstOrDefault(folder => folder.Id == itemId);
            return candidate is null || !Directory.Exists(candidate.Path) ? null
                : CreateTarget(candidate.Id, candidate.Name, candidate.Type, candidate.Path, parentId);
        }

        // Compatibility for older clients which did not send the owning series ID.
        var indexedItems = library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series, BaseItemKind.Episode],
            IsVirtualItem = false,
        });
        var seriesItems = indexedItems
            .OfType<Series>()
            .Where(IsEditableSeries)
            .Select(seriesItem => CreateItem(seriesItem, []))
            .ToList();
        var folder = BuildPhysicalFolderItems(seriesItems, indexedItems.OfType<JellyfinEpisode>())
            .FirstOrDefault(candidate => candidate.Id == itemId);
        return folder is null
            ? null
            : CreateTarget(folder.Id, folder.Name, folder.Type, folder.Path, folder.ParentId);
    }

    private static MediaLibraryItem CreateItem(BaseItem item, IReadOnlyList<LibraryFolder> libraries, bool checkConfiguration = true)
    {
        var folder = libraries.FirstOrDefault(candidate =>
            candidate.Locations.Any(location => IsPathInDirectory(item.Path, location)));
        return new MediaLibraryItem
        {
            Id = item.Id,
            Name = item.Name ?? Path.GetFileName(item.Path),
            SeriesName = item.Name ?? string.Empty,
            Type = item.GetBaseItemKind().ToString(),
            Path = item.Path,
            HasConfiguration = checkConfiguration && System.IO.File.Exists(Path.Join(item.Path, "bangumi.ini")),
            LibraryId = folder?.Id ?? string.Empty,
            LibraryName = folder?.Name ?? "未分组",
        };
    }

    internal static List<MediaLibraryItem> BuildPhysicalFolderItems(
        IReadOnlyList<MediaLibraryItem> seriesItems,
        IEnumerable<JellyfinEpisode> episodes,
        bool checkStorage = true)
    {
        var comparer = GetPathComparer();
        var seriesById = seriesItems.ToDictionary(series => series.Id);
        var seriesByPath = seriesItems.GroupBy(series => Path.GetFullPath(series.Path), comparer)
            .ToDictionary(group => group.Key, group => group.First(), comparer);
        var seen = new HashSet<string>(comparer);
        var folders = new List<MediaLibraryItem>();
        foreach (var episode in episodes)
        {
            if (string.IsNullOrWhiteSpace(episode.Path)) continue;
            var directory = Path.GetDirectoryName(episode.Path);
            if (string.IsNullOrWhiteSpace(directory)) continue;
            directory = Path.GetFullPath(directory);
            if (!seen.Add(directory)) continue;

            seriesById.TryGetValue(episode.SeriesId, out var parent);
            if (parent is null)
            {
                // Some old index entries have no SeriesId. Walk the directory's
                // ancestors instead of scanning and sorting every series path.
                for (var ancestor = directory; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
                    if (seriesByPath.TryGetValue(ancestor, out parent)) break;
            }
            if (parent is null || PathsEqual(directory, parent.Path)) continue;
            if (checkStorage && !Directory.Exists(directory)) continue;
            folders.Add(new MediaLibraryItem
            {
                Id = CreatePathId(directory), ParentId = parent.Id,
                Name = Path.GetFileName(directory), SeriesName = parent.Name,
                Type = "Folder", Path = directory,
                HasConfiguration = checkStorage && System.IO.File.Exists(Path.Join(directory, "bangumi.ini")),
                LibraryId = parent.LibraryId, LibraryName = parent.LibraryName,
            });
        }
        return folders.OrderBy(folder => folder.SeriesName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    internal static List<MediaLibraryItem> BuildTree(
        IReadOnlyList<MediaLibraryItem> items,
        string? search)
    {
        var foldersBySeries = items
            .Where(item => item.Type != nameof(Series) && item.ParentId != Guid.Empty)
            .GroupBy(item => item.ParentId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList());
        var roots = new List<MediaLibraryItem>();

        foreach (var series in items.Where(item => item.Type == nameof(Series)))
        {
            foldersBySeries.TryGetValue(series.Id, out var folders);
            folders ??= [];
            var seriesMatches = MatchesSearch(series, search);
            var matchingFolders = string.IsNullOrWhiteSpace(search) || seriesMatches
                ? folders
                : folders.Where(item => MatchesSearch(item, search)).ToList();
            if (!seriesMatches && matchingFolders.Count == 0)
                continue;

            series.Children = matchingFolders;
            roots.Add(series);
        }

        var knownSeriesIds = items
            .Where(item => item.Type == nameof(Series))
            .Select(item => item.Id)
            .ToHashSet();
        roots.AddRange(items.Where(item =>
            item.Type != nameof(Series) &&
            !knownSeriesIds.Contains(item.ParentId) &&
            MatchesSearch(item, search)));

        return roots
            .OrderBy(item => item.LibraryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.SeriesName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Type == nameof(Series) ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool MatchesSearch(MediaLibraryItem item, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return item.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               item.SeriesName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               item.Path.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool IsPathInDirectory(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            return false;

        var relativePath = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return relativePath == "." ||
               (!relativePath.Equals("..", StringComparison.Ordinal) &&
                !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !Path.IsPathRooted(relativePath));
    }

    private static bool PathsEqual(string first, string second)
    {
        return GetPathComparer().Equals(Path.GetFullPath(first), Path.GetFullPath(second));
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static Guid CreatePathId(string path)
    {
        var normalizedPath = OperatingSystem.IsWindows()
            ? Path.GetFullPath(path).ToUpperInvariant()
            : Path.GetFullPath(path);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static ConfigurationTarget CreateTarget(Guid id, string? name, string type, string path, Guid seriesId)
    {
        return new ConfigurationTarget
        {
            Id = id,
            SeriesId = seriesId,
            Name = name ?? Path.GetFileName(path),
            Type = type,
            Path = path,
        };
    }

    private static MediaLibraryConfiguration CreateConfiguration(
        ConfigurationTarget item,
        LocalConfiguration configuration,
        bool exists)
    {
        var result = new MediaLibraryConfiguration
        {
            ItemId = item.Id,
            ItemName = item.Name,
            ItemType = item.Type,
            DirectoryPath = item.Path,
            ConfigurationPath = Path.Join(item.Path, "bangumi.ini"),
            Exists = exists,
            Id = configuration.Id,
            Offset = configuration.Offset,
            Report = configuration.Report,
            Skip = configuration.Skip,
            CorrectIndex = configuration.CorrectIndex,
            Type = configuration.Type,
        };
        foreach (var section in configuration.Sections)
            result.Sections.Add(section);
        return result;
    }

    private sealed class LibraryFolder
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string[] Locations { get; init; } = [];
    }

    private sealed class ConfigurationTarget
    {
        public Guid SeriesId { get; init; }

        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Type { get; init; } = string.Empty;

        public string Path { get; init; } = string.Empty;
    }
}

public class MediaLibraryItemsResult
{
    public IEnumerable<MediaLibraryInfo> Libraries { get; set; } = [];

    public IEnumerable<MediaLibraryItem> Items { get; set; } = [];

    public int TotalRecordCount { get; set; }

    public int TotalItemCount { get; set; }

    public int StartIndex { get; set; }
}

public class MediaLibraryInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public class MediaLibraryItem
{
    public Guid Id { get; set; }

    public Guid ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string SeriesName { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public bool HasConfiguration { get; set; }

    public string LibraryId { get; set; } = string.Empty;

    public string LibraryName { get; set; } = string.Empty;

    public IEnumerable<MediaLibraryItem> Children { get; set; } = [];
}

public class MediaLibraryConfiguration
{
    public Guid ItemId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public string ItemType { get; set; } = string.Empty;

    public string DirectoryPath { get; set; } = string.Empty;

    public string ConfigurationPath { get; set; } = string.Empty;

    public bool Exists { get; set; }

    public int Id { get; set; }

    public int Offset { get; set; }

    public Collection<LocalConfigurationSection> Sections { get; } = [];

    public bool Report { get; set; }

    public bool Skip { get; set; }

    public bool CorrectIndex { get; set; }

    public DirectoryType Type { get; set; }
}

public class UpdateMediaLibraryConfiguration
{
    public int Id { get; set; }

    public int Offset { get; set; }

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only",
        Justification = "A public setter is required for request-body JSON deserialization.")]
    public Collection<LocalConfigurationSection> Sections { get; set; } = [];

    public bool Report { get; set; } = true;

    public bool Skip { get; set; }

    public bool CorrectIndex { get; set; }

    public DirectoryType Type { get; set; }
}
