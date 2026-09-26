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

    public List<FileOffsetRule> OffsetRules { get; set; } = [];

    public int GetOffset(string? path)
    {
        if (string.IsNullOrEmpty(path)) return Offset;
        var fileName = Path.GetFileName(path);
        return OffsetRules.FirstOrDefault(rule =>
            !string.IsNullOrEmpty(rule.Selector) &&
            FileSystemName.MatchesSimpleExpression(rule.Selector, fileName, true))?.Offset ?? Offset;
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
            await configuration.ReadFrom(Path.Join(Path.GetDirectoryName(path), "bangumi.ini"));
        return configuration;
    }

    public async Task ReadFrom(string path)
    {
        if (!File.Exists(path))
            return;

        var properties = GetType().GetProperties();
        var lines = await File.ReadAllLinesAsync(path);
        OffsetRules.Clear();
        FileOffsetRule? currentRule = null;
        var inFileSection = false;
        foreach (var line in lines)
        {
            var section = line.Trim();
            if (section.StartsWith('[') && section.EndsWith(']'))
            {
                inFileSection = section.StartsWith("[File:", StringComparison.OrdinalIgnoreCase);
                currentRule = null;
                if (inFileSection)
                {
                    var selector = section[6..^1].Trim();
                    if (!string.IsNullOrWhiteSpace(selector))
                        currentRule = new FileOffsetRule { Selector = selector };
                }
                continue;
            }
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            if (inFileSection)
            {
                if (currentRule != null && string.Equals(key, nameof(Offset), StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(value, out var ruleOffset))
                {
                    currentRule.Offset = ruleOffset;
                    if (!OffsetRules.Contains(currentRule)) OffsetRules.Add(currentRule);
                }
                continue;
            }
            var property = properties.FirstOrDefault(info => string.Equals(info!.Name, key, StringComparison.CurrentCultureIgnoreCase), null);
            if (property == null) continue;
            if (property.Name == nameof(OffsetRules)) continue;
            if (property.PropertyType == typeof(bool))
            {
                var trueValue = new[]
                {
                    "on",
                    "yes",
                    "true",
                    "1"
                };
                var falseValue = new[]
                {
                    "off",
                    "no",
                    "false",
                    "0"
                };
                if (trueValue.Contains(value, StringComparer.CurrentCultureIgnoreCase))
                    property.SetValue(this, true);
                else if (falseValue.Contains(value, StringComparer.CurrentCultureIgnoreCase))
                    property.SetValue(this, false);
            }
            else if (property.PropertyType == typeof(int))
            {
                if (int.TryParse(value, out var intValue))
                    property.SetValue(this, intValue);
            }
            else if (property.PropertyType.IsEnum)
            {
                if (Enum.TryParse(property.PropertyType, value, true, out var enumValue)
                    && enumValue != null && Enum.IsDefined(property.PropertyType, enumValue))
                    property.SetValue(this, enumValue);
            }
            else if (property.PropertyType == typeof(string))
            {
                property.SetValue(this, value);
            }
        }
    }

    public async Task SaveTo(string path)
    {
        var content = "[Bangumi]" + Environment.NewLine;
        var defaultConfiguration = new LocalConfiguration();
        var properties = GetType().GetProperties();
        foreach (var property in properties)
        {
            if (property.Name == nameof(OffsetRules)) continue;
            var value = property.GetValue(this);
            if (value == null) continue;
            if (value.Equals(property.GetValue(defaultConfiguration))) continue;
            var key = property.Name == nameof(Id) ? "ID" : property.Name;
            if (property.PropertyType == typeof(bool))
                content += $"{key}={((bool)value ? "on" : "off")}" + Environment.NewLine;
            else
                content += $"{key}={value}" + Environment.NewLine;
        }

        foreach (var rule in OffsetRules)
            content += $"{Environment.NewLine}[File:{rule.Selector}]{Environment.NewLine}Offset={rule.Offset}{Environment.NewLine}";

        await File.WriteAllTextAsync(path, content);
    }
}

public class FileOffsetRule
{
    public string Selector { get; set; } = string.Empty;

    public int Offset { get; set; }
}
