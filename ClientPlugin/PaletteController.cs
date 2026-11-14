using System;
using System.Collections.Generic;
using HarmonyLib;
using Keen.Game2.Client.WorldObjects.Tools;
using Keen.Game2.Simulation.GameSystems.Ownership;
using Keen.Game2.Simulation.WorldObjects.Tools;
using Keen.VRage.Core.Game.Components;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.UI.Shared.Extensions;

namespace Farvane.PaintColorPicker;

/// <summary>
/// Finds the local paint tool and writes palette slots via the game's own
/// networked <see cref="PaintToolPerPlayerData.SetPaintColor"/> call — the same
/// path vanilla uses for its colour-pick action. Nothing is written to disk.
/// </summary>
internal sealed class PaletteController : IDisposable
{
    private const string Label = "[FNS Paint Color Picker] ";
    internal const string OwnerId = "farvane.fns-paint-color-picker";
    internal const string SupportedBuild = "4.0.0.3169";

    private static readonly AccessTools.FieldRef<PaintToolControllableComponent, bool> RemovedFromScene =
        AccessTools.FieldRefAccess<PaintToolControllableComponent, bool>("_removedFromScene");
    private static readonly AccessTools.FieldRef<PaintToolControllableComponent, IOwnerProvider> Owner =
        AccessTools.FieldRefAccess<PaintToolControllableComponent, IOwnerProvider>("_owner");

    private static PaletteController? _instance;
    private readonly Harmony? _harmony;
    private WeakReference<PaintToolControllableComponent>? _tool;
    private readonly string? _unsupported;

    public PaletteController()
    {
        try
        {
            var version = typeof(PaintToolPerPlayerData).Assembly.GetName().Version?.ToString();
            if (version != SupportedBuild)
                throw new InvalidOperationException("Supported SE2 build: " + SupportedBuild + "; found " + version);
            if (_instance is not null)
                throw new InvalidOperationException("Another Paint Color Picker instance is already running");
            var setter = AccessTools.PropertySetter(typeof(PaintToolControllableComponent), "PaintData")
                ?? throw new InvalidOperationException("Paint tool data hook not found");
            _harmony = new Harmony(OwnerId);
            _harmony.Patch(setter, postfix: new HarmonyMethod(typeof(PaletteController), nameof(OnPaintDataAssigned)));
            _instance = this;
        }
        catch (Exception error)
        {
            _unsupported = error is InvalidOperationException ? error.Message : "Unavailable (" + error.GetType().Name + ")";
            Log.Default.WriteLine(Label + _unsupported);
            _harmony?.UnpatchAll(OwnerId);
        }
    }

    // Vanilla assigns PaintData when the tool's client component joins the scene.
    private static void OnPaintDataAssigned(PaintToolControllableComponent __instance)
    {
        if (_instance is not null && __instance.PaintData is not null)
            _instance._tool = new WeakReference<PaintToolControllableComponent>(__instance);
    }

    public bool IsSupported => _unsupported is null;

    public string Status
    {
        get
        {
            if (_unsupported is not null) return _unsupported;
            if (!TryGetTool(out _, out var reason)) return reason;
            return "Ready — editing your paint gun palette in this world";
        }
    }

    public IReadOnlyList<HexColor> ReadPalette()
    {
        if (!TryGetTool(out var tool, out _)) return Array.Empty<HexColor>();
        var result = new List<HexColor>();
        foreach (var color in tool.PaintData.Palette) result.Add(ToHex(color));
        return result;
    }

    public int CurrentIndex => TryGetTool(out var tool, out _) ? tool.PaintData.PaletteIndex : -1;

    /// <summary>The colour the game will store for this hex value, as hex (may differ by rounding).</summary>
    public static HexColor Stored(HexColor color) => ToHex(ToHsv(color));

    /// <summary>Sends the colour; returns false when the slot already holds it.</summary>
    public bool SetSlot(int index, HexColor color)
    {
        if (!TryGetTool(out var tool, out var reason)) throw new InvalidOperationException(reason);
        var palette = tool.PaintData.Palette;
        // The server writes Palette[index] without a range check; never send an invalid slot.
        if (index < 0 || index >= palette.Count)
            throw new InvalidOperationException("Slot " + (index + 1) + " does not exist in this palette");
        var hsv = ToHsv(color);
        if (palette[index].Values == hsv.Values) return false;
        var session = tool.GetSession()
            ?? throw new InvalidOperationException("The world session is unavailable");
        // Static: the server resolves the sender's identity and edits only that player's palette.
        PaintToolPerPlayerData.SetPaintColor(session, hsv, index).SkipWait();
        return true;
    }

    private bool TryGetTool(out PaintToolControllableComponent tool, out string reason)
    {
        tool = null!;
        if (_tool is null || !_tool.TryGetTarget(out var candidate))
        {
            reason = "Waiting — equip the paint gun in a world";
            return false;
        }
        if (RemovedFromScene(candidate) || candidate.PaintData is null)
        {
            reason = "Waiting — equip the paint gun (it was put away or the world unloaded)";
            return false;
        }
        if (!IsLocalPlayers(candidate))
        {
            reason = "Waiting — the observed paint gun belongs to another player";
            return false;
        }
        tool = candidate;
        reason = string.Empty;
        return true;
    }

    private static bool IsLocalPlayers(PaintToolControllableComponent tool)
    {
        try
        {
            var session = tool.GetSession();
            var local = session?.Get<IPlayerIdentityProvider>()?.LocalIdentity;
            var owner = Owner(tool)?.Owner;
            // If either side cannot be resolved, rely on the server: it always edits the sender's palette.
            return local is null || owner is null || owner.Value.Equals(local.Value);
        }
        catch (Exception)
        {
            return true;
        }
    }

    internal static ColorHSV ToHsv(HexColor color) =>
        new Avalonia.Media.Color(255, color.R, color.G, color.B).ToHSV();

    internal static HexColor ToHex(ColorHSV color)
    {
        var c = color.ToAvalonia();
        return new HexColor(c.R, c.G, c.B);
    }

    public void Dispose()
    {
        _harmony?.UnpatchAll(OwnerId);
        if (ReferenceEquals(_instance, this)) _instance = null;
        _tool = null;
    }
}
