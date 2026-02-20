using System;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.Bangumi.Archive.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class ArchiveData
{
    [TestMethod]
    [Timeout(5000)]
    public void ParseArchiveSubject()
    {
        var jsonLine = """{"id":128885,"type":2,"name":"新妹魔王の契約者 BURST","name_cn":"新妹魔王的契约者 BURST","infobox":"{{Infobox animanga/TVAnime\r\n|中文名= 新妹魔王的契约者 BURST\r\n|别名={\r\n}\r\n|话数= 10\r\n|放送开始= 2015年10月9日\r\n|放送星期= 星期五\r\n|官方网站= https://anime-shinmaimaou.com/\r\n|在线播放平台= \r\n|播放电视台= TOKYO MX\r\n|其他电视台= BS11 / チバテレビ / tvk / テレ玉 / 三重テレビ / ぎふチャン / サンテレビ / 九州放送\r\n|播放结束= 2015年12月11日\r\n|导演= 斎藤久\r\n|音乐= 高梨康治（Team-MAX）\r\n|链接={\r\n}\r\n|其他= \r\n|Copyright= ©2015 上栖綴人・Nitroplus/KADOKAWA/「新妹魔王の契約者 BURST」製作委員会\r\n|原作= 上栖綴人（株式会社KADOKAWA 角川スニーカー文庫刊）\r\n|脚本监修= 吉岡たかを\r\n|人物设定= わたなべよしひろ（渡邊義弘）\r\n|人物原案= 大熊猫介（ニトロプラス）\r\n|总作画监督= わたなべよしひろ（渡邊義弘）・沈宏（FAI）・今井雅美・森前和也・油井徹太郎\r\n|美术= 美峰\r\n|美术监督= 木下了香（美峰）\r\n|美术设计= 金城沙綾（美峰）\r\n|色彩设计= 金久保高央（Triple A）\r\n|2D 设计= 村上朋輝\r\n|摄影监督= 船倉一晃\r\n|剪辑= 木村祥明（IMAGICA）\r\n|3DCG= 渡辺哲也\r\n|主动画师= 高橋健、津熊健徳、竹上貴雄、萩尾圭太\r\n|设定= 沖田宮奈、石本剛啓\r\n|音效= 今野康之（スワラ・プロ）、上野励（スワラ・プロ）\r\n|音响监督= 高橋剛\r\n|录音= 黒崎裕樹\r\n|音响= 伊藤映里\r\n|录音助理= 東田直子\r\n|录音工作室= グロービジョン\r\n|音乐制作= 日本コロムビア\r\n|音乐制作人= 植村俊一\r\n|主题歌演出= Metamorphose、Dual Flare\r\n|企画= 菊池剛、工藤大丈\r\n|制片人= 倉兼千晶、元長聡\r\n|动画制片人= 制作制片人：淡野直人\r\n|製作= 「新妹魔王の契約者」製作委員会（ KADOKAWA、三共プランニング、クロックワークス、日本コロムビア、グロービジョン、角川メディアハウス、ソニーPCL、AT-X、プロダクションアイムズ、コミックとらのあな、サンテレビジョン）；堀内大示、毒島剛介、武智恒雄、北條真、岡田信之、篠崎文彦、武田邦裕、土橋哲也、松嵜義之、鮎澤慎二郎、曽原敏朗\r\n|OP・ED 分镜= 斉藤久 / 木戸通泰（LSI inc.）\r\n|设定制作= 松本健吾\r\n|制作协力= ウィルペレット\r\n|协力= 岸田悠佑、中澤章一、萩原淳、岩上貴則、金子尚友、鈴木さよ、藤原利紀、山口貴之、佐藤大悟、二木大介、鈴木友理絵、中嶋嘉美、石川功\r\n|特别鸣谢= 原慶祐、矢島竜、城代佑樹（Triple A）","platform":1,"summary":"澪の持つ先代魔王の力を狙ってきたゾルギアを倒し、刃更たちはひと時の平穏を取り戻したかに見えた。澪や柚希たちと体育祭の準備で、慌ただしく日常生活を謳歌する刃更たちだったがある日、突如魔法で操られた人間たちに襲われて…。家族を守るため、強くなるため、ますます淫らに激しく主従契約を強める刃更たち。彼らの前に万理亜の姉、ルキアが現れたことから魔界を揺るがす戦いに巻き込まれることに……大切な家族を、守りきりたい。バトルとエロスも臨界突破！天地決壊のエクスタシーバトルアクション！魔界への扉が、ここに開くッ！！\r\n\r\n","nsfw":false,"tags":[{"name":"后宫","count":577},{"name":"肉番","count":515},{"name":"2015年10月","count":391},{"name":"轻小说改","count":374},{"name":"TV","count":285},{"name":"新妹魔王的契约者","count":228},{"name":"卖肉","count":206},{"name":"战斗","count":193},{"name":"2015","count":170},{"name":"Production.IMS","count":137},{"name":"兄妹","count":118}],"meta_tags":["后宫","TV","日本","战斗","小说改"],"score":5.9,"score_details":{"1":6,"2":12,"3":66,"4":137,"5":547,"6":950,"7":399,"8":119,"9":23,"10":35},"rank":7966,"date":"2015-10-09","favorite":{"wish":649,"done":4531,"doing":230,"on_hold":208,"dropped":156},"series":false}""";

        var archiveSubject = JsonSerializer.Deserialize<Subject>(jsonLine, Constants.JsonSerializerOptions);
        Assert.IsNotNull(archiveSubject, "archive subject should be parsed");

        var modelSubject = archiveSubject.ToSubject();
        Assert.IsNotNull(modelSubject, "model subject should not be null");
        Assert.AreEqual(128885, modelSubject.Id, "should parse id");
        Assert.IsNotNull(modelSubject.Rating, "rating should be parsed");
        Assert.IsTrue(Math.Abs(modelSubject.Rating!.Score - 5.9f) < 0.001f, "score should match");
        Assert.AreEqual(archiveSubject.ScoreDetails.Sum(item => item.Value), modelSubject.Rating!.Total, "rating total should match");
        Assert.AreEqual("https://anime-shinmaimaou.com/", modelSubject.InfoBox?.Get("官方网站"), "infobox should contain official website");
    }
}
