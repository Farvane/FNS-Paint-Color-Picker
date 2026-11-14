using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Keen.VRage.UI.Screens;

namespace Farvane.PaintColorPicker;

/// <summary>Small dialogs opened from the paint gun's colour wheel. The wheel stays visible behind them.</summary>
internal abstract class WheelDialogModel : ScreenViewModel
{
    internal Plugin Plugin { get; }
    protected WheelDialogModel(Plugin plugin)
    {
        Plugin = plugin;
        KeepsOtherScreensVisible = true;
        AllowsInputBelowUI = false;
        AllowsInputFromLowerScreens = false;
        InitializeInputContext();
    }
}

internal abstract class WheelDialog : ScreenView
{
    protected readonly TextBlock Feedback = new() { TextWrapping = TextWrapping.Wrap };

    protected void Build(string title, Control body, double width)
    {
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 21 });
        panel.Children.Add(body);
        panel.Children.Add(Feedback);
        var border = new Border
        {
            Width = width, MaxHeight = 640,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = PaletteScreen.Panel, BorderBrush = PaletteScreen.Edge, BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = panel }
        };
        var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)) };
        overlay.Children.Add(border);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Background = Brushes.Transparent;
        Content = overlay;
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Dispose();
        };
    }

    protected void Run(Func<string?> action)
    {
        try
        {
            var message = action();
            if (message is null) Dispose();
            else Feedback.Text = message;
        }
        catch (Exception error)
        {
            Feedback.Text = error is InvalidOperationException or ArgumentException
                ? error.Message
                : "Failed (" + error.GetType().Name + "). Check the SE2 log.";
        }
    }
}

internal sealed class SlotEditorModel : WheelDialogModel
{
    internal int Slot { get; }
    internal Action<HexColor> Saved { get; }
    public SlotEditorModel(Plugin plugin, int slot, Action<HexColor> saved) : base(plugin)
    {
        Slot = slot;
        Saved = saved;
    }
}

internal sealed class SlotEditorScreen : WheelDialog
{
    private readonly TextBox _input = new() { Width = 150, MaxLength = 7, Watermark = "#RRGGBB" };
    private readonly Border _current = PaletteScreen.Swatch(default, 40);
    private readonly Border _preview = PaletteScreen.Swatch(default, 40);
    private readonly TextBlock _currentText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = new() { VerticalAlignment = VerticalAlignment.Center };
    private string _title = "Palette slot";

    public SlotEditorScreen()
    {
        var save = new Button { Content = "Save" };
        save.Click += (_, _) => Run(Save);
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Dispose();
        _input.TextChanged += (_, _) => UpdatePreview();
        _input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Run(Save);
        };
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(PaletteScreen.Row(_current, _currentText));
        body.Children.Add(PaletteScreen.Row(_input, _preview, _hint));
        body.Children.Add(PaletteScreen.Row(save, cancel));
        body.Children.Add(new TextBlock { Text = "Enter saves • Esc cancels", Opacity = 0.7 });
        Build(_title, body, 460);
    }

    private SlotEditorModel? Model => DataContext as SlotEditorModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Model is not { } model) return;
        var palette = model.Plugin.Palette.ReadPalette();
        if (model.Slot < palette.Count)
        {
            var color = palette[model.Slot];
            _current.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            _currentText.Text = "Slot " + (model.Slot + 1) + " is " + color;
            _input.Text = color.ToString();
        }
        else
        {
            Feedback.Text = model.Plugin.Palette.Status;
        }
        UpdatePreview();
        Dispatcher.UIThread.Post(() =>
        {
            _input.Focus();
            _input.SelectAll();
        });
    }

    private string? Save()
    {
        var model = Model ?? throw new InvalidOperationException("Editor is not ready");
        if (!HexColor.TryParse(_input.Text, out var color, out var error)) return error;
        if (model.Plugin.Palette.SetSlot(model.Slot, color))
            model.Saved(PaletteController.Stored(color));
        return null;
    }

    private void UpdatePreview()
    {
        if (HexColor.TryParse(_input.Text, out var color, out var error))
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
            _hint.Foreground = PaletteScreen.Bad;
            _hint.Text = string.IsNullOrWhiteSpace(_input.Text) ? string.Empty : error;
        }
    }
}

internal sealed class PresetsModel : WheelDialogModel
{
    internal Action<IReadOnlyDictionary<int, HexColor>> Applied { get; }
    public PresetsModel(Plugin plugin, Action<IReadOnlyDictionary<int, HexColor>> applied) : base(plugin) => Applied = applied;
}

internal sealed class PresetsScreen : WheelDialog
{
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly TextBox _name = new() { Width = 260, Watermark = "New preset name", MaxLength = Settings.MaxPresetNameLength };

    public PresetsScreen()
    {
        var save = new Button { Content = "Save current palette" };
        save.Click += (_, _) => Run(Save);
        _name.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Run(Save);
        };
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Dispose();
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(_list);
        body.Children.Add(PaletteScreen.Row(_name, save));
        body.Children.Add(close);
        Build("Palette presets", body, 640);
    }

    private PresetsModel? Model => DataContext as PresetsModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Rebuild();
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        if (Model is not { } model) return;
        if (model.Plugin.SettingsError is { } error)
        {
            _list.Children.Add(new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap });
            return;
        }
        if (model.Plugin.Presets.Count == 0)
            _list.Children.Add(new TextBlock { Text = "No presets yet. Name the current palette below and save it." });
        foreach (var (name, colors) in model.Plugin.Presets)
        {
            var apply = new Button { Content = "Apply" };
            apply.Click += (_, _) => Run(() => Apply(name));
            var delete = new Button { Content = "Delete" };
            delete.Click += (_, _) => Run(() =>
            {
                model.Plugin.DeletePreset(name);
                Rebuild();
                return "Deleted " + name + ".";
            });
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            foreach (var hex in colors)
                if (HexColor.TryParse(hex, out var c, out _)) swatches.Children.Add(PaletteScreen.Swatch(c, 14));
            _list.Children.Add(PaletteScreen.Row(
                new TextBlock { Text = name, Width = 170, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
                swatches, apply, delete));
        }
    }

    private string? Apply(string name)
    {
        var model = Model ?? throw new InvalidOperationException("Presets are not ready");
        model.Plugin.ApplyPreset(name);
        var shown = new Dictionary<int, HexColor>();
        var colors = model.Plugin.Presets[name];
        var slots = model.Plugin.Palette.ReadPalette().Count;
        for (var i = 0; i < Math.Min(colors.Count, slots); i++)
            if (HexColor.TryParse(colors[i], out var color, out _)) shown[i] = PaletteController.Stored(color);
        model.Applied(shown);
        return null;
    }

    private string Save()
    {
        var model = Model ?? throw new InvalidOperationException("Presets are not ready");
        var palette = model.Plugin.Palette.ReadPalette();
        if (palette.Count == 0) throw new InvalidOperationException("Equip the paint gun first");
        var name = Settings.NormalizeName(_name.Text);
        model.Plugin.SavePreset(name, palette);
        _name.Text = string.Empty;
        Rebuild();
        return "Saved " + name + ".";
    }
}
