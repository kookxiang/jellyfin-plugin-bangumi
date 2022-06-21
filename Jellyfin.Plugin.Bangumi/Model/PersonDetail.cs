using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class PersonDetail : Person
{
    public string Summary { get; set; } = "";

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