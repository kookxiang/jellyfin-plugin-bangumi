﻿using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Archive.Data;

public class RelatedPerson
{
    [JsonPropertyName("person_id")]
    public int PersonId { get; set; }

    [JsonPropertyName("position")]
    public short Position { get; set; }

    public async Task<Model.RelatedPerson> ToRelatedPerson(ArchiveData archive)
    {
        var person = await archive.Person.FindById(PersonId);

        return new Model.RelatedPerson
        {
            Id = PersonId,
            Type = (int)(person?.Type ?? default),
            Name = person?.Name ?? "",
            Career = person?.Career,

            // https://github.com/bangumi/common/blob/master/subject_staffs.yml
            Relation = Position switch
            {
                1 => "原作",
                2 => "导演",
                3 => "脚本",
                4 => "分镜",
                5 => "演出",
                6 => "音乐",
                7 => "人物原案",
                8 => "人物设定",
                9 => "构图",
                10 => "系列构成",
                11 => "美术监督",
                13 => "色彩设计",
                14 => "总作画监督",
                15 => "作画监督",
                16 => "机械设定",
                17 => "摄影监督",
                18 => "监修",
                19 => "道具设计",
                20 => "原画",
                21 => "第二原画",
                22 => "动画检查",
                23 => "助理制片人",
                24 => "制作助理",
                25 => "背景美术",
                26 => "色彩指定",
                27 => "数码绘图",
                28 => "剪辑",
                29 => "原案",
                30 => "主题歌编曲",
                31 => "主题歌作曲",
                32 => "主题歌作词",
                33 => "主题歌演出",
                34 => "插入歌演出",
                35 => "企画",
                36 => "企划制作人",
                37 => "制作管理",
                38 => "宣传",
                39 => "录音",
                40 => "录音助理",
                41 => "系列监督",
                42 => "製作",
                43 => "设定",
                44 => "音响监督",
                45 => "音响",
                46 => "音效",
                47 => "特效",
                48 => "配音监督",
                49 => "联合导演",
                50 => "背景设定",
                51 => "补间动画",
                52 => "执行制片人",
                53 => "助理制片人",
                54 => "制片人",
                55 => "音乐助理",
                56 => "制作进行", // 管理动画的制作时程、协调各部门作业、回收作画原稿等
                57 => "演员监督",
                58 => "总制片人",
                59 => "联合制片人",
                60 => "台词编辑",
                61 => "后期制片协调",
                62 => "制作助理",
                63 => "制作",
                64 => "制作协调",
                65 => "音乐制作",
                66 => "特别鸣谢",
                67 => "动画制作",
                69 => "CG 导演",
                70 => "机械作画监督",
                71 => "美术设计",
                72 => "副导演",
                73 => "OP・ED 分镜",
                74 => "总导演",
                75 => "3DCG",
                76 => "制作协力",
                77 => "动作作画监督",
                80 => "监制",
                81 => "协力",
                82 => "摄影",
                83 => "制作进行协力",
                84 => "设定制作", // 有时需要额外的设计工作，联系负责部门并监督工作确保交付
                85 => "音乐制作人",
                86 => "3DCG 导演",
                87 => "动画制片人",
                88 => "特效作画监督",
                90 => "作画监督助理",
                92 => "主动画师",
                1001 => "开发",
                1002 => "发行",
                1003 => "游戏设计师",
                1004 => "剧本",
                1005 => "美工",
                1006 => "音乐",
                1007 => "关卡设计",
                1008 => "人物设定",
                1009 => "主题歌作曲",
                1010 => "主题歌作词",
                1011 => "主题歌演出",
                1012 => "插入歌演出",
                1013 => "原画",
                1014 => "动画制作",
                1015 => "原作",
                1016 => "导演",
                1017 => "动画监督",
                1018 => "制作总指挥",
                1019 => "QC",
                1020 => "动画剧本",
                1021 => "程序",
                1022 => "协力",
                1023 => "CG 监修",
                1024 => "SD原画",
                1025 => "背景",
                1026 => "监修",
                1027 => "系列构成",
                1028 => "企画",
                1029 => "机械设定",
                1030 => "音响监督",
                1031 => "作画监督",
                1032 => "制作人",
                2001 => "作者",
                2002 => "作画",
                2003 => "插图",
                2004 => "出版社",
                2005 => "连载杂志",
                2006 => "译者",
                2007 => "原作",
                2008 => "客串",
                2009 => "人物原案",
                2010 => "脚本",
                2011 => "书系",
                2012 => "出品方",
                3001 => "艺术家",
                3002 => "制作人",
                3003 => "作曲",
                3004 => "厂牌",
                3005 => "原作",
                3006 => "作词",
                3007 => "录音",
                3008 => "编曲",
                3009 => "插图",
                3010 => "脚本",
                3011 => "出版方",
                3012 => "母带制作",
                3013 => "混音",
                3014 => "乐器",
                3015 => "声乐",
                4001 => "原作",
                4002 => "导演",
                4003 => "编剧",
                4004 => "音乐",
                4005 => "执行制片人",
                4006 => "共同执行制作",
                4007 => "制片人/制作人",
                4008 => "监制",
                4009 => "副制作人/制作顾问",
                4010 => "故事",
                4011 => "编审",
                4012 => "剪辑",
                4013 => "创意总监",
                4014 => "摄影",
                4015 => "主题歌演出",
                4016 => "主演",
                4017 => "配角",
                4018 => "制作",
                4019 => "出品",
                _ => null
            }
        };
    }
}

public class RelatedPersonRaw : RelatedPerson
{
    [JsonPropertyName("subject_id")]
    public int SubjectId { get; set; }
}