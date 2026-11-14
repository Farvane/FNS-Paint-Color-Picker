using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Core;
using Keen.VRage.Core.Plugins;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;

namespace Farvane.PaintColorPicker;

public sealed class Plugin : IPlugin, IDisposable
{
    public const string Name = "FNSPaintColorPicker";
    // Pulsar injects this delegate before constructing the plugin.
#pragma warning disable CS0649
    private static Func<string, string?, string>? GetConfigPath;
#pragma warning restore CS0649
    private readonly string? _configFile;
    private Settings _settings = new();
    private readonly string? _settingsError;

    internal PaletteController Palette { get; }

    public Plugin()
    {
        Palette = new PaletteController();
        try
        {
            var directory = GetConfigPath?.Invoke(Name, null)
                ?? throw new InvalidOperationException("Pulsar configuration directory is unavailable");
            _configFile = Path.Combine(directory, "settings.json");
            _settings = Settings.Load(_configFile);
        }
        catch (Exception error)
        {
            // Palette editing still works; only saved presets are unavailable.
            _settingsError = "Saved presets unavailable: settings could not be loaded (" + error.GetType().Name + ")";
            Log.Default.WriteLine("[FNS Paint Color Picker] " + _settingsError);
        }
    }

    public void OpenConfigDialog()
    {
        try
        {
            var engine = Singleton<VRageCore>.Instance?.Engine;
            var appType = AccessTools.TypeByName("Keen.Game2.GameAppComponent");
            if (engine is null || appType is null) return;
            var get = AccessTools.FirstMethod(engine.GetType(), method => method.Name == "Get" &&
                method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 &&
                method.GetParameters().Length == 1)
                ?? throw new InvalidOperationException("Engine component lookup is unavailable");
            var app = get.MakeGenericMethod(appType).Invoke(engine, new object[] { default(StringId) });
            var ui = AccessTools.Method(appType, "GetSharedUI")?.Invoke(app, null) as SharedUIComponent;
            if (ui is null) return;
            ui.CreateScreen<PaletteScreen>(new PaletteScreenModel(this), showCursor: true);
        }
        catch (Exception error)
        {
            Log.Default.WriteLine("[FNS Paint Color Picker] Screen unavailable: " + error.GetType().Name);
        }
    }

    internal string? SettingsError => _settingsError;
    internal IReadOnlyDictionary<string, List<string>> Presets => _settings.Presets;

    internal void SavePreset(string name, IEnumerable<HexColor> colors) => Store(_settings.With(name, colors));

    internal void DeletePreset(string name) => Store(_settings.Without(name));

    private void Store(Settings next)
    {
        if (_configFile is null) throw new InvalidOperationException(_settingsError ?? "Settings are unavailable");
        // Save first: a disk failure leaves the in-memory presets unchanged.
        next.Save(_configFile);
        _settings = next;
    }

    public void Dispose() => Palette.Dispose();
}
