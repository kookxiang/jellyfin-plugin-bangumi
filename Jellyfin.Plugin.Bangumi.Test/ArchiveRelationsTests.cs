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
    public async Task CharacterIndexPreservesOrderAndSubjectSpecificActors()
    {
        var root = _root;
        var directory = Path.Join(root, "bangumi", "archive");
        Directory.CreateDirectory(Path.Join(directory, "temp"));
        var archive = new Bangumi.Archive.ArchiveData(new Paths(root));
        Assert.IsNull(await archive.SubjectCharacterRelation.Get(10));
        WriteStore(directory, "character", "{\"id\":1,\"name\":\"First\",\"role\":1}", "{\"id\":2,\"name\":\"Second\",\"role\":1}");
        WriteStore(directory, "person", "{\"id\":1,\"name\":\"Actor A\"}", "{\"id\":2,\"name\":\"Actor B\"}");
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

    private static void WriteStore(string directory, string name, params string[] rows)
    {
        File.WriteAllText(Path.Join(directory, name + ".jsonlines"), string.Join("\n", rows) + "\n", new UTF8Encoding(false));
        using var writer = new BinaryWriter(File.Create(Path.Join(directory, name + ".idx")));
        writer.Write((int)sizeof(uint));
        uint offset = 0;
        foreach (var row in rows)
        {
            writer.Write(offset);
            offset += (uint)Encoding.UTF8.GetByteCount(row + "\n");
        }
    }
}
