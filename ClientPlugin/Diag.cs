using System;
using Keen.VRage.Library.Diagnostics;

namespace Farvane.PaintColorPicker;

/// <summary>
/// Plugin logging. Problems are always written; step-by-step detail only when the
/// user turns on debug logging in the plugin settings, so normal play stays quiet.
/// </summary>
internal static class Diag
{
    private const string Label = "[FNS Paint Color Picker] ";

    internal static bool DebugEnabled { get; set; }

    /// <summary>Always logged. Use for startup, failures and setting changes.</summary>
    internal static void Info(string message) => Log.Default.WriteLine(Label + message);

    /// <summary>Logged only with debug logging on.</summary>
    internal static void Debug(string message)
    {
        if (DebugEnabled) Log.Default.WriteLine(Label + "[debug] " + message);
    }

    /// <summary>
    /// A user action failed. Expected refusals ("equip the paint gun") are shown on screen
    /// and logged only in debug mode; unexpected exceptions are always logged.
    /// </summary>
    internal static void Failed(string action, Exception error)
    {
        if (error is InvalidOperationException or ArgumentException)
            Debug(action + " refused: " + error.Message);
        else
            Info(action + " failed: " + error.GetType().Name + ": " + error.Message +
                 (DebugEnabled ? Environment.NewLine + error : string.Empty));
    }
}
