using System.Text.Json;
using Farvane.PaintColorPicker;
using Xunit;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fns-paint-tests-" + Guid.NewGuid().ToString("N"));
    private string File => Path.Combine(_dir, "settings.json");

    private static readonly HexColor[] Fns =
    {
        new(0xE9, 0x65, 0x12), new(0x35, 0x84, 0xC4), new(0x42, 0xB7, 0x7B)
    };

    [Fact]
    public void Missing_file_starts_empty()
    {
        Assert.Empty(Settings.Load(File).Presets);
    }

    [Fact]
    public void Saves_and_loads_presets_atomically()
    {
        new Settings().With("FNS Standard", Fns).Save(File);
        Assert.False(System.IO.File.Exists(File + ".tmp"));
        var loaded = Settings.Load(File);
        Assert.Equal(new[] { "#E96512", "#3584C4", "#42B77B" }, loaded.Presets["fns standard"]);
    }

    [Fact]
    public void With_and_without_do_not_mutate_the_original()
    {
        var original = new Settings().With("A", Fns);
        var added = original.With("B", Fns);
        var removed = added.Without("a");
        Assert.Single(original.Presets);
        Assert.Equal(2, added.Presets.Count);
        Assert.Equal(new[] { "B" }, removed.Presets.Keys);
    }

    [Fact]
    public void Same_name_ignoring_case_and_spacing_replaces()
    {
        var settings = new Settings().With("FNS  Standard", Fns).With(" fns standard ", Fns.Take(1));
        var (name, colors) = Assert.Single(settings.Presets);
        Assert.Equal("FNS Standard", name);
        Assert.Single(colors);
    }

    [Fact]
    public void Rejects_blank_name_and_truncates_long_name()
    {
        Assert.Throws<ArgumentException>(() => new Settings().With("   ", Fns));
        var longName = new string('x', 100);
        var key = Assert.Single(new Settings().With(longName, Fns).Presets).Key;
        Assert.Equal(Settings.MaxPresetNameLength, key.Length);
    }

    [Fact]
    public void Normalizes_hand_edited_colours_and_rejects_invalid_ones()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File, "{\"Presets\":{\"Mine\":[\"e96512\",\"#fff\"],\"  \":[\"#000\"]}}");
        var loaded = Settings.Load(File);
        Assert.Equal(new[] { "#E96512", "#FFFFFF" }, loaded.Presets["Mine"]);
        Assert.Single(loaded.Presets);

        System.IO.File.WriteAllText(File, "{\"Presets\":{\"Bad\":[\"#nothex\"]}}");
        Assert.Throws<JsonException>(() => Settings.Load(File));
        System.IO.File.WriteAllText(File, "null");
        Assert.Throws<JsonException>(() => Settings.Load(File));
    }

    [Fact]
    public void Debug_logging_defaults_off_and_round_trips()
    {
        Assert.False(Settings.Load(File).DebugLogging);
        new Settings().With("A", Fns).WithDebugLogging(true).Save(File);
        var loaded = Settings.Load(File);
        Assert.True(loaded.DebugLogging);
        Assert.Single(loaded.Presets);
    }

    [Fact]
    public void Preset_edits_keep_debug_logging_and_toggle_does_not_mutate()
    {
        var original = new Settings().WithDebugLogging(true);
        Assert.True(original.With("A", Fns).Without("A").DebugLogging);
        Assert.False(original.WithDebugLogging(false).DebugLogging);
        Assert.True(original.DebugLogging);
    }

    [Fact]
    public void Files_from_v1_0_without_debug_flag_still_load()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File, "{\"Presets\":{\"FNS Color Palette\":[\"#E96512\"]}}");
        var loaded = Settings.Load(File);
        Assert.False(loaded.DebugLogging);
        Assert.Equal(new[] { "#E96512" }, loaded.Presets["FNS Color Palette"]);
    }

    [Fact]
    public void Built_in_name_is_reserved_but_old_saves_are_kept()
    {
        Assert.Throws<ArgumentException>(() => new Settings().With(" se2  default ", Fns));
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File, "{\"Presets\":{\"SE2 Default\":[\"#E96512\"]}}");
        var key = Assert.Single(Settings.Load(File).Presets).Key;
        Assert.Equal("SE2 Default (saved)", key);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
