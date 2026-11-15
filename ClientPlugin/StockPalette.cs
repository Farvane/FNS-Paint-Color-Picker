using System.Collections.Generic;
using System.Linq;
using Keen.VRage.Library.Mathematics;

namespace Farvane.PaintColorPicker;

/// <summary>
/// The paint gun's out-of-the-box palette for SE2 4.0.0.3169 (PaintToolConfiguration.DefaultColors).
/// Sent as exact HSV so restored slots match stock-painted blocks, e.g. for the vanilla swap-ship-colour action.
/// </summary>
internal static class StockPalette
{
    internal static string Name => Settings.BuiltInPresetName;

    internal static readonly IReadOnlyList<ColorHSV> Colors = new[]
    {
        Hsv(0f, 0f, 0.45f), Hsv(0f, 0f, 0.65f),
        Hsv(0f, 0.8f, 0.5f), Hsv(0f, 0.95f, 0.7f),
        Hsv(0.33333334f, 0.32f, 0.2f), Hsv(0.33333334f, 0.47f, 0.4f),
        Hsv(0.575f, 0.8f, 0.45f), Hsv(0.575f, 0.95f, 0.65f),
        Hsv(0.12222222f, 0.7f, 0.71f), Hsv(0.12222222f, 0.85f, 0.91f),
        Hsv(0f, 0f, 0.85f), Hsv(0f, 0f, 1f),
        Hsv(0f, 0f, 0f), Hsv(0f, 0f, 0.15f)
    };

    internal static IReadOnlyList<string> Hex => Colors.Select(c => PaletteController.ToHex(c).ToString()).ToList();

    private static ColorHSV Hsv(float h, float s, float v) => new(h, s, v, 1f);
}
