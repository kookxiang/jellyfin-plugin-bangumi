using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class RelatedPersonTests
{
    [TestMethod]
    public void OriginalAuthorIsIncludedInMetadataAlongsideScreenwriters()
    {
        // Relevant staff returned by /v0/subjects/531724/persons.
        const string json = """
            [
                {"id":24482,"name":"三木なずな","relation":"原作","type":1},
                {"id":57826,"name":"佐藤寿昭","relation":"脚本","type":1}
            ]
            """;
        var staff = JsonSerializer.Deserialize<RelatedPerson[]>(json, Constants.JsonSerializerOptions)!;
        var metadata = new MetadataResult<Series>();
        foreach (var person in staff.Select(person => person.ToPersonInfo()).OfType<MediaBrowser.Controller.Entities.PersonInfo>())
            metadata.AddPerson(person);

        Assert.AreEqual(2, metadata.People.Count);
        var author = metadata.People.Single(person => person.IsType(PersonKind.Author));
        Assert.AreEqual("三木なずな", author.Name);
        Assert.AreEqual("24482", author.ProviderIds[Constants.ProviderName]);
        Assert.AreEqual("原作", author.Role);
        var writer = metadata.People.Single(person => person.IsType(PersonKind.Writer));
        Assert.AreEqual("佐藤寿昭", writer.Name);
        Assert.AreEqual("脚本", writer.Role);
    }
}
