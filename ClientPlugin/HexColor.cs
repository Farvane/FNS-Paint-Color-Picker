using System;
using System.Globalization;

namespace Farvane.PaintColorPicker;

/// <summary>sRGB colour parsed from <c>#RGB</c> or <c>#RRGGBB</c> text. Has no game dependency.</summary>
public readonly record struct HexColor(byte R, byte G, byte B)
{
    public static bool TryParse(string? text, out HexColor color, out string error)
    {
        color = default;
        var value = (text ?? string.Empty).Trim();
        if (value.StartsWith('#')) value = value.Substring(1);
        if (value.Length == 3)
            value = new string(new[] { value[0], value[0], value[1], value[1], value[2], value[2] });
        if (value.Length != 6)
        {
            error = "Use #RRGGBB or #RGB";
            return false;
        }
        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                error = "Only 0-9 and A-F are allowed";
                return false;
            }
        }
        var rgb = int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        color = new HexColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        error = string.Empty;
        return true;
    }

    public override string ToString() => "#" + R.ToString("X2", CultureInfo.InvariantCulture) +
        G.ToString("X2", CultureInfo.InvariantCulture) + B.ToString("X2", CultureInfo.InvariantCulture);
}
