using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Bangumi.Model;

namespace Jellyfin.Plugin.Bangumi.Archive.Data;

public class Person
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public PersonType Type { get; set; }

    [JsonPropertyName("career")]
    public List<PersonCareer> Career { get; set; } = [];

    [JsonPropertyName("infobox")]
    public string RawInfoBox { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    public PersonDetail ToPersonDetail()
    {
        var detail = new PersonDetail
        {
            Id = Id,
            Name = Name,
            Type = Type,
            Career = Career,
            InfoBox = InfoBox.ParseString(RawInfoBox),
            SummaryRaw = Summary
        };

        var birthDayText = detail.InfoBox?.Get("生日");
        if (birthDayText != null &&
            DateTime.TryParseExact(birthDayText, "yyyy年M月d日", CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.None, out var birthDate))
        {
            detail.BirthYear = birthDate.Year;
            detail.BirthMonth = birthDate.Month;
            detail.Birthday = birthDate.Day;
        }

        detail.BloodType = detail.InfoBox?.Get("血型") switch
        {
            "A型" => BloodType.A,
            "B型" => BloodType.B,
            "AB型" => BloodType.AB,
            "O型" => BloodType.O,
            _ => null
        };

        return detail;
    }
}