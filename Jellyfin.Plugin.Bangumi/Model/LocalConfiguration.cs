using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Model;

public class LocalConfiguration
{
    public int Id { get; set; } = 0;

    public int Offset { get; set; } = 0;

    public bool Report { get; set; } = true;

    public bool Skip { get; set; } = false;

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
        foreach (var line in lines)
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            var property = properties.FirstOrDefault(info => string.Equals(info!.Name, key, StringComparison.CurrentCultureIgnoreCase), null);
            if (property == null) continue;
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
                property.SetValue(this, int.Parse(value));
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
            var value = property.GetValue(this);
            if (value == null) continue;
            if (value.Equals(property.GetValue(defaultConfiguration))) continue;
            if (property.PropertyType == typeof(bool))
                content += $"{property.Name}={((bool)value ? "on" : "off")}" + Environment.NewLine;
            else
                content += $"{property.Name}={value}" + Environment.NewLine;
        }

        await File.WriteAllTextAsync(path, content);
    }
}
