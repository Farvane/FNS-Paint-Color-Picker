using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using HarmonyLib;
using Keen.Game2.Client.WorldObjects.Tools;
using Keen.Game2.Simulation.GameSystems.Ownership;
using Keen.Game2.Simulation.WorldObjects.Tools;
using Keen.VRage.Core.Game.Components;
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
            Diag.Info(_unsupported);
            _harmony?.UnpatchAll(OwnerId);
        }
    }

    // Vanilla assigns PaintData when the tool's client component joins the scene.
    private static void OnPaintDataAssigned(PaintToolControllableComponent __instance)
    {
        if (_instance is null || __instance.PaintData is null) return;
        _instance._tool = new WeakReference<PaintToolControllableComponent>(__instance);
        if (Diag.DebugEnabled)
            Diag.Debug("Paint tool observed: " + __instance.PaintData.Palette.Count + " slots, selected slot " +
                       (__instance.PaintData.PaletteIndex + 1) + ", local player's: " + IsLocalPlayers(__instance));
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
    public bool SetSlot(int index, HexColor color, string source) =>
        SetSlots(new[] { new KeyValuePair<int, HexColor>(index, color) }, source).Sent.Count == 1;

    /// <summary>
    /// Sends several slots. Slots that already hold the colour are not sent. With debug logging
    /// on, each slot is logged and the palette is re-read shortly after to confirm the game applied it.
    /// </summary>
    public SlotWriteResult SetSlots(IReadOnlyList<KeyValuePair<int, HexColor>> slots, string source) =>
        SetSlots(slots.Select(s => new KeyValuePair<int, ColorHSV>(s.Key, ToHsv(s.Value))).ToList(), source);

    /// <summary>Exact HSV variant, used for the stock palette so values match the game's own.</summary>
    public SlotWriteResult SetSlots(IReadOnlyList<KeyValuePair<int, ColorHSV>> slots, string source)
    {
        var result = new SlotWriteResult();
        Diag.Debug(source + ": sending " + slots.Count + " slot(s)");
        foreach (var (index, color) in slots)
        {
            if (SetSlotCore(index, color, source)) result.Sent.Add(index);
            else result.Unchanged.Add(index);
        }
        Diag.Debug(source + ": sent " + result.Sent.Count + ", already matched " + result.Unchanged.Count);
        if (Diag.DebugEnabled && result.Sent.Count > 0)
            VerifyLater(slots.Where(s => result.Sent.Contains(s.Key)).ToList(), source);
        return result;
    }

    private bool SetSlotCore(int index, ColorHSV hsv, string source)
    {
        if (!TryGetTool(out var tool, out var reason)) throw new InvalidOperationException(reason);
        var palette = tool.PaintData.Palette;
        // The server writes Palette[index] without a range check; never send an invalid slot.
        if (index < 0 || index >= palette.Count)
            throw new InvalidOperationException("Slot " + (index + 1) + " does not exist in this palette");
        var color = ToHex(hsv);
        if (palette[index].Values == hsv.Values)
        {
            Diag.Debug(source + ": slot " + (index + 1) + " already holds " + color + "; not sent");
            return false;
        }
        var session = tool.GetSession()
            ?? throw new InvalidOperationException("The world session is unavailable");
        // Static: the server resolves the sender's identity and edits only that player's palette.
        PaintToolPerPlayerData.SetPaintColor(session, hsv, index).SkipWait();
        Diag.Debug(source + ": slot " + (index + 1) + " " + ToHex(palette[index]) + " -> " + color +
                   " (HSV " + hsv.Values + ") sent");
        return true;
    }

    // Debug only: the game confirms asynchronously, so re-read the palette a moment later.
    private void VerifyLater(IReadOnlyList<KeyValuePair<int, ColorHSV>> sent, string source)
    {
        var expected = sent.Select(s => (Slot: s.Key, Color: ToHex(s.Value))).ToList();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var palette = ReadPalette();
                if (palette.Count == 0)
                {
                    Diag.Debug(source + ": could not confirm; " + Status);
                    return;
                }
                var wrong = expected
                    .Where(e => e.Slot >= palette.Count || !Close(palette[e.Slot], e.Color))
                    .Select(e => "slot " + (e.Slot + 1) + " expected " + e.Color + ", has " +
                                 (e.Slot < palette.Count ? palette[e.Slot].ToString() : "nothing"))
                    .ToList();
                Diag.Debug(wrong.Count == 0
                    ? source + ": game confirmed all " + expected.Count + " slot(s)"
                    : source + ": " + wrong.Count + " of " + expected.Count + " slot(s) not applied after 2 s: " +
                      string.Join("; ", wrong));
            }
            catch (Exception error)
            {
                Diag.Failed(source + " confirmation", error);
            }
        };
        timer.Start();
    }

    // Network round-trips of HSV floats can shift a channel by one.
    private static bool Close(HexColor a, HexColor b) =>
        Math.Abs(a.R - b.R) <= 1 && Math.Abs(a.G - b.G) <= 1 && Math.Abs(a.B - b.B) <= 1;

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

internal sealed class SlotWriteResult
{
    public List<int> Sent { get; } = new();
    public List<int> Unchanged { get; } = new();
}
