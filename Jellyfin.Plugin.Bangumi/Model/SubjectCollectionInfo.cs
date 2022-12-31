using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class SubjectCollectionInfo
{
    [JsonPropertyName("subject_id")]
    public int Id { get; set; }

    [JsonPropertyName("subject_type")]
    public SubjectType Type { get; set; }

    [JsonPropertyName("type")]
    public CollectionType Status { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }


    [JsonPropertyName("subject")]
    public Subject? Subject { get; set; }
}