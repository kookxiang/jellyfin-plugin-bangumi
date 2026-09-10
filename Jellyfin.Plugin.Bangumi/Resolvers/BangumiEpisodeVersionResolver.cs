using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using AnitomySharp;
using EpisodeType = Jellyfin.Plugin.Bangumi.Model.EpisodeType;
using LocalConfiguration = Jellyfin.Plugin.Bangumi.Model.LocalConfiguration;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;
using Emby.Naming.Common;
using Emby.Naming.Video;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.BaseItemManager;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Bangumi.Resolvers;

/// <summary>
/// Requires both persisted Bangumi IDs and matching path identities for local version groups.
/// Jellyfin discovers IItemResolver exports and runs Plugin priority before its built-in resolver.
/// </summary>
public partial class BangumiEpisodeVersionResolver(
    ILibraryManager library,
    IBaseItemManager baseItemManager,
    NamingOptions namingOptions) : IItemResolver, IMultiItemResolver
{
    public ResolverPriority Priority => ResolverPriority.Plugin;

    // Leave individual files and directories to Jellyfin's normal resolvers.
    public BaseItem? ResolvePath(ItemResolveArgs args) => null;

    public MultiItemResolverResult ResolveMultiple(
        Folder parent,
        List<FileSystemMetadata> files,
        CollectionType? collectionType,
        IDirectoryService directoryService)
    {
        if (Plugin.Instance?.Configuration.MergeEpisodeVersionsByBangumiId != true
            || parent is null || parent.IsRoot || collectionType != CollectionType.tvshows)
            return null!;

        var options = library.GetLibraryOptions(parent).GetTypeOptions(nameof(Episode));
        if (options is null || !baseItemManager.IsMetadataFetcherEnabled(new Episode(), options, Constants.ProviderName))
            return null!;

        // Keep Jellyfin's multipart and extra detection, but disable its filename-based version grouping.
        var videos = files.Where(file => !file.IsDirectory && !SampleRegex().IsMatch(Path.GetFileName(file.FullName)))
            .Select(file => VideoResolver.Resolve(file.FullName, false, namingOptions, true, parent.ContainingFolderPath))
            .OfType<VideoFileInfo>().ToArray();
        var standalone = new VideoListResolver(namingOptions).Resolve(videos,
            supportMultiVersion: false, parseName: true, libraryRoot: parent.ContainingFolderPath, collectionType: collectionType);
        var result = new MultiItemResolverResult();
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var groups = new Dictionary<(int Id, FileIdentity Identity), Episode>();
        var localConfiguration = LocalConfiguration.ForPath(parent.Path).GetAwaiter().GetResult();
        var savedEpisodes = new Dictionary<string, Episode>(StringComparer.Ordinal);

        foreach (var video in standalone.OrderBy(video => video.Files[0].Path, StringComparer.Ordinal))
        {
            if (video.ExtraType != null)
                continue;
            var path = video.Files[0].Path;
            var file = files.First(entry => string.Equals(entry.FullName, path, StringComparison.Ordinal));
            // ResolvePath invokes single-item resolvers, so this does not recursively call ResolveMultiple.
            if (library.ResolvePath(file, parent, directoryService, collectionType) is not Episode episode)
                continue;

            episode.IsInMixedFolder = standalone.Count > 1 || parent.IsTopParent;
            episode.AdditionalParts = video.Files.Skip(1).Select(part => part.Path).ToArray();
            episode.LocalAlternateVersions = [];
            foreach (var part in video.Files)
                consumed.Add(part.Path);

            // Never query Bangumi during a synchronous filesystem scan. A later scan can use IDs
            // obtained by metadata refresh after the first scan creates independent episodes.
            var saved = library.FindByPath(path, false) as Episode;
            if (saved != null)
                savedEpisodes[path] = saved;
            var hasId = int.TryParse(saved?.GetProviderId(Constants.ProviderName), out var id) && id > 0;
            var identity = GetFileIdentity(path, localConfiguration);
            var canGroup = hasId && identity != null;
            var key = (id, identity.GetValueOrDefault());
            if (canGroup && groups.TryGetValue(key, out var primary))
            {
                primary.LocalAlternateVersions = [.. primary.LocalAlternateVersions, path];
            }
            else
            {
                result.Items.Add(episode);
                if (canGroup)
                    groups.Add(key, episode);
            }
        }

        // Directories, subtitles, trailers and unsupported files retain native single-item handling.
        result.ExtraFiles = files.Where(file => !consumed.Contains(file.FullName)).ToList();
        DetachChangedLocalVersions(parent, result.Items.Cast<Episode>().ToArray(), savedEpisodes);
        return result.Items.Count > 0 ? result : null!;
    }

    internal readonly record struct FileIdentity(int Season, decimal Number, EpisodeType Type);

    internal static FileIdentity? GetFileIdentity(string path, LocalConfiguration configuration)
    {
        // Read only the path, never IndexNumber/ParentIndexNumber or API metadata copied by Jellyfin.
        // Anitomy already distinguishes release numbers from HEVC-10bit, resolution and checksums.
        var fileName = Path.GetFileName(path);
        var parsed = new Anitomy(fileName);
        var numbers = parsed.GetElements()
            .Where(element => element.Category == Element.ElementCategory.ElementEpisodeNumber)
            .Select(element => element.Value).ToArray();
        if (numbers.Length != 1 || !decimal.TryParse(numbers[0], NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number) || number < 0)
            return null;

        // A title sequel number can be mistaken for the episode (e.g. Log Horizon 2 [08]).
        // Conflicting explicit bracket numbers make the path unsafe for grouping.
        foreach (Match match in BracketEpisodeRegex().Matches(fileName))
        {
            if (!decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var bracketNumber) || bracketNumber != number)
                return null;
        }

        var seasonText = parsed.ExtractAnimeSeason()
            ?? new Anitomy(Path.GetFileName(Path.GetDirectoryName(path)) ?? "").ExtractAnimeSeason();
        var season = 1;
        if (seasonText != null && (!int.TryParse(seasonText, NumberStyles.None,
                CultureInfo.InvariantCulture, out season) || season < 0))
            return null;

        var type = configuration.GetForcedEpisodeType()
            ?? BasicEpisodeParser.GuessEpisodeTypeFromFileName(fileName)
            ?? AnitomyEpisodeTypeMapping.GetAnitomyAndBangumiEpisodeType(parsed.ExtractAnimeType()).Item2
            ?? EpisodeType.Normal;
        return new FileIdentity(season, number, type);
    }

    private void DetachChangedLocalVersions(Folder parent, Episode[] resolved, Dictionary<string, Episode> saved)
    {
        var desired = resolved.ToDictionary(episode => episode.Path,
            episode => episode.LocalAlternateVersions.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var previous in saved.Values)
        {
            var kept = desired.GetValueOrDefault(previous.Path) ?? [];
            // Only touch files actually resolved in this scan. Inaccessible or unrecognized files,
            // and manually linked versions, remain Jellyfin's responsibility.
            var dropped = previous.LocalAlternateVersions.Where(path => saved.ContainsKey(path) && !kept.Contains(path)).ToArray();
            if (dropped.Length == 0)
                continue;

            foreach (var path in dropped)
            {
                var child = saved[path];
                if (child.OwnerId == previous.Id)
                {
                    // ItemPersistenceService deletes dropped local versions still owned by the
                    // old primary. Promote them first so metadata and user data remain attached.
                    child.OwnerId = Guid.Empty;
                    child.SetPrimaryVersionId(null);
                    library.UpdateItemAsync(child, parent, ItemUpdateType.MetadataImport, CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
            }

            previous.LocalAlternateVersions = previous.LocalAlternateVersions.Except(dropped, StringComparer.Ordinal).ToArray();
            library.UpdateItemAsync(previous, parent, ItemUpdateType.MetadataImport, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
    }

    [GeneratedRegex(@"\[(\d+(?:\.\d+)?)\]")]
    private static partial Regex BracketEpisodeRegex();

    [GeneratedRegex(@"\bsample\b", RegexOptions.IgnoreCase)]
    private static partial Regex SampleRegex();
}
