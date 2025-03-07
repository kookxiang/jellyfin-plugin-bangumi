using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Bangumi.Configuration;

namespace Jellyfin.Plugin.Bangumi.Model;

public class PersonDetail : Person
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    [JsonIgnore]
    public string Summary => SummaryRaw.ToMarkdown();

    [JsonPropertyName("summary")]
    public string SummaryRaw { get; set; } = "";

    [JsonPropertyName("blood_type")]
    public BloodType? BloodType { get; set; }

    [JsonPropertyName("birth_year")]
    public int? BirthYear { get; set; }

    [JsonPropertyName("birth_mon")]
    public int? BirthMonth { get; set; }

    [JsonPropertyName("birth_day")]
    public int? Birthday { get; set; }

    [JsonIgnore]
    public DateTime? Birthdate =>
        BirthYear != null && BirthMonth != null && Birthday != null ? new DateTime((int)BirthYear, (int)BirthMonth, (int)Birthday) : null;

    [JsonIgnore]
    public string? BirthPlace => InfoBox?.Get("出生地") ?? InfoBox?.Get("出身地");

    [JsonIgnore]
    public DateTime? DeathDate
    {
        get
        {
            var dateStr = InfoBox?.Get("卒日");
            if (dateStr != null && DateTime.TryParseExact(dateStr, "yyyy年MM月dd日", CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.None, out var date))
                return date;
            return null;
        }
    }

    [JsonPropertyName("infobox")]
    public JsonElement? InfoBoxRaw
    {
        get => null;
        set => InfoBox = InfoBox.ParseJson(value!.Value);
    }

    [JsonIgnore]
    public InfoBox? InfoBox { get; set; }

    [JsonIgnore]
    public string TranslatedName => Configuration.PersonTranslationPreference switch
    {
        TranslationPreferenceType.Original => Name,
        TranslationPreferenceType.Chinese => InfoBox?.Get("简体中文名") ?? Name,
        _ => Name
    };
}
