using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
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
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 500;

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
                Id = folder.ItemId.ToString(),
                Name = folder.Name ?? string.Empty,
                Locations = folder.Locations ?? [],
            })
            .OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var indexedItems = library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series, BaseItemKind.Episode],
            IsVirtualItem = false,
        });
        var seriesItems = indexedItems
            .OfType<Series>()
            .Where(IsEditableSeries)
            .Select(item => CreateItem(item, virtualFolders))
            .Where(item => string.IsNullOrWhiteSpace(libraryId) ||
                           string.Equals(item.LibraryId, libraryId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var physicalFolders = BuildPhysicalFolderItems(seriesItems, indexedItems.OfType<JellyfinEpisode>());
        var items = seriesItems.Concat(physicalFolders).ToList();

        var tree = BuildTree(items, search);
        return Ok(new MediaLibraryItemsResult
        {
            Libraries = virtualFolders.Select(folder => new MediaLibraryInfo
            {
                Id = folder.Id,
                Name = folder.Name,
            }),
            Items = tree.Skip(startIndex).Take(limit),
            TotalRecordCount = tree.Count,
            TotalItemCount = tree.Sum(CountTreeItems),
            StartIndex = startIndex,
        });
    }

    [HttpGet("Configuration/{itemId:guid}")]
    public async Task<ActionResult<MediaLibraryConfiguration>> GetConfiguration(Guid itemId)
    {
        var target = GetTarget(itemId);
        if (target is null)
            return NotFound();

        var configurationPath = Path.Join(target.Path, "bangumi.ini");
        var configuration = new LocalConfiguration();
        await configuration.ReadFrom(configurationPath);
        return Ok(CreateConfiguration(target, configuration, System.IO.File.Exists(configurationPath)));
    }

    [HttpPut("Configuration/{itemId:guid}")]
    public async Task<ActionResult<MediaLibraryConfiguration>> SaveConfiguration(
        Guid itemId,
        [FromBody] UpdateMediaLibraryConfiguration request)
    {
        var target = GetTarget(itemId);
        if (target is null)
            return NotFound();
        if (request.Id < 0)
            return BadRequest("Bangumi ID 不能小于 0。");

        var configuration = new LocalConfiguration
        {
            Id = request.Id,
            Offset = request.Offset,
            Report = request.Report,
            Skip = request.Skip,
            CorrectIndex = request.CorrectIndex,
        };
        var configurationPath = Path.Join(target.Path, "bangumi.ini");
        await configuration.SaveTo(configurationPath);
        return Ok(CreateConfiguration(target, configuration, true));
    }

    [HttpDelete("Configuration/{itemId:guid}")]
    public ActionResult DeleteConfiguration(Guid itemId)
    {
        var target = GetTarget(itemId);
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

    private ConfigurationTarget? GetTarget(Guid itemId)
    {
        var item = library.GetItemById(itemId);
        if (item is Series series && IsEditableSeries(series))
            return CreateTarget(series.Id, series.Name, nameof(Series), series.Path);

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
            : CreateTarget(folder.Id, folder.Name, folder.Type, folder.Path);
    }

    private static MediaLibraryItem CreateItem(BaseItem item, IReadOnlyList<LibraryFolder> libraries)
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
            HasConfiguration = System.IO.File.Exists(Path.Join(item.Path, "bangumi.ini")),
            LibraryId = folder?.Id ?? string.Empty,
            LibraryName = folder?.Name ?? "未分组",
        };
    }

    internal static List<MediaLibraryItem> BuildPhysicalFolderItems(
        IReadOnlyList<MediaLibraryItem> seriesItems,
        IEnumerable<JellyfinEpisode> episodes)
    {
        var folders = new Dictionary<string, MediaLibraryItem>(GetPathComparer());

        foreach (var episode in episodes)
        {
            if (string.IsNullOrWhiteSpace(episode.Path))
                continue;
            var directory = Path.GetDirectoryName(episode.Path);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                continue;

            var parent = seriesItems.FirstOrDefault(series => series.Id == episode.Series?.Id) ??
                         seriesItems
                             .Where(series => IsPathInDirectory(directory, series.Path))
                             .OrderByDescending(series => series.Path.Length)
                             .FirstOrDefault();
            if (parent is null || PathsEqual(directory, parent.Path))
                continue;

            var normalizedDirectory = Path.GetFullPath(directory);
            folders.TryAdd(normalizedDirectory, new MediaLibraryItem
            {
                Id = CreatePathId(normalizedDirectory),
                ParentId = parent.Id,
                Name = Path.GetFileName(normalizedDirectory),
                SeriesName = parent.Name,
                Type = "Folder",
                Path = normalizedDirectory,
                HasConfiguration = System.IO.File.Exists(Path.Join(normalizedDirectory, "bangumi.ini")),
                LibraryId = parent.LibraryId,
                LibraryName = parent.LibraryName,
            });
        }

        return folders.Values
            .OrderBy(folder => folder.SeriesName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
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

    private static int CountTreeItems(MediaLibraryItem item)
    {
        return 1 + item.Children.Sum(CountTreeItems);
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

    private static ConfigurationTarget CreateTarget(Guid id, string? name, string type, string path)
    {
        return new ConfigurationTarget
        {
            Id = id,
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
        return new MediaLibraryConfiguration
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
        };
    }

    private sealed class LibraryFolder
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string[] Locations { get; init; } = [];
    }

    private sealed class ConfigurationTarget
    {
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

    public bool Report { get; set; }

    public bool Skip { get; set; }

    public bool CorrectIndex { get; set; }
}

public class UpdateMediaLibraryConfiguration
{
    public int Id { get; set; }

    public int Offset { get; set; }

    public bool Report { get; set; } = true;

    public bool Skip { get; set; }

    public bool CorrectIndex { get; set; }
}
