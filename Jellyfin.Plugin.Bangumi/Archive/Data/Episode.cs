using System.Text.Json.Serialization;
using Jellyfin.Plugin.Bangumi.Model;

namespace Jellyfin.Plugin.Bangumi.Archive.Data;

public class Episode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("subject_id")]
    public int ParentId { get; set; }

    [JsonPropertyName("type")]
    public EpisodeType Type { get; set; }

    [JsonPropertyName("name")]
    public string OriginalName { get; set; } = "";

    [JsonPropertyName("name_cn")]
    public string ChineseName { get; set; } = "";

    [JsonPropertyName("sort")]
    public double Order { get; set; }

    [JsonPropertyName("disc")]
    public int Disc { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("airdate")]
    public string AirDate { get; set; } = "";

    public Model.Episode ToEpisode()
    {
        return new Model.Episode
        {
            Id = Id,
            ParentId = ParentId,
            Type = Type,
            OriginalNameRaw = OriginalName,
            ChineseNameRaw = ChineseName,
            Order = Order,
            Disc = Disc,
            DescriptionRaw = Description,
            AirDate = AirDate
        };
    }
}
