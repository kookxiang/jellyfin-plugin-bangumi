using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using MediaBrowser.Common.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class ArchiveRelationsTests
{
    private readonly string _root = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class Paths(string path) : MockedApplicationPaths, IApplicationPaths
    {
        public new string DataPath => path;
    }

    [TestMethod]
    public async Task CompleteSubjectRoundTripsThroughArchiveFiles()
    {
        var paths = new Paths(_root);
        var archive = new Bangumi.Archive.ArchiveData(paths);
        var directory = Path.Join(_root, "bangumi", "archive");
        var temp = Path.Join(directory, "temp");
        Directory.CreateDirectory(temp);
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            var assembly = typeof(ArchiveRelationsTests).Assembly;
            const string prefix = "Jellyfin.Plugin.Bangumi.Test.Fixtures.Archive.";
            foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                await using var source = assembly.GetManifestResourceStream(resource)!;
                await using var target = await zip.CreateEntry(resource[prefix.Length..]).OpenAsync();
                await source.CopyToAsync(target);
            }
        }
        memory.Position = 0;
        using var input = new ZipArchive(memory, ZipArchiveMode.Read);
        Assert.AreEqual(8, input.Entries.Count);
        foreach (var store in archive.Stores)
        {
            var staged = store.Fork(temp, Path.GetRandomFileName());
            await using (var source = await input.GetEntry(store.FileName)!.OpenAsync())
            await using (var target = File.Create(staged.FilePath))
                await source.CopyToAsync(target);
            await staged.GenerateIndex(CancellationToken.None);
            await staged.Move(directory, store.FileName);
            Assert.AreEqual(store.FilePath, staged.FilePath);
            Assert.IsTrue(staged.Exists());
        }
        await archive.SubjectEpisodeRelation.GenerateIndex(CancellationToken.None);
        await archive.SubjectPersonRelation.GenerateIndex(input, CancellationToken.None);
        await archive.SubjectRelations.GenerateIndex(input, CancellationToken.None);
        await archive.SubjectCharacterRelation.GenerateIndex(input, CancellationToken.None);

        // A new instance must recover everything from disk rather than in-memory state.
        archive = new Bangumi.Archive.ArchiveData(paths);
        var subject = (await archive.Subject.FindById(10))!.ToSubject();
        Assert.AreEqual("测试动画", subject.ChineseName);
        Assert.AreEqual(Bangumi.Model.SubjectType.Anime, subject.Type);
        Assert.AreEqual("TV", subject.Platform);
        Assert.AreEqual(3, subject.Rating!.Total);
        Assert.AreEqual(8f, subject.Rating.Score);
        Assert.AreEqual("https://example.invalid/anime", subject.OfficialWebSite);
        CollectionAssert.AreEqual(new[] { "TV", "科幻" }, subject.MetaTags.ToArray());
        Assert.AreEqual("测试续集", (await archive.Subject.FindById(20))!.ChineseName);
        Assert.IsNull(await archive.Subject.FindById(11), "A sparse ID must not return the first record.");
        Assert.IsNull(await archive.Subject.FindById(21));
        Assert.AreEqual(2, archive.Subject.Enumerate().Count());

        var episodes = (await archive.SubjectEpisodeRelation.GetEpisodes(10)).Select(e => e.ToEpisode()).ToArray();
        CollectionAssert.AreEqual(new[] { 100, 102 }, episodes.Select(e => e.Id).ToArray());
        Assert.AreEqual(2d, episodes[1].Order);
        Assert.AreEqual("第2话", episodes[1].ChineseName);
        Assert.AreEqual("24:00", episodes[1].Duration);
        Assert.IsNull(await archive.Episode.FindById(101));
        Assert.IsFalse((await archive.SubjectEpisodeRelation.GetEpisodes(999)).Any());

        var staff = (await archive.SubjectPersonRelation.Get(10)).Single();
        Assert.AreEqual("测试导演", staff.Name);
        Assert.AreEqual("导演", staff.Relation);
        Assert.AreEqual("1-2", staff.AppearEps!.Value.GetString());
        var person = (await archive.Person.FindById(1))!.ToPersonDetail();
        Assert.AreEqual(new DateTime(1990, 1, 2), person.Birthdate);
        var sequel = (await archive.SubjectRelations.Get(10)).Single();
        Assert.AreEqual(20, sequel.Id);
        Assert.AreEqual("续集", sequel.Relation);
        Assert.AreEqual("测试续集", sequel.ChineseName);
        var characters = (await archive.SubjectCharacterRelation.Get(10))!.ToArray();
        CollectionAssert.AreEqual(new[] { 1, 3 }, characters.Select(c => c.Id).ToArray());
        Assert.AreEqual("主角", characters[0].Relation);
        Assert.AreEqual("测试声优", characters[0].Actors!.Single().Name);
        var cast = characters[0].ToPersonInfos().Single();
        Assert.AreEqual("主角", cast.Role);
        Assert.AreEqual("3", cast.ProviderIds[Constants.ProviderName]);

        var version = new Bangumi.Archive.ArchiveVersion(12345678901L, new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), 1000);
        Assert.IsFalse(await archive.IsCurrent(version), "Existing archives without a version need an update.");
        var oldPersonIndex = Path.Join(directory, "subject_person.map");
        var oldSubjectIndex = Path.Join(directory, "subject_relation.map");
        var unknownIndex = Path.Join(directory, "custom.map");
        await File.WriteAllTextAsync(oldPersonIndex, "legacy persons");
        await File.WriteAllTextAsync(oldSubjectIndex, "legacy subjects");
        await File.WriteAllTextAsync(unknownIndex, "preserve");
        await archive.CleanupObsoleteIndexes(version);
        Assert.IsTrue(File.Exists(oldPersonIndex), "Do not clean before a successful import.");
        await archive.SaveVersion(version);
        archive = new Bangumi.Archive.ArchiveData(paths);
        Assert.IsTrue(await archive.IsCurrent(version), "The version must survive a restart.");
        Assert.IsFalse(await archive.IsCurrent(version with { Id = version.Id + 1 }));
        Assert.IsFalse(await archive.IsCurrent(version with { UpdateTime = version.UpdateTime.AddHours(1) }));
        Assert.IsFalse(await archive.IsCurrent(version with { Size = version.Size + 1 }));
        Assert.IsFalse(await archive.IsCurrent(version with { IndexVersion = version.IndexVersion + 1 }));

        var indexPath = Path.ChangeExtension(archive.Subject.FilePath, ".idx");
        var backup = Path.Join(temp, "subject-index-backup");
        File.Move(indexPath, backup);
        Assert.IsFalse(await archive.IsCurrent(version), "Missing data/index files must trigger repair.");
        await archive.CleanupObsoleteIndexes(version);
        Assert.IsTrue(File.Exists(oldPersonIndex), "Incomplete archives must retain legacy indexes.");
        Assert.IsTrue(File.Exists(oldSubjectIndex));
        File.Move(backup, indexPath);
        await archive.CleanupObsoleteIndexes(version with { Id = version.Id + 1 });
        Assert.IsTrue(File.Exists(oldPersonIndex), "A mismatched version must not trigger cleanup.");
        await archive.CleanupObsoleteIndexes(version);
        Assert.IsFalse(File.Exists(oldPersonIndex));
        Assert.IsFalse(File.Exists(oldSubjectIndex));
        Assert.AreEqual("preserve", await File.ReadAllTextAsync(unknownIndex));
        await archive.CleanupObsoleteIndexes(version); // Repeated cleanup is harmless.
        Assert.IsTrue(await archive.IsCurrent(version));

        archive.InvalidateVersion();
        Assert.IsFalse(await new Bangumi.Archive.ArchiveData(paths).IsCurrent(version), "An interrupted import must be retried.");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        try
        {
            await archive.SaveVersion(version, cancelled.Token);
            Assert.Fail("Saving a version with a cancelled token must fail.");
        }
        catch (OperationCanceledException)
        {
            // Cancellation must leave the archive unversioned.
        }
        Assert.IsFalse(await archive.IsCurrent(version), "Cancelled writes must not publish a version.");
        await archive.SaveVersion(version);
        Assert.IsTrue(await archive.IsCurrent(version));
        await File.WriteAllTextAsync(Path.Join(directory, "version.json"), "{invalid");
        Assert.IsFalse(await archive.IsCurrent(version), "Invalid version metadata must trigger repair.");
    }

    [TestMethod]
    public async Task CharacterIndexPreservesOrderAndSubjectSpecificActors()
    {
        var root = _root;
        var directory = Path.Join(root, "bangumi", "archive");
        Directory.CreateDirectory(Path.Join(directory, "temp"));
        var archive = new Bangumi.Archive.ArchiveData(new Paths(root));
        Assert.IsNull(await archive.SubjectCharacterRelation.Get(10));
        await WriteStore(directory, "character", "{\"id\":1,\"name\":\"First\",\"role\":1}", "{\"id\":2,\"name\":\"Second\",\"role\":1}");
        await WriteStore(directory, "person", "{\"id\":1,\"name\":\"Actor A\"}", "{\"id\":2,\"name\":\"Actor B\"}");
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            WriteEntry(zip, "subject-characters.jsonlines", """
                {"subject_id":10,"character_id":1,"type":2,"order":0}
                {"subject_id":10,"character_id":2,"type":1,"order":100}
                {"subject_id":20,"character_id":1,"type":1,"order":0}
                {"subject_id":30,"character_id":1,"type":1,"order":0}
                """);
            WriteEntry(zip, "person-characters.jsonlines", """
                {"subject_id":10,"character_id":1,"person_id":1}
                {"subject_id":10,"character_id":2,"person_id":2}
                {"subject_id":20,"character_id":1,"person_id":2}
                {"subject_id":30,"character_id":1,"person_id":3}
                """);
            WriteEntry(zip, "subject-relations.jsonlines", """
                {"subject_id":10,"related_subject_id":1,"relation_type":2,"order":50}
                {"subject_id":10,"related_subject_id":2,"relation_type":3,"order":3}
                """);
            WriteEntry(zip, "subject-persons.jsonlines", """
                {"subject_id":10,"person_id":1,"position":2,"appear_eps":"1-3"}
                """);
        }
        memory.Position = 0;
        using var input = new ZipArchive(memory, ZipArchiveMode.Read);
        await archive.SubjectCharacterRelation.GenerateIndex(input, CancellationToken.None);
        var characters = (await archive.SubjectCharacterRelation.Get(10))!.ToArray();
        CollectionAssert.AreEqual(new[] { 2, 1 }, characters.Select(c => c.Id).ToArray());
        Assert.AreEqual("主角", characters[0].Relation);
        Assert.AreEqual("Actor A", characters[1].Actors!.Single().Name);
        Assert.AreEqual(2, (await archive.SubjectCharacterRelation.Get(20))!.Single().Actors!.Single().Id);
        Assert.IsNull(await archive.SubjectCharacterRelation.Get(30));
        await archive.SubjectRelations.GenerateIndex(input, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { 2, 1 }, (await archive.SubjectRelations.Get(10)).Select(s => s.Id).ToArray());
        await archive.SubjectPersonRelation.GenerateIndex(input, CancellationToken.None);
        Assert.AreEqual("1-3", (await archive.SubjectPersonRelation.Get(10)).Single().AppearEps!.Value.GetString());
    }

    [TestMethod]
    [DataRow(300)]
    [DataRow(70000)]
    public async Task IndexWidthTracksByteOffsetsRatherThanIds(int summaryLength)
    {
        Directory.CreateDirectory(_root);
        var first = JsonSerializer.Serialize(new { id = 1, name = "首条", summary = new string('中', summaryLength) });
        var last = JsonSerializer.Serialize(new { id = 3, name = "末条" });
        await WriteStore(_root, "subject", first, last);
        var store = new Bangumi.Archive.ArchiveStore<Bangumi.Archive.Data.Subject>(_root, "subject.jsonlines");
        Assert.AreEqual("首条", (await store.FindById(1))!.OriginalName);
        Assert.AreEqual("末条", (await store.FindById(3))!.OriginalName);
        Assert.IsNull(await store.FindById(2));
        Assert.IsNull(await store.FindById(4));
    }

    [TestMethod]
    public void EpisodeDurationSurvivesConversion()
    {
        var episode = JsonSerializer.Deserialize<Bangumi.Archive.Data.Episode>("""{"id":1,"duration":"24:00"}""")!;
        Assert.AreEqual("24:00", episode.ToEpisode().Duration);
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }

    private static async Task WriteStore(string directory, string name, params string[] rows)
    {
        await File.WriteAllTextAsync(Path.Join(directory, name + ".jsonlines"), string.Join("\n", rows) + "\n", new UTF8Encoding(false));
        var store = new Bangumi.Archive.ArchiveStore<Bangumi.Archive.Data.Subject>(directory, name + ".jsonlines");
        await store.GenerateIndex(CancellationToken.None);
    }

}
