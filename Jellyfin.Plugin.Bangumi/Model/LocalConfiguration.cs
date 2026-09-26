using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Model;

public class LocalConfiguration
{
    public DirectoryType Type { get; set; } = DirectoryType.Auto;

    public EpisodeType? GetForcedEpisodeType() => Type switch
    {
        DirectoryType.Normal => EpisodeType.Normal,
        DirectoryType.Special => EpisodeType.Special,
        _ => null,
    };

    public int Id { get; set; } = 0;

    public int Offset { get; set; } = 0;

    public List<LocalConfigurationSection> Sections { get; set; } = [];

    private LocalConfigurationSection? FindSection(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var fileName = Path.GetFileName(path);
        return Sections.FirstOrDefault(section =>
            !string.IsNullOrEmpty(section.Selector) &&
            FileSystemName.MatchesSimpleExpression(section.Selector, fileName, true));
    }

    public int GetOffset(string? path) => FindSection(path)?.Offset ?? Offset;

    public LocalConfiguration ForFile(string path)
    {
        var section = FindSection(path);
        if (section == null) return this;
        return new LocalConfiguration
        {
            Id = section.Id ?? Id,
            Offset = section.Offset ?? Offset,
            Report = section.Report ?? Report,
            Skip = section.Skip ?? Skip,
            CorrectIndex = section.CorrectIndex ?? CorrectIndex,
            Type = section.Type ?? Type,
        };
    }

    public bool Report { get; set; } = true;

    public bool Skip { get; set; } = false;

    public bool CorrectIndex { get; set; } = false;

    public static async Task<LocalConfiguration> ForPath(string path)
    {
        var configuration = new LocalConfiguration();
        if (Directory.Exists(path))
            await configuration.ReadFrom(Path.Join(path, "bangumi.ini"));
        if (File.Exists(path))
        {
            await configuration.ReadFrom(Path.Join(Path.GetDirectoryName(path), "bangumi.ini"));
            return configuration.ForFile(path);
        }
        return configuration;
    }

    public async Task ReadFrom(string path)
    {
        if (!File.Exists(path))
            return;

        var lines = await File.ReadAllLinesAsync(path);
        Sections.Clear();
        LocalConfigurationSection? currentSection = null;
        var inRuleSection = false;
        var inBangumiSection = true;
        foreach (var line in lines)
        {
            var section = line.Trim();
            if (section.StartsWith('[') && section.EndsWith(']'))
            {
                inBangumiSection = string.Equals(section, "[Bangumi]", StringComparison.OrdinalIgnoreCase);
                inRuleSection = section.StartsWith("[Section.", StringComparison.OrdinalIgnoreCase) && section.Length > 10;
                currentSection = null;
                if (inRuleSection)
                    currentSection = new LocalConfigurationSection();
                continue;
            }
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            if (inRuleSection)
            {
                if (currentSection != null)
                {
                    ReadProperty(currentSection, key, value);
                    if (!string.IsNullOrWhiteSpace(currentSection.Selector) && !Sections.Contains(currentSection))
                        Sections.Add(currentSection);
                }
                continue;
            }
            if (!inBangumiSection) continue;
            ReadProperty(this, key, value);
        }
    }

    private static void ReadProperty(object target, string key, string value)
    {
        var property = target.GetType().GetProperties().FirstOrDefault(info =>
            string.Equals(info.Name, key, StringComparison.OrdinalIgnoreCase));
        if (property == null || property.Name == nameof(Sections)) return;
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(bool))
        {
            if (new[] { "on", "yes", "true", "1" }.Contains(value, StringComparer.OrdinalIgnoreCase))
                property.SetValue(target, true);
            else if (new[] { "off", "no", "false", "0" }.Contains(value, StringComparer.OrdinalIgnoreCase))
                property.SetValue(target, false);
        }
        else if (type == typeof(int) && int.TryParse(value, out var intValue))
            property.SetValue(target, intValue);
        else if (type.IsEnum && Enum.TryParse(type, value, true, out var enumValue)
                 && enumValue != null && Enum.IsDefined(type, enumValue))
            property.SetValue(target, enumValue);
        else if (type == typeof(string))
            property.SetValue(target, value);
    }

    public async Task SaveTo(string path)
    {
        var content = "[Bangumi]" + Environment.NewLine;
        var defaultConfiguration = new LocalConfiguration();
        var properties = GetType().GetProperties();
        foreach (var property in properties)
        {
            if (property.Name == nameof(Sections)) continue;
            var value = property.GetValue(this);
            if (value == null) continue;
            if (value.Equals(property.GetValue(defaultConfiguration))) continue;
            var key = property.Name == nameof(Id) ? "ID" : property.Name;
            if (property.PropertyType == typeof(bool))
                content += $"{key}={((bool)value ? "on" : "off")}" + Environment.NewLine;
            else
                content += $"{key}={value}" + Environment.NewLine;
        }

        for (var index = 0; index < Sections.Count; index++)
        {
            var section = Sections[index];
            content += $"{Environment.NewLine}[Section.{index + 1}]{Environment.NewLine}" +
                       $"Selector={section.Selector}{Environment.NewLine}";
            foreach (var property in typeof(LocalConfigurationSection).GetProperties())
            {
                if (property.Name == nameof(LocalConfigurationSection.Selector)) continue;
                var value = property.GetValue(section);
                if (value == null) continue;
                var key = property.Name == nameof(Id) ? "ID" : property.Name;
                content += $"{key}={(value is bool flag ? flag ? "on" : "off" : value)}{Environment.NewLine}";
            }
        }

        await File.WriteAllTextAsync(path, content);
    }
}

public class LocalConfigurationSection
{
    public string Selector { get; set; } = string.Empty;

    public int? Id { get; set; }

    public int? Offset { get; set; }

    public bool? Report { get; set; }

    public bool? Skip { get; set; }

    public bool? CorrectIndex { get; set; }

    public DirectoryType? Type { get; set; }
}
