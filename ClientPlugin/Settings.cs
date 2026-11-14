using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Farvane.PaintColorPicker;

/// <summary>Named hex palettes kept by the plugin. World palettes stay in each world save.</summary>
public sealed class Settings
{
    public const int MaxPresetNameLength = 40;

    public SortedDictionary<string, List<string>> Presets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static Settings Load(string file)
    {
        if (!File.Exists(file)) return new Settings();
        var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file))
            ?? throw new JsonException("Expected a settings object");
        var settings = new Settings();
        foreach (var (name, colors) in loaded.Presets ?? new SortedDictionary<string, List<string>>())
        {
            var key = NormalizeName(name);
            if (key.Length == 0 || colors is null) continue;
            var valid = new List<string>();
            foreach (var color in colors)
            {
                if (!HexColor.TryParse(color, out var parsed, out _))
                    throw new JsonException("Preset '" + key + "' contains an invalid colour");
                valid.Add(parsed.ToString());
            }
            settings.Presets[key] = valid;
        }
        return settings;
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, file, overwrite: true);
    }

    public Settings With(string name, IEnumerable<HexColor> colors)
    {
        var key = NormalizeName(name);
        if (key.Length == 0) throw new ArgumentException("Enter a preset name");
        var copy = Clone();
        copy.Presets[key] = colors.Select(c => c.ToString()).ToList();
        return copy;
    }

    public Settings Without(string name)
    {
        var copy = Clone();
        copy.Presets.Remove(NormalizeName(name));
        return copy;
    }

    public static string NormalizeName(string? name)
    {
        var trimmed = string.Join(" ", (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return trimmed.Length > MaxPresetNameLength ? trimmed.Substring(0, MaxPresetNameLength) : trimmed;
    }

    private Settings Clone()
    {
        var copy = new Settings();
        foreach (var (name, colors) in Presets) copy.Presets[name] = new List<string>(colors);
        return copy;
    }
}
