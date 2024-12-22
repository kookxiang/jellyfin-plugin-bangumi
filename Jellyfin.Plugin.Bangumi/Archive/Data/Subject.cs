using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Bangumi.Model;

namespace Jellyfin.Plugin.Bangumi.Archive.Data;

public class Subject
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public SubjectType Type { get; set; }

    [JsonPropertyName("name")]
    public string OriginalName { get; set; } = "";

    [JsonPropertyName("name_cn")]
    public string ChineseName { get; set; } = "";

    [JsonPropertyName("infobox")]
    public string RawInfoBox { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("nsfw")]
    public bool IsNSFW { get; set; }

    [JsonPropertyName("platform")]
    public int Platform { get; set; }

    [JsonPropertyName("tags")]
    public List<Tag> Tags { get; set; } = [];

    [JsonPropertyName("score")]
    public float Score { get; set; }

    [JsonPropertyName("score_details")]
    public Dictionary<string, int> ScoreDetails { get; set; } = [];

    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    public Model.Subject ToSubject()
    {
        return new Model.Subject
        {
            Id = Id,
            OriginalNameRaw = OriginalName,
            ChineseNameRaw = ChineseName,
            SummaryRaw = Summary,
            Date = Date,
            Date2 = null,
            Images = null,
            EpisodeCount = null,
            Rating = new Rating
            {
                Rank = Rank,
                Count = ScoreDetails,
                Score = Score,
                Total = ScoreDetails.Sum(item => item.Value)
            },
            AllTags = Tags,
            IsNSFW = IsNSFW,
            Platform = Platform switch
            {
                1 => SubjectPlatform.Tv,
                2 => SubjectPlatform.OVA,
                3 => SubjectPlatform.Movie,
                5 => SubjectPlatform.Web,
                1001 => SubjectPlatform.Comic,
                1002 => SubjectPlatform.Novel,
                1003 => SubjectPlatform.Illustration,
                4001 => SubjectPlatform.Game,
                4002 => SubjectPlatform.Software,
                4003 => SubjectPlatform.ExpansionPack,
                4005 => SubjectPlatform.BoardGame,
                6001 => SubjectPlatform.TvShow,
                6002 => SubjectPlatform.Movie,
                6003 => SubjectPlatform.Performance,
                6004 => SubjectPlatform.Variety,
                _ => null
            },
            InfoBox = InfoBox.ParseString(RawInfoBox)
        };
    }
}