using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Querying;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using Jellyfin.Plugin.Bangumi.Test.Util;
using Jellyfin.Plugin.Bangumi.Tools.MediaLibrary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JellyfinEpisode = MediaBrowser.Controller.Entities.TV.Episode;
using MediaLibraryController = Jellyfin.Plugin.Bangumi.Tools.MediaLibrary.Controller;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class MediaLibraryTestCases
{
    [TestMethod]
    public void ItemsIncludesLibrariesWithoutIndexedIds()
    {
        var library = new MockedLibraryManager();
        library.VirtualFolders.Add(new MediaBrowser.Model.Entities.VirtualFolderInfo { Name = "Unindexed" });
        library.VirtualFolders.Add(new MediaBrowser.Model.Entities.VirtualFolderInfo { Name = "Empty ID", ItemId = "" });
        var id = Guid.NewGuid().ToString("N");
        library.VirtualFolders.Add(new MediaBrowser.Model.Entities.VirtualFolderInfo { Name = "Anime", ItemId = id });
        var controller = new MediaLibraryController(library);
        var response = controller.GetItems(null, null).Result as OkObjectResult;
        Assert.IsNotNull(response);
        var result = response.Value as MediaLibraryItemsResult;
        Assert.IsNotNull(result);
        Assert.AreEqual(3, result.Libraries.Count());
        Assert.AreEqual(id, result.Libraries.Single(item => item.Name == "Anime").Id);
        Assert.AreEqual("name:Unindexed", result.Libraries.Single(item => item.Name == "Unindexed").Id);
        Assert.AreEqual(0, result.TotalRecordCount);
    }

    [TestMethod]
    public void LibraryWithoutIndexedIdCanFilterSeriesByLocation()
    {
        var library = new MockedLibraryManager();
        var series = FakePath.CreateSeries(library, "media-library/library-filter");
        library.VirtualFolders.Add(new MediaBrowser.Model.Entities.VirtualFolderInfo
        {
            Name = "Anime", Locations = [series.Path],
        });
        var controller = new MediaLibraryController(library);
        var response = (OkObjectResult)controller.GetItems("name:Anime", null).Result!;
        var result = (MediaLibraryItemsResult)response.Value!;
        Assert.AreEqual(series.Id, result.Items.Single().Id);
        Assert.AreEqual("name:Anime", result.Items.Single().LibraryId);
        var otherResponse = (OkObjectResult)controller.GetItems("name:Other", null).Result!;
        Assert.AreEqual(0, ((MediaLibraryItemsResult)otherResponse.Value!).TotalRecordCount);
    }

    [TestMethod]
    public void IndexedLibraryPagesInDatabaseBeforeEnrichingItems()
    {
        var library = new MockedLibraryManager();
        var libraryId = Guid.NewGuid();
        library.VirtualFolders.Add(new MediaBrowser.Model.Entities.VirtualFolderInfo
        {
            ItemId = libraryId.ToString("N"), Name = "Anime",
        });
        var series = FakePath.CreateSeries(library, "media-library/paged-config");
        FakePath.CreateFile("media-library/paged-config/bangumi.ini");
        var calls = 0;
        library.ItemQuery = _ => throw new AssertFailedException("Listing must not fetch all series or episodes.");
        library.ItemsResultQuery = query =>
        {
            calls++;
            Assert.AreEqual(libraryId, query.ParentId);
            Assert.IsTrue(query.Recursive);
            CollectionAssert.AreEqual(new[] { BaseItemKind.Series }, query.IncludeItemTypes);
            Assert.AreEqual(20, query.StartIndex);
            Assert.AreEqual(20, query.Limit);
            return new QueryResult<BaseItem>(20, 45, new BaseItem[] { series });
        };
        var response = (OkObjectResult)new MediaLibraryController(library)
            .GetItems(libraryId.ToString("N"), null, 20, 500).Result!;
        var result = (MediaLibraryItemsResult)response.Value!;
        Assert.AreEqual(1, calls);
        Assert.AreEqual(45, result.TotalRecordCount);
        Assert.AreEqual(20, result.StartIndex);
        Assert.IsTrue(result.Items.Single().HasConfiguration);
        Assert.AreEqual(0, result.Items.Single().Children.Count());
    }

    [TestMethod]
    public void DefaultPageContainsTwentySeriesAndNoEpisodes()
    {
        var library = new MockedLibraryManager();
        for (var i = 0; i < 45; i++)
        {
            var series = FakePath.CreateSeries(library, $"media-library/pagination/{i:00}");
            series.Name = $"Show {i:00}";
            library.CreateItem(new JellyfinEpisode { Path = Path.Join(series.Path, "Season 1", "01.mkv") }, series);
        }
        var controller = new MediaLibraryController(library);
        var result = (MediaLibraryItemsResult)((OkObjectResult)controller.GetItems(null, null).Result!).Value!;
        Assert.AreEqual(45, result.TotalRecordCount);
        Assert.AreEqual(20, result.Items.Count());
        Assert.IsTrue(result.Items.All(item => item.Type == "Series" && !item.Children.Any()));
        var second = (MediaLibraryItemsResult)((OkObjectResult)controller.GetItems(null, null, 20).Result!).Value!;
        Assert.AreEqual(20, second.Items.Count());
        Assert.IsFalse(result.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)).Any());
    }

    [TestMethod]
    public void FolderBrowsingQueriesOnlyItsSeriesAndPagesDistinctDirectories()
    {
        var library = new MockedLibraryManager();
        var series = FakePath.CreateSeries(library, "media-library/lazy-folders");
        var episodes = Enumerable.Range(0, 25).SelectMany(index => new[]
        {
            new JellyfinEpisode { Path = Path.Join(series.Path, $"Part {index:00}", "01.mkv"), SeriesId = series.Id },
            new JellyfinEpisode { Path = Path.Join(series.Path, $"Part {index:00}", "02.mkv"), SeriesId = series.Id },
        }).ToArray();
        FakePath.CreateFile("media-library/lazy-folders/Part 20/bangumi.ini");
        library.ItemQuery = query =>
        {
            Assert.AreEqual(series.Id, query.ParentId);
            Assert.IsTrue(query.Recursive);
            CollectionAssert.AreEqual(new[] { BaseItemKind.Episode }, query.IncludeItemTypes);
            return episodes;
        };
        var result = (MediaLibraryItemsResult)((OkObjectResult)new MediaLibraryController(library)
            .GetFolders(series.Id, startIndex: 20).Result!).Value!;
        Assert.AreEqual(25, result.TotalRecordCount);
        Assert.AreEqual(5, result.Items.Count());
        Assert.AreEqual("Part 20", result.Items.First().Name);
        Assert.IsTrue(result.Items.First().HasConfiguration);
        Assert.IsTrue(result.Items.All(item => item.ParentId == series.Id));
    }

    [TestMethod]
    public void SearchByPhysicalFolderKeepsOwningSeriesWithoutLoadingStorage()
    {
        var library = new MockedLibraryManager();
        var libraryId = Guid.NewGuid();
        library.VirtualFolders.Add(new MediaBrowser.Model.Entities.VirtualFolderInfo
        {
            ItemId = libraryId.ToString("N"), Name = "Anime",
        });
        var series = FakePath.CreateSeries(library, "media-library/search-folder");
        series.Name = "Example";
        library.ItemQuery = query =>
        {
            Assert.AreEqual(libraryId, query.ParentId);
            Assert.IsTrue(query.Recursive);
            return query.IncludeItemTypes.Contains(BaseItemKind.Series) ? new BaseItem[] { series }
                : new BaseItem[] { new JellyfinEpisode
                {
                    Path = Path.Join(series.Path, "Unloaded Season", "01.mkv"), SeriesId = series.Id,
                } };
        };
        var result = (MediaLibraryItemsResult)((OkObjectResult)new MediaLibraryController(library)
            .GetItems(libraryId.ToString("N"), "Unloaded Season").Result!).Value!;
        Assert.AreEqual(series.Id, result.Items.Single().Id);
        Assert.AreEqual(0, result.Items.Single().Children.Count());
    }

    [TestMethod]
    public void IndexedSeriesIdKeepsMergedSeriesFoldersOutsidePrimaryPath()
    {
        var id = Guid.NewGuid();
        var series = new MediaLibraryItem
        {
            Id = id, Name = "Merged Series", Type = "Series",
            Path = Path.Join(Path.GetTempPath(), "merged-series", "Season 1"),
        };
        var folderPath = Path.Join(Path.GetTempPath(), "merged-series", "Season 2");
        var folders = MediaLibraryController.BuildPhysicalFolderItems([series],
            [new JellyfinEpisode { SeriesId = id, Path = Path.Join(folderPath, "01.mkv") }], false);
        Assert.AreEqual(id, folders.Single().ParentId);
        Assert.AreEqual(folderPath, folders.Single().Path);
    }

    [TestMethod]
    public async Task ScopedFolderConfigurationNeverQueriesOtherSeries()
    {
        var library = new MockedLibraryManager();
        var series = FakePath.CreateSeries(library, "media-library/scoped-save");
        var episode = new JellyfinEpisode
        {
            SeriesId = series.Id,
            Path = FakePath.CreateFile("media-library/scoped-save/Part A/01.mkv"),
        };
        library.ItemQuery = query =>
        {
            Assert.AreEqual(series.Id, query.ParentId);
            CollectionAssert.AreEqual(new[] { BaseItemKind.Episode }, query.IncludeItemTypes);
            return new BaseItem[] { episode };
        };
        var controller = new MediaLibraryController(library);
        var folder = ((MediaLibraryItemsResult)((OkObjectResult)controller.GetFolders(series.Id).Result!).Value!)
            .Items.Single();
        var saved = await controller.SaveConfiguration(folder.Id, new UpdateMediaLibraryConfiguration { Id = 12345 }, series.Id);
        Assert.IsInstanceOfType<OkObjectResult>(saved.Result);
        var loaded = await controller.GetConfiguration(folder.Id, series.Id);
        Assert.AreEqual(12345, ((MediaLibraryConfiguration)((OkObjectResult)loaded.Result!).Value!).Id);
        Assert.IsInstanceOfType<NotFoundResult>((await controller.GetConfiguration(folder.Id, Guid.NewGuid())).Result);
        Assert.IsInstanceOfType<NoContentResult>(controller.DeleteConfiguration(folder.Id, series.Id));
    }

    [TestMethod]
    public void PreviewEmptyDirectoryDoesNotCallParser()
    {
        var library = new MockedLibraryManager();
        var series = FakePath.CreateSeries(library, "media-library/empty-preview");
        var controller = new MediaLibraryController(library);
        var result = controller.Preview(series.Id, null!, null!, null!, null!, null!, System.Threading.CancellationToken.None);
        Assert.IsInstanceOfType<OkObjectResult>(result);
        var missing = controller.Preview(Guid.NewGuid(), null!, null!, null!, null!, null!, System.Threading.CancellationToken.None);
        Assert.IsInstanceOfType<NotFoundResult>(missing);
    }

    [DataTestMethod]
    [DataRow(1d, 26, false, 27)]
    [DataRow(1d, 26, true, 1)]
    [DataRow(25d, -24, false, 1)]
    [DataRow(25d, -24, true, 25)]
    [DataRow(12.5d, 0, false, 12)]
    public void PreviewDisplayIndexMatchesProvider(double order, int offset, bool correct, int expected)
    {
        Assert.AreEqual(expected, Parser.LocalConfigurationHelper.GetDisplayEpisodeIndex(order,
            new Model.LocalConfiguration { Offset = offset, CorrectIndex = correct }));
    }

    [TestMethod]
    public async Task SaveAndDeleteConfiguration()
    {
        var library = new MockedLibraryManager();
        var series = FakePath.CreateSeries(library, "media-library/series");
        series.Name = "Test Series";
        var controller = new MediaLibraryController(library);

        var saveResult = await controller.SaveConfiguration(series.Id, new UpdateMediaLibraryConfiguration
        {
            Id = 12345,
            Offset = 12,
            Sections = [new Model.LocalConfigurationSection { Selector = "[某字幕组][**].mp4", Offset = 26, Skip = false }],
            Report = false,
            Skip = true,
            CorrectIndex = true,
            Type = Model.DirectoryType.Special,
        });

        var savedConfiguration = (saveResult.Result as OkObjectResult)?.Value as MediaLibraryConfiguration;
        Assert.IsNotNull(savedConfiguration);
        Assert.IsTrue(savedConfiguration.Exists);
        Assert.AreEqual(12345, savedConfiguration.Id);
        var configurationPath = Path.Join(series.Path, "bangumi.ini");
        var content = await File.ReadAllTextAsync(configurationPath);
        StringAssert.Contains(content, "ID=12345");
        StringAssert.Contains(content, "Offset=12");
        StringAssert.Contains(content, "[Section.1]");
        StringAssert.Contains(content, "Selector=[某字幕组][**].mp4");
        StringAssert.Contains(content, "Offset=26");
        StringAssert.Contains(content, "[Section.1]" + Environment.NewLine + "Selector=[某字幕组][**].mp4" + Environment.NewLine + "Offset=26" + Environment.NewLine + "Skip=off");
        StringAssert.Contains(content, "Report=off");
        StringAssert.Contains(content, "Skip=on");
        StringAssert.Contains(content, "CorrectIndex=on");
        StringAssert.Contains(content, "Type=Special");
        Assert.AreEqual(Model.DirectoryType.Special, savedConfiguration.Type);
        var loaded = (await controller.GetConfiguration(series.Id)).Result as OkObjectResult;
        Assert.AreEqual(Model.DirectoryType.Special, ((MediaLibraryConfiguration)loaded!.Value!).Type);
        Assert.AreEqual(26, ((MediaLibraryConfiguration)loaded.Value!).Sections.Single().Offset);
        Assert.AreEqual(false, ((MediaLibraryConfiguration)loaded.Value!).Sections.Single().Skip);
        var episodePath = FakePath.CreateFile("media-library/series/[某字幕组][27].mp4");
        var selected = await Model.LocalConfiguration.ForPath(episodePath);
        Assert.AreEqual(26, selected.Offset);
        Assert.IsFalse(selected.Skip);

        var deleteResult = controller.DeleteConfiguration(series.Id);
        Assert.IsInstanceOfType<NoContentResult>(deleteResult);
        Assert.IsFalse(File.Exists(configurationPath));
    }

    [TestMethod]
    public async Task RejectsNegativeBangumiId()
    {
        var library = new MockedLibraryManager();
        var series = FakePath.CreateSeries(library, "media-library/invalid-id");
        var controller = new MediaLibraryController(library);

        var result = await controller.SaveConfiguration(series.Id, new UpdateMediaLibraryConfiguration
        {
            Id = -1,
        });

        Assert.IsInstanceOfType<BadRequestObjectResult>(result.Result);
        Assert.IsFalse(File.Exists(Path.Join(series.Path, "bangumi.ini")));
    }

    [TestMethod]
    public async Task OmitsZeroNumericValuesFromConfiguration()
    {
        var library = new MockedLibraryManager();
        var series = FakePath.CreateSeries(library, "media-library/default-numeric-values");
        var controller = new MediaLibraryController(library);

        var result = await controller.SaveConfiguration(series.Id, new UpdateMediaLibraryConfiguration
        {
            Id = 0,
            Offset = 0,
            Report = false,
        });

        Assert.IsInstanceOfType<OkObjectResult>(result.Result);
        var content = await File.ReadAllTextAsync(Path.Join(series.Path, "bangumi.ini"));
        Assert.IsFalse(content.Contains("ID=", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("Offset=", StringComparison.Ordinal));
        StringAssert.Contains(content, "Report=off");
    }

    [TestMethod]
    public void SearchTreeKeepsParentContext()
    {
        var seriesId = Guid.NewGuid();
        var items = new List<MediaLibraryItem>
        {
            new()
            {
                Id = seriesId,
                Name = "Test Series",
                SeriesName = "Test Series",
                Type = "Series",
                Path = "/media/test-series",
            },
            new()
            {
                Id = Guid.NewGuid(),
                ParentId = seriesId,
                Name = "Season 1",
                SeriesName = "Test Series",
                Type = "Season",
                Path = "/media/test-series/Season 1",
            },
            new()
            {
                Id = Guid.NewGuid(),
                ParentId = seriesId,
                Name = "Season 2",
                SeriesName = "Test Series",
                Type = "Season",
                Path = "/media/test-series/Season 2",
            },
        };

        var tree = MediaLibraryController.BuildTree(items, "Season 2");

        Assert.AreEqual(1, tree.Count);
        Assert.AreEqual("Test Series", tree[0].Name);
        var children = tree[0].Children.ToList();
        Assert.AreEqual(1, children.Count);
        Assert.AreEqual("Season 2", children[0].Name);
    }

    [TestMethod]
    public void BuildsOneNodePerPhysicalEpisodeFolder()
    {
        var seriesId = Guid.NewGuid();
        var seriesPath = FakePath.Create("media-library/multiple-folders");
        var firstEpisodePath = FakePath.CreateFile("media-library/multiple-folders/Part A/01.mkv");
        var secondEpisodePath = FakePath.CreateFile("media-library/multiple-folders/Part B/02.mkv");
        var rootEpisodePath = FakePath.CreateFile("media-library/multiple-folders/03.mkv");
        var seriesItems = new List<MediaLibraryItem>
        {
            new()
            {
                Id = seriesId,
                Name = "Test Series",
                SeriesName = "Test Series",
                Type = "Series",
                Path = seriesPath,
            },
        };
        var episodes = new List<JellyfinEpisode>
        {
            new() { Path = firstEpisodePath },
            new() { Path = secondEpisodePath },
            new() { Path = rootEpisodePath },
        };

        var folders = MediaLibraryController.BuildPhysicalFolderItems(seriesItems, episodes);

        Assert.AreEqual(2, folders.Count);
        CollectionAssert.AreEquivalent(
            new[] { "Part A", "Part B" },
            folders.Select(folder => folder.Name).ToArray());
        Assert.AreEqual(2, folders.Select(folder => folder.Id).Distinct().Count());
        Assert.IsTrue(folders.All(folder => folder.ParentId == seriesId));
    }

    [TestMethod]
    public async Task SavesConfigurationToIndexedPhysicalFolder()
    {
        var library = new MockedLibraryManager();
        var series = FakePath.CreateSeries(library, "media-library/indexed-folder");
        series.Name = "Test Series";
        var episodePath = FakePath.CreateFile("media-library/indexed-folder/Part A/01.mkv");
        library.CreateItem(new JellyfinEpisode { Path = episodePath }, series);
        var seriesItems = new List<MediaLibraryItem>
        {
            new()
            {
                Id = series.Id,
                Name = series.Name,
                SeriesName = series.Name,
                Type = "Series",
                Path = series.Path,
            },
        };
        var folder = MediaLibraryController.BuildPhysicalFolderItems(
            seriesItems,
            new[] { new JellyfinEpisode { Path = episodePath } }).Single();
        var controller = new MediaLibraryController(library);

        var result = await controller.SaveConfiguration(folder.Id, new UpdateMediaLibraryConfiguration
        {
            Id = 54321,
        });

        Assert.IsInstanceOfType<OkObjectResult>(result.Result);
        var configurationPath = Path.Join(Path.GetDirectoryName(episodePath), "bangumi.ini");
        Assert.IsTrue(File.Exists(configurationPath));
        StringAssert.Contains(await File.ReadAllTextAsync(configurationPath), "ID=54321");
    }
    [TestMethod]
    public async Task RejectsInvalidDirectoryType()
    {
        var library = new MockedLibraryManager();
        var series = FakePath.CreateSeries(library, "media-library/invalid-type");
        var controller = new MediaLibraryController(library);
        var result = await controller.SaveConfiguration(series.Id, new UpdateMediaLibraryConfiguration
        {
            Type = (Model.DirectoryType)999,
        });
        Assert.IsInstanceOfType<BadRequestObjectResult>(result.Result);
        Assert.IsFalse(File.Exists(Path.Join(series.Path, "bangumi.ini")));
    }

    [TestMethod]
    public void DirectoryTypeJsonUsesNames()
    {
        var request = JsonSerializer.Deserialize<UpdateMediaLibraryConfiguration>("{\"Type\":\"Normal\"}")!;
        Assert.AreEqual(Model.DirectoryType.Normal, request.Type);
        var json = JsonSerializer.Serialize(new MediaLibraryConfiguration { Type = Model.DirectoryType.Special });
        StringAssert.Contains(json, "\"Type\":\"Special\"");
    }

}
