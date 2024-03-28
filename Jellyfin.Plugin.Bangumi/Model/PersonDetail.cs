using System;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Bangumi.Configuration;

namespace Jellyfin.Plugin.Bangumi.Model;

public class PersonDetail : Person
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    [JsonIgnore]
    public string Summary => Configuration.ConvertLineBreaks ? SummaryRaw.ReplaceLineEndings(Constants.HtmlLineBreak) : SummaryRaw;

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

    public DateTime? Birthdate =>
        BirthYear != null && BirthMonth != null && Birthday != null ? new DateTime((int)BirthYear, (int)BirthMonth, (int)Birthday) : null;
}