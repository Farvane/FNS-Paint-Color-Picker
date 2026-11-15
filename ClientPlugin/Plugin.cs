using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Core;
using Keen.VRage.Core.Plugins;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Library.Utils;
using Keen.VRage.UI.Screens;

namespace Farvane.PaintColorPicker;

public sealed class Plugin : IPlugin, IDisposable
{
    public const string Name = "FNSPaintColorPicker";
    public const string Version = "1.1.0";
    // Pulsar injects this delegate before constructing the plugin.
#pragma warning disable CS0649
    private static Func<string, string?, string>? GetConfigPath;
#pragma warning restore CS0649
    private readonly string? _configFile;
    private Settings _settings = new();
    private readonly string? _settingsError;

    internal PaletteController Palette { get; }
    private readonly RadialMenuIntegration? _radial;

    public Plugin()
    {
        Palette = new PaletteController();
        if (Palette.IsSupported) _radial = new RadialMenuIntegration(this);
        try
        {
            var directory = GetConfigPath?.Invoke(Name, null)
                ?? throw new InvalidOperationException("Pulsar configuration directory is unavailable");
            _configFile = Path.Combine(directory, "settings.json");
            _settings = Settings.Load(_configFile);
            Diag.DebugEnabled = _settings.DebugLogging;
        }
        catch (Exception error)
        {
            // Palette editing still works; only saved presets are unavailable.
            _settingsError = "Saved presets unavailable: settings could not be loaded (" + error.GetType().Name + ")";
            Diag.Info(_settingsError + ": " + error.Message);
        }
        Diag.Info("v" + Version + " loaded; debug logging " + (Diag.DebugEnabled ? "on" : "off"));
        Diag.Debug("Settings file: " + (_configFile ?? "unavailable") + "; presets: " +
                   (_settings.Presets.Count == 0 ? "none saved" : string.Join(", ", _settings.Presets.Keys)) +
                   "; palette editing " + (Palette.IsSupported ? "supported" : "unsupported") +
                   "; colour wheel integration " + (_radial is null ? "off" : "on"));
    }

    public void OpenConfigDialog() => ShowScreen<PaletteScreen>(new PaletteScreenModel(this), ScreenLayer.Normal);

    /// <summary>Opens a plugin screen; <see cref="ScreenLayer.Top"/> draws above the radial menu.</summary>
    internal void ShowScreen<TView>(ScreenViewModel model, ScreenLayer layer) where TView : ScreenView, new()
    {
        try
        {
            var engine = Singleton<VRageCore>.Instance?.Engine;
            var appType = AccessTools.TypeByName("Keen.Game2.GameAppComponent");
            if (engine is null || appType is null)
            {
                Diag.Info("Cannot open " + typeof(TView).Name + ": game " + (engine is null ? "engine" : "app component type") + " unavailable");
                return;
            }
            var get = AccessTools.FirstMethod(engine.GetType(), method => method.Name == "Get" &&
                method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 &&
                method.GetParameters().Length == 1)
                ?? throw new InvalidOperationException("Engine component lookup is unavailable");
            var app = get.MakeGenericMethod(appType).Invoke(engine, new object[] { default(StringId) });
            var ui = AccessTools.Method(appType, "GetSharedUI")?.Invoke(app, null) as SharedUIComponent;
            if (ui is null)
            {
                Diag.Info("Cannot open " + typeof(TView).Name + ": shared UI unavailable");
                return;
            }
            ui.CreateScreen<TView>(model, true, layer);
            Diag.Debug("Opened " + typeof(TView).Name + " on layer " + layer);
        }
        catch (Exception error)
        {
            Diag.Failed("Opening " + typeof(TView).Name, error);
        }
    }

    internal string? SettingsError => _settingsError;
    /// <summary>The built-in stock palette first, then saved presets by name.</summary>
    internal IReadOnlyList<PresetView> Presets =>
        new[] { new PresetView(StockPalette.Name, StockPalette.Hex, true) }
            .Concat(_settings.Presets.Select(p => new PresetView(p.Key, p.Value, false)))
            .ToList();

    /// <summary>Sends a preset's colours to the matching palette slots.</summary>
    internal PresetApplied ApplyPreset(string name)
    {
        var builtIn = Settings.IsBuiltIn(name);
        List<string>? colors = null;
        if (!builtIn && !_settings.Presets.TryGetValue(name, out colors))
            throw new InvalidOperationException("Preset " + name + " no longer exists");
        var count = builtIn ? StockPalette.Colors.Count : colors!.Count;
        var palette = Palette.ReadPalette();
        if (palette.Count == 0) throw new InvalidOperationException(Palette.Status);
        Diag.Debug("Applying " + (builtIn ? "built-in" : "saved") + " preset '" + name + "': " + count +
                   " colour(s) to a " + palette.Count + "-slot palette");
        var slots = new List<KeyValuePair<int, ColorHSV>>();
        for (var i = 0; i < Math.Min(count, palette.Count); i++)
        {
            if (builtIn) slots.Add(new(i, StockPalette.Colors[i]));
            else if (HexColor.TryParse(colors![i], out var color, out _)) slots.Add(new(i, PaletteController.ToHsv(color)));
        }
        var result = Palette.SetSlots(slots, "Preset '" + name + "'");
        return new PresetApplied(
            slots.ToDictionary(s => s.Key, s => PaletteController.ToHex(s.Value)),
            result.Sent.Count, result.Unchanged.Count, Math.Max(0, count - palette.Count));
    }

    internal void SavePreset(string name, IEnumerable<HexColor> colors) => Store(_settings.With(name, colors), "Saved preset '" + name + "'");

    internal void DeletePreset(string name) => Store(_settings.Without(name), "Deleted preset '" + name + "'");

    internal bool DebugLogging => Diag.DebugEnabled;

    /// <summary>Turns debug logging on or off. Returns false when it applies to this session only (settings unsaved).</summary>
    internal bool SetDebugLogging(bool enabled)
    {
        // Save first so a disk failure leaves logging as it was.
        var saved = _configFile is not null;
        if (saved) Store(_settings.WithDebugLogging(enabled), "Saved debug logging setting");
        Diag.DebugEnabled = enabled;
        Diag.Info("Debug logging " + (enabled ? "on" : "off") + (saved ? string.Empty : " (this session only)"));
        return saved;
    }

    private void Store(Settings next, string what)
    {
        if (_configFile is null) throw new InvalidOperationException(_settingsError ?? "Settings are unavailable");
        // Save first: a disk failure leaves the in-memory presets unchanged.
        next.Save(_configFile);
        _settings = next;
        Diag.Debug(what + " (" + next.Presets.Count + " preset(s) on disk)");
    }

    public void Dispose()
    {
        _radial?.Dispose();
        Palette.Dispose();
    }
}

internal sealed record PresetApplied(IReadOnlyDictionary<int, HexColor> Shown, int Sent, int Unchanged, int Ignored)
{
    public string Describe(string name)
    {
        var text = Sent == 0
            ? name + " already matches the palette."
            : "Applied " + name + ": sent " + Sent + " slot(s)" + (Unchanged > 0 ? ", " + Unchanged + " already matched" : string.Empty) + ".";
        return Ignored > 0 ? text + " " + Ignored + " extra preset colour(s) ignored." : text;
    }
}

internal sealed record PresetView(string Name, IReadOnlyList<string> Colors, bool IsBuiltIn);
