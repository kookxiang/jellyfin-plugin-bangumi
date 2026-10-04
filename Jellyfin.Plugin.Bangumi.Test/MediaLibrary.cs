using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
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
