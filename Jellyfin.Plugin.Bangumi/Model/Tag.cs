using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class Tag
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    // 获取公共标签
    // https://bgm.tv/group/topic/406595
    public static string[] GetCommonTagList(SubjectType type)
    {
        return type switch
        {
            SubjectType.Anime =>
            [
                "短片", "剧场版", "TV", "OVA", "MV", "CM", "WEB", "PV", "动态漫画",
                "原创", "漫画改", "游戏改", "小说改",
                "科幻", "喜剧", "百合", "校园", "惊悚", "后宫", "机战", "悬疑", "恋爱", "奇幻", "推理", "运动", "耽美", "音乐", "战斗", "冒险", "萌系", "穿越", "玄幻", "乙女", "恐怖", "历史", "日常", "剧情", "武侠", "美食", "职场",
                "欧美", "日本", "美国", "中国", "法国", "韩国", "俄罗斯", "英国", "苏联", "香港", "捷克", "台湾", "马来西亚",
                "R18",
                "BL", "GL", "子供向", "女性向", "少女向", "少年向", "青年向"
            ],
            SubjectType.Book =>
            [
                "漫画", "小说", "单行本", "画集", "短篇", "系列", "短篇集", "写真集", "公式书", "绘本", "TRPG", "杂志",
                "游戏改", "原创", "小说改", "动画改", "漫画改",
                "少年", "少女", "青年", "BL", "一般向", "GL", "名著", "儿童", "女性", "TL",
                "日本", "中国", "韩国", "台湾", "美国", "法国", "泰国", "香港", "马来西亚",
                "R18",
                "已完结", "连载中",
                "推理", "后宫", "科幻", "百合", "恐怖", "恋爱", "音乐", "校园", "穿越", "战斗", "运动", "武侠", "奇幻", "惊悚", "搞笑", "日常", "悬疑", "冒险", "历史", "乙女", "美食", "职场", "玄幻", "机战",
                "电子书", "纸质书"
            ],
            SubjectType.Game =>
            [
                "游戏", "FD", "资料片", "桌游", "DLC", "扩展包", "软件",
                "PC", "Web", "Windows", "Mac", "Linux", "PS5", "XSX", "NS", "iOS", "Android", "VR", "PSVR2", "街机", "XboxOne", "Xbox", "Xbox360", "GBA", "Wii", "NDS", "FC", "3DS", "GBC", "GB", "N64", "NGC", "SFC", "WiiU", "PS4", "PSVR", "PSV", "PS3", "PSP", "PS2", "PS", "DC", "SS", "MD", "AppleII", "Amiga", "DOS", "Symbian", "PC98", "PCE", "PC88", "X68000",
                "AAVG", "ACT", "ADV", "ARPG", "AVG", "CRPG", "DBG", "DRPG", "EDU", "FPS", "FTG", "Fly", "Horror", "JRPG", "MMORPG", "MOBA", "MUG", "PUZ", "Platform", "RAC", "RPG", "RTS", "RTT", "Roguelike", "SIM", "SLG", "SPG", "SRPG", "STG", "Sandbox", "Survival", "TAB", "TPS", "VN", "休闲", "卡牌对战",
                "Galgame", "BL", "乙女",
                "全年龄", "R18"
            ],
            SubjectType.Music =>
            [
                "专辑", "ASMR",
                "OST", "同人音乐", "ED", "OP", "角色歌", "Vocaloid", "Drama", "Remix", "IN", "印象曲", "VOCAL", "Radio", "单曲", "TM", "朗读剧", "艺人专辑",
                "动画", "游戏", "原创", "电影", "小说", "电视剧", "漫画", "有声作品",
                "BL", "乙女",
                "日本", "韩国", "中国",
                "JPOP", "古典", "摇滚", "电子",
                "R18", "全0",
                "CD", "8cm", "DVD", "Digital", "磁带", "黑胶"
            ],
            SubjectType.Reality =>
            [
                "电视剧", "电影", "综艺", "电台", "广播剧", "演出", "有声剧", "有声书",
                "小说改", "游戏改", "漫画改", "原创",
                "犯罪", "悬疑", "推理", "喜剧", "爱情", "特摄", "科幻", "音乐", "校园", "美食", "奇幻", "动作", "家庭", "战争", "玄幻", "西部", "歌舞", "历史", "传记", "剧情", "纪录片", "恐怖", "惊悚", "职场", "武侠", "古装", "布袋戏", "灾难", "冒险", "少儿", "运动", "同性",
                "日本", "欧美", "美国", "中国", "华语", "英国", "韩国", "香港", "台湾", "加拿大", "法国", "俄罗斯", "泰国", "意大利", "新西兰"
            ],
            _ => []
        };
    }
}
