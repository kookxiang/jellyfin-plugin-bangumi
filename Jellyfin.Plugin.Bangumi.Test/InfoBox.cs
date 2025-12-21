using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class InfoBoxTest
{
    [TestMethod]
    public async Task DuplicatedKey()
    {
        var data = JsonSerializer.Deserialize<JsonElement>("[{\"key\":\"中文名\",\"value\":\"间谍过家家\"},{\"key\":\"脚本\",\"value\":\"山崎莉乃、谷村大四郎、河口友美、加藤穂乃伽\"},{\"key\":\"脚本\",\"value\":\"河口友美(1,5,7)、山崎莉乃(2,4,8,11)、谷村大四郎(3,6,10,12)、加藤穂乃伽(9)\"}]", Constants.JsonSerializerOptions);
        var infobox = InfoBox.ParseJson(data);
        Assert.AreEqual(infobox.Get("脚本"), "河口友美(1,5,7)、山崎莉乃(2,4,8,11)、谷村大四郎(3,6,10,12)、加藤穂乃伽(9)");

        data = JsonSerializer.Deserialize<JsonElement>("[{\"key\":\"中文名\",\"value\":\"无意间变成狗，被喜欢的女生捡回家。\"},{\"key\":\"别名\",\"value\":[{\"k\":\"非官方\",\"v\":\"生而为狗，我很幸福\"},{\"k\":\"非官方\",\"v\":\"变成狗后被喜欢的人捡了。\"},{\"v\":\"Inu ni Nattara Suki na Hito ni Hirowareta.\"}]}]", Constants.JsonSerializerOptions);
        infobox = InfoBox.ParseJson(data);
        Assert.AreEqual(infobox.Get("中文名"), "无意间变成狗，被喜欢的女生捡回家。");
    }
}
