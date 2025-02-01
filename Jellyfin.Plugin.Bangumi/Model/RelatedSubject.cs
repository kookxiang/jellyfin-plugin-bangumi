using System.Net;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Bangumi.Configuration;

namespace Jellyfin.Plugin.Bangumi.Model;

public class RelatedSubject
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Id { get; set; }

    public SubjectType Type { get; set; }

    [JsonIgnore]
    public string? Name => Configuration.TranslationPreference switch
    {
        TranslationPreferenceType.Chinese => string.IsNullOrEmpty(ChineseName) ? OriginalName : ChineseName,
        TranslationPreferenceType.Original => OriginalName,
        _ => OriginalName
    };

    [JsonIgnore]
    public string OriginalName => WebUtility.HtmlDecode(OriginalNameRaw);

    [JsonPropertyName("name")]
    public string OriginalNameRaw { get; set; } = "";

    [JsonIgnore]
    public string? ChineseName => WebUtility.HtmlDecode(ChineseNameRaw);

    [JsonPropertyName("name_cn")]
    public string? ChineseNameRaw { get; set; }

    public string? Relation { get; set; }
}
