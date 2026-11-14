using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Keen.VRage.UI.Screens;

namespace Farvane.PaintColorPicker;

internal sealed class PaletteScreenModel : ScreenViewModel
{
    internal Plugin Plugin { get; }
    public PaletteScreenModel(Plugin plugin)
    {
        Plugin = plugin;
        KeepsOtherScreensVisible = false;
        AllowsInputBelowUI = false;
        AllowsInputFromLowerScreens = false;
        InitializeInputContext();
    }
}

// Uses the same ScreenView/CreateScreen extension points as the SE2 plugin template.
internal sealed class PaletteScreen : ScreenView
{
    internal static readonly IBrush Panel = new SolidColorBrush(Color.FromRgb(23, 31, 39));
    internal static readonly IBrush Edge = new SolidColorBrush(Color.FromRgb(66, 82, 96));
    internal static readonly IBrush Bad = new SolidColorBrush(Color.FromRgb(232, 112, 96));

    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _feedback = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _slots = new() { Spacing = 6 };
    private readonly StackPanel _presets = new() { Spacing = 6 };
    private readonly TextBox _presetName = new() { Width = 260, Watermark = "Preset name", MaxLength = Settings.MaxPresetNameLength };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<SlotRow> _rows = new();

    public PaletteScreen()
    {
        var applyAll = new Button { Content = "Apply all entered" };
        applyAll.Click += (_, _) => Run(ApplyAll);
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Dispose();
        var actions = Row(applyAll, close);

        var save = new Button { Content = "Save current palette as preset" };
        save.Click += (_, _) => Run(SavePreset);

        var panel = new StackPanel { Margin = new Thickness(30), Spacing = 18 };
        panel.Children.Add(new TextBlock { Text = "FNS Paint Gun Color Picker", FontSize = 25 });
        panel.Children.Add(new TextBlock { Text = "v0.2.1 preview • SE2 " + PaletteController.SupportedBuild, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_status);
        panel.Children.Add(new TextBlock
        {
            Text = "Type #RRGGBB or #RGB next to a slot. \"Stored as\" shows the colour the game will actually keep. " +
                   "The palette belongs to this world; use presets to reuse it elsewhere.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(_slots);
        panel.Children.Add(actions);
        panel.Children.Add(_feedback);
        panel.Children.Add(new TextBlock { Text = "Saved presets", FontSize = 19 });
        panel.Children.Add(Row(_presetName, save));
        panel.Children.Add(_presets);

        var border = new Border
        {
            MinWidth = 760, MaxWidth = 1400, MaxHeight = 900,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = Panel, BorderBrush = Edge, BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = panel }
        };
        var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)) };
        overlay.Children.Add(border);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Background = Brushes.Transparent;
        Content = overlay;
        _timer.Tick += (_, _) => Refresh();
    }

    private Plugin? Plugin => (DataContext as PaletteScreenModel)?.Plugin;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
        RebuildPresets();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        if (Plugin is not { } plugin) return;
        _status.Text = "Status: " + plugin.Palette.Status;
        var palette = plugin.Palette.ReadPalette();
        if (palette.Count != _rows.Count) RebuildSlots(palette.Count);
        var current = plugin.Palette.CurrentIndex;
        for (var i = 0; i < palette.Count; i++) _rows[i].Show(palette[i], i == current);
    }

    private void RebuildSlots(int count)
    {
        _rows.Clear();
        _slots.Children.Clear();
        for (var i = 0; i < count; i++)
        {
            var row = new SlotRow(i, (slot, color) => Run(() => SetSlot(slot, color)));
            _rows.Add(row);
            _slots.Children.Add(row.View);
        }
        if (count == 0) _slots.Children.Add(new TextBlock { Text = "No palette yet — equip the paint gun in a world, then reopen or wait a moment." });
    }

    private void RebuildPresets()
    {
        _presets.Children.Clear();
        if (Plugin is not { } plugin) return;
        if (plugin.SettingsError is { } error)
        {
            _presets.Children.Add(new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap });
            return;
        }
        if (plugin.Presets.Count == 0) _presets.Children.Add(new TextBlock { Text = "None saved yet." });
        foreach (var (name, colors) in plugin.Presets)
        {
            var load = new Button { Content = "Load into slots" };
            var delete = new Button { Content = "Delete" };
            load.Click += (_, _) => Run(() => LoadPreset(colors));
            delete.Click += (_, _) => Run(() => { plugin.DeletePreset(name); RebuildPresets(); return "Deleted " + name + "."; });
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            foreach (var hex in colors)
                if (HexColor.TryParse(hex, out var c, out _)) swatches.Children.Add(Swatch(c, 16));
            _presets.Children.Add(Row(new TextBlock { Text = name, Width = 240, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, swatches, load, delete));
        }
    }

    private string SetSlot(int slot, HexColor color)
    {
        if (!Plugin!.Palette.SetSlot(slot, color)) return "Slot " + (slot + 1) + " already holds " + color + ".";
        return "Sent slot " + (slot + 1) + " = " + color + ". The swatch updates when the game confirms.";
    }

    private string ApplyAll()
    {
        var sent = 0;
        var unchanged = 0;
        foreach (var row in _rows)
        {
            if (!row.TryGetEntered(out var color, out var error))
            {
                if (error.Length > 0) return "Slot " + (row.Index + 1) + ": " + error + ". Nothing sent.";
                continue;
            }
            if (Plugin!.Palette.SetSlot(row.Index, color)) sent++;
            else unchanged++;
        }
        if (sent == 0) return unchanged > 0 ? "Entered colours already match the palette." : "Enter at least one hex value.";
        return "Sent " + sent + " slot(s)." + (unchanged > 0 ? " " + unchanged + " already matched." : string.Empty);
    }

    private string LoadPreset(IReadOnlyList<string> colors)
    {
        if (_rows.Count == 0) throw new InvalidOperationException("Equip the paint gun first");
        for (var i = 0; i < _rows.Count; i++) _rows[i].Enter(i < colors.Count ? colors[i] : string.Empty);
        var note = colors.Count > _rows.Count ? " Extra preset colours were ignored." : string.Empty;
        return "Preset entered into the slots. Review, then click Apply all entered." + note;
    }

    private string SavePreset()
    {
        var palette = Plugin!.Palette.ReadPalette();
        if (palette.Count == 0) throw new InvalidOperationException("Equip the paint gun first");
        var name = Settings.NormalizeName(_presetName.Text);
        Plugin.SavePreset(name, palette);
        RebuildPresets();
        return "Saved preset " + name + ".";
    }

    private void Run(Func<string> action)
    {
        try
        {
            _feedback.Text = action();
        }
        catch (Exception error)
        {
            _feedback.Text = error is InvalidOperationException or ArgumentException
                ? error.Message
                : "Failed (" + error.GetType().Name + "). Check the SE2 log.";
        }
        Refresh();
    }

    internal static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    internal static Border Swatch(HexColor color, double size) => new()
    {
        Width = size, Height = size, BorderBrush = Edge, BorderThickness = new Thickness(1),
        Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B))
    };

    private sealed class SlotRow
    {
        private readonly Border _current = Swatch(default, 28);
        private readonly TextBlock _label = new() { Width = 240, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _input = new() { Width = 130, Watermark = "#RRGGBB", MaxLength = 7 };
        private readonly Border _preview = Swatch(default, 28);
        private readonly TextBlock _hint = new() { Width = 200, VerticalAlignment = VerticalAlignment.Center };

        public int Index { get; }
        public Control View { get; }

        public SlotRow(int index, Action<int, HexColor> set)
        {
            Index = index;
            var button = new Button { Content = "Set" };
            button.Click += (_, _) =>
            {
                if (TryGetEntered(out var color, out var error)) set(index, color);
                else _hint.Text = error.Length > 0 ? error : "Enter a hex value";
            };
            _input.TextChanged += (_, _) => UpdatePreview();
            _preview.IsVisible = false;
            View = Row(_current, _label, _input, _preview, button, _hint);
        }

        public void Show(HexColor color, bool selected)
        {
            _current.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            _current.BorderThickness = new Thickness(selected ? 3 : 1);
            _label.Text = (Index + 1) + ". " + color + (selected ? "  (selected)" : string.Empty);
        }

        public void Enter(string hex)
        {
            _input.Text = hex;
            UpdatePreview();
        }

        public bool TryGetEntered(out HexColor color, out string error)
        {
            color = default;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(_input.Text)) return false;
            if (!HexColor.TryParse(_input.Text, out var parsed, out error)) return false;
            color = parsed;
            return true;
        }

        private void UpdatePreview()
        {
            if (string.IsNullOrWhiteSpace(_input.Text))
            {
                _preview.IsVisible = false;
                _hint.Text = string.Empty;
                _hint.Foreground = null;
            }
            else if (HexColor.TryParse(_input.Text, out var color, out var error))
            {
                var stored = PaletteController.Stored(color);
                _preview.Background = new SolidColorBrush(Color.FromRgb(stored.R, stored.G, stored.B));
                _preview.IsVisible = true;
                _hint.Foreground = null;
                _hint.Text = stored == color ? "Exact" : "Stored as " + stored;
            }
            else
            {
                _preview.IsVisible = false;
                _hint.Foreground = Bad;
                _hint.Text = error;
            }
        }
    }
}
