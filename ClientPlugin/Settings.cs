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

    /// <summary>Name of the plugin's built-in, read-only stock palette. Not stored in the file.</summary>
    public const string BuiltInPresetName = "SE2 Default";

    public SortedDictionary<string, List<string>> Presets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Writes detailed diagnostics to the SE2 log. Off by default.</summary>
    public bool DebugLogging { get; set; }

    public static Settings Load(string file)
    {
        if (!File.Exists(file)) return new Settings();
        var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file))
            ?? throw new JsonException("Expected a settings object");
        var settings = new Settings { DebugLogging = loaded.DebugLogging };
        foreach (var (name, colors) in loaded.Presets ?? new SortedDictionary<string, List<string>>())
        {
            var key = NormalizeName(name);
            if (key.Length == 0 || colors is null) continue;
            // Keep a hand-made preset that predates the built-in name instead of hiding or dropping it.
            if (IsBuiltIn(key)) key = BuiltInPresetName + " (saved)";
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
        if (IsBuiltIn(key)) throw new ArgumentException(BuiltInPresetName + " is built in; choose another name");
        var copy = Clone();
        copy.Presets[key] = colors.Select(c => c.ToString()).ToList();
        return copy;
    }

    public Settings WithDebugLogging(bool enabled)
    {
        var copy = Clone();
        copy.DebugLogging = enabled;
        return copy;
    }

    public Settings Without(string name)
    {
        var copy = Clone();
        copy.Presets.Remove(NormalizeName(name));
        return copy;
    }

    public static bool IsBuiltIn(string? name) =>
        string.Equals(NormalizeName(name), BuiltInPresetName, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeName(string? name)
    {
        var trimmed = string.Join(" ", (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return trimmed.Length > MaxPresetNameLength ? trimmed.Substring(0, MaxPresetNameLength) : trimmed;
    }

    private Settings Clone()
    {
        var copy = new Settings { DebugLogging = DebugLogging };
        foreach (var (name, colors) in Presets) copy.Presets[name] = new List<string>(colors);
        return copy;
    }
}
