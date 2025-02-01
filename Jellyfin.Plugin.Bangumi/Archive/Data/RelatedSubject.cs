using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Archive.Data;

public class RelatedSubject
{
    [JsonPropertyName("related_subject_id")]
    public int RelatedSubjectId { get; set; }

    [JsonPropertyName("relation_type")]
    public short RelationType { get; set; }

    public async Task<Model.RelatedSubject> ToRelatedSubject(ArchiveData archive)
    {
        var subject = await archive.Subject.FindById(RelatedSubjectId);

        return new Model.RelatedSubject
        {
            Id = RelatedSubjectId,
            Type = subject?.Type ?? default,
            OriginalNameRaw = subject?.OriginalName ?? "",
            ChineseNameRaw = subject?.ChineseName,

            // https://github.com/bangumi/common/blob/master/subject_relations.yml
            Relation = RelationType switch
            {
                1 => "改编", // 同系列不同平台作品（如柯南漫画与动画版）
                2 => "前传", // 发生在故事之前
                3 => "续集", // 发生在故事之后
                4 => "总集篇", // 对故事的概括版本
                5 => "全集", // 相对于剧场版/总集篇的完整故事
                6 => "番外篇",
                7 => "角色出演", // 相同角色，没有关联的故事
                8 => "相同世界观", // 发生在同一个世界观/时间线下，不同的出演角色
                9 => "不同世界观", // 相同的主演角色，不同的世界观/时间线设定
                10 => "不同演绎", // 相同设定、角色，不同的演绎方式（如EVA原作与新剧场版)
                11 => "衍生", // 世界观相同，角色主线与有关联或来自主线，但又非主线的主角们
                12 => "主线故事",
                14 => "联动", // 出现了被关联作品中的角色
                99 => "其他",
                1002 => "系列",
                1003 => "单行本",
                1004 => "画集",
                1005 => "前传", // 发生在故事之前
                1006 => "续集", // 发生在故事之后
                1007 => "番外篇",
                1008 => "主线故事",
                1010 => "不同版本",
                1011 => "角色出演", // 相同角色，没有关联的故事
                1012 => "相同世界观", // 发生在同一个世界观/时间线下，不同的出演角色
                1013 => "不同世界观", // 相同的出演角色，不同的世界观/时间线设定
                1014 => "联动", // 出现了被关联作品中的角色
                1099 => "其他",
                3001 => "原声集",
                3002 => "角色歌",
                3003 => "片头曲",
                3004 => "片尾曲",
                3005 => "插入歌",
                3006 => "印象曲",
                3007 => "广播剧",
                3099 => "其他",
                4002 => "前传", // 发生在故事之前/或作品发售之前
                4003 => "续集", // 发生在故事之后/或作品发售之后
                4006 => "外传",
                4007 => "角色出演", // 相同角色，没有关联的故事
                4008 => "相同世界观", // 发生在同一个世界观/时间线下，不同的出演角色
                4009 => "不同世界观", // 相同的出演角色，不同的世界观/时间线设定
                4010 => "不同演绎", // 相同设定、角色，不同的演绎方式
                4011 => "不同版本", // 相同故事、角色，画面、音乐或系统改进
                4012 => "主线故事",
                4013 => "主版本", // 游戏最初发售时的版本
                4014 => "联动", // 出现了被关联作品中的角色
                4015 => "扩展包",
                4016 => "合集", // 收录本作品的合集条目
                4017 => "收录作品", // 合集条目中收录的作品
                4099 => "其他",
                _ => null
            }
        };
    }
}

public class RelatedSubjectRaw : RelatedSubject
{
    [JsonPropertyName("subject_id")]
    public int SubjectId { get; set; }
}
