using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Controller.Sorting;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.Bangumi.Test.Mock;

#pragma warning disable CS0067

public class MockedLibraryManager : ILibraryManager
{
    public BaseItem? ResolvePath(FileSystemMetadata fileInfo, Folder? parent = null, IDirectoryService? directoryService = null)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<BaseItem> ResolvePaths(IEnumerable<FileSystemMetadata> files, IDirectoryService directoryService, Folder parent, LibraryOptions libraryOptions, CollectionType? collectionType = null)
    {
        throw new NotImplementedException();
    }

    public MediaBrowser.Controller.Entities.Person? GetPerson(string name)
    {
        throw new NotImplementedException();
    }

    public BaseItem? FindByPath(string path, bool? isFolder)
    {
        return null;
    }

    public MusicArtist GetArtist(string name)
    {
        throw new NotImplementedException();
    }

    public MusicArtist GetArtist(string name, DtoOptions options)
    {
        throw new NotImplementedException();
    }

    public Studio GetStudio(string name)
    {
        throw new NotImplementedException();
    }

    public Genre GetGenre(string name)
    {
        throw new NotImplementedException();
    }

    public MusicGenre GetMusicGenre(string name)
    {
        throw new NotImplementedException();
    }

    public Year GetYear(int value)
    {
        throw new NotImplementedException();
    }

    public Task ValidatePeopleAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task ValidateMediaLibrary(IProgress<double> progress, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task UpdateImagesAsync(BaseItem item, bool forceUpdate = false)
    {
        throw new NotImplementedException();
    }

    public List<VirtualFolderInfo> GetVirtualFolders()
    {
        throw new NotImplementedException();
    }

    public List<VirtualFolderInfo> GetVirtualFolders(bool includeRefreshState)
    {
        throw new NotImplementedException();
    }

    public BaseItem? GetItemById(Guid id)
    {
        throw new NotImplementedException();
    }

    public T? GetItemById<T>(Guid id) where T : BaseItem
    {
        throw new NotImplementedException();
    }

    public T? GetItemById<T>(Guid id, Guid userId) where T : BaseItem
    {
        throw new NotImplementedException();
    }

    public T? GetItemById<T>(Guid id, User? user) where T : BaseItem
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<Video>> GetIntros(BaseItem item, User user)
    {
        throw new NotImplementedException();
    }

    public void AddParts(IEnumerable<IResolverIgnoreRule> rules, IEnumerable<IItemResolver> resolvers, IEnumerable<IIntroProvider> introProviders, IEnumerable<IBaseItemComparer> itemComparers, IEnumerable<ILibraryPostScanTask> postscanTasks)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<BaseItem> Sort(IEnumerable<BaseItem> items, User? user, IEnumerable<ItemSortBy> sortBy, SortOrder sortOrder)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<BaseItem> Sort(IEnumerable<BaseItem> items, User? user, IEnumerable<(ItemSortBy OrderBy, SortOrder SortOrder)> orderBy)
    {
        throw new NotImplementedException();
    }

    public Folder GetUserRootFolder()
    {
        throw new NotImplementedException();
    }

    public void CreateItem(BaseItem item, BaseItem? parent)
    {
        throw new NotImplementedException();
    }

    public void CreateItems(IReadOnlyList<BaseItem> items, BaseItem? parent, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task UpdateItemsAsync(IReadOnlyList<BaseItem> items, BaseItem parent, ItemUpdateType updateReason, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task UpdateItemAsync(BaseItem item, BaseItem parent, ItemUpdateType updateReason, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public BaseItem RetrieveItem(Guid id)
    {
        throw new NotImplementedException();
    }

    public CollectionType? GetContentType(BaseItem item)
    {
        throw new NotImplementedException();
    }

    public CollectionType? GetInheritedContentType(BaseItem item)
    {
        throw new NotImplementedException();
    }

    public CollectionType? GetConfiguredContentType(BaseItem item)
    {
        throw new NotImplementedException();
    }

    public CollectionType? GetConfiguredContentType(string path)
    {
        throw new NotImplementedException();
    }

    public List<FileSystemMetadata> NormalizeRootPathList(IEnumerable<FileSystemMetadata> paths)
    {
        throw new NotImplementedException();
    }

    public void RegisterItem(BaseItem item)
    {
        throw new NotImplementedException();
    }

    public void DeleteItem(BaseItem item, DeleteOptions options)
    {
        throw new NotImplementedException();
    }

    public void DeleteItem(BaseItem item, DeleteOptions options, bool notifyParentItem)
    {
        throw new NotImplementedException();
    }

    public void DeleteItem(BaseItem item, DeleteOptions options, BaseItem parent, bool notifyParentItem)
    {
        throw new NotImplementedException();
    }

    public UserView GetNamedView(User user, string name, Guid parentId, CollectionType? viewType, string sortName)
    {
        throw new NotImplementedException();
    }

    public UserView GetNamedView(User user, string name, CollectionType? viewType, string sortName)
    {
        throw new NotImplementedException();
    }

    public UserView GetNamedView(string name, CollectionType viewType, string sortName)
    {
        throw new NotImplementedException();
    }

    public UserView GetNamedView(string name, Guid parentId, CollectionType? viewType, string sortName, string uniqueId)
    {
        throw new NotImplementedException();
    }

    public UserView GetShadowView(BaseItem parent, CollectionType? viewType, string sortName)
    {
        throw new NotImplementedException();
    }

    public int? GetSeasonNumberFromPath(string path)
    {
        throw new NotImplementedException();
    }

    public bool FillMissingEpisodeNumbersFromPath(MediaBrowser.Controller.Entities.TV.Episode episode, bool forceRefresh)
    {
        throw new NotImplementedException();
    }

    public ItemLookupInfo ParseName(string name)
    {
        throw new NotImplementedException();
    }

    public Guid GetNewItemId(string key, Type type)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<BaseItem> FindExtras(BaseItem owner, IReadOnlyList<FileSystemMetadata> fileSystemChildren, IDirectoryService directoryService)
    {
        throw new NotImplementedException();
    }

    public List<Folder> GetCollectionFolders(BaseItem item)
    {
        throw new NotImplementedException();
    }

    public List<Folder> GetCollectionFolders(BaseItem item, IEnumerable<Folder> allUserRootChildren)
    {
        throw new NotImplementedException();
    }

    public LibraryOptions GetLibraryOptions(BaseItem item)
    {
        throw new NotImplementedException();
    }

    public List<PersonInfo> GetPeople(BaseItem item)
    {
        throw new NotImplementedException();
    }

    public List<PersonInfo> GetPeople(InternalPeopleQuery query)
    {
        throw new NotImplementedException();
    }

    public List<MediaBrowser.Controller.Entities.Person> GetPeopleItems(InternalPeopleQuery query)
    {
        throw new NotImplementedException();
    }

    public void UpdatePeople(BaseItem item, List<PersonInfo> people)
    {
        throw new NotImplementedException();
    }

    public Task UpdatePeopleAsync(BaseItem item, List<PersonInfo> people, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public List<Guid> GetItemIds(InternalItemsQuery query)
    {
        throw new NotImplementedException();
    }

    public List<string> GetPeopleNames(InternalPeopleQuery query)
    {
        throw new NotImplementedException();
    }

    public QueryResult<BaseItem> QueryItems(InternalItemsQuery query)
    {
        throw new NotImplementedException();
    }

    public string GetPathAfterNetworkSubstitution(string path, BaseItem? ownerItem = null)
    {
        throw new NotImplementedException();
    }

    public Task<ItemImageInfo> ConvertImageToLocal(BaseItem item, ItemImageInfo image, int imageIndex, bool removeOnFailure = true)
    {
        throw new NotImplementedException();
    }

    public List<BaseItem> GetItemList(InternalItemsQuery query)
    {
        throw new NotImplementedException();
    }

    public List<BaseItem> GetItemList(InternalItemsQuery query, bool allowExternalContent)
    {
        throw new NotImplementedException();
    }

    public List<BaseItem> GetItemList(InternalItemsQuery query, List<BaseItem> parents)
    {
        throw new NotImplementedException();
    }

    public QueryResult<BaseItem> GetItemsResult(InternalItemsQuery query)
    {
        throw new NotImplementedException();
    }

    public bool IgnoreFile(FileSystemMetadata file, BaseItem parent)
    {
        throw new NotImplementedException();
    }

    public Guid GetStudioId(string name)
    {
        throw new NotImplementedException();
    }

    public Guid GetGenreId(string name)
    {
        throw new NotImplementedException();
    }

    public Guid GetMusicGenreId(string name)
    {
        throw new NotImplementedException();
    }

    public Task AddVirtualFolder(string name, CollectionTypeOptions? collectionType, LibraryOptions options, bool refreshLibrary)
    {
        throw new NotImplementedException();
    }

    public Task RemoveVirtualFolder(string name, bool refreshLibrary)
    {
        throw new NotImplementedException();
    }

    public void AddMediaPath(string virtualFolderName, MediaPathInfo mediaPath)
    {
        throw new NotImplementedException();
    }

    public void UpdateMediaPath(string virtualFolderName, MediaPathInfo mediaPath)
    {
        throw new NotImplementedException();
    }

    public void RemoveMediaPath(string virtualFolderName, string mediaPath)
    {
        throw new NotImplementedException();
    }

    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetGenres(InternalItemsQuery query)
    {
        throw new NotImplementedException();
    }

    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetMusicGenres(InternalItemsQuery query)
    {
        throw new NotImplementedException();
    }

    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetStudios(InternalItemsQuery query)
    {
        throw new NotImplementedException();
    }

    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetArtists(InternalItemsQuery query)
    {
        throw new NotImplementedException();
    }

    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetAlbumArtists(InternalItemsQuery query)
    {
        throw new NotImplementedException();
    }

    public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetAllArtists(InternalItemsQuery query)
    {
        throw new NotImplementedException();
    }

    public int GetCount(InternalItemsQuery query)
    {
        throw new NotImplementedException();
    }

    public Task RunMetadataSavers(BaseItem item, ItemUpdateType updateReason)
    {
        throw new NotImplementedException();
    }

    public BaseItem GetParentItem(Guid? parentId, Guid? userId)
    {
        throw new NotImplementedException();
    }

    public void QueueLibraryScan()
    {
        throw new NotImplementedException();
    }

    public AggregateFolder RootFolder { get; } = new();
    public bool IsScanRunning { get; }
    public event EventHandler<ItemChangeEventArgs>? ItemAdded;
    public event EventHandler<ItemChangeEventArgs>? ItemUpdated;
    public event EventHandler<ItemChangeEventArgs>? ItemRemoved;
}
