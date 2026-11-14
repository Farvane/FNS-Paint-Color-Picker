using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HarmonyLib;
using Keen.Game2.Client.GameSystems.Toolbars.PaintTool;
using Keen.Game2.Client.UI.Library.Controls.QuickMenu;
using Keen.Game2.Client.UI.Library.Controls.RadialMenu;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.UI.Screens;

namespace Farvane.PaintColorPicker;

/// <summary>
/// Adds hex editing to the paint gun's colour wheel: middle-click a colour slice
/// (right-click stays vanilla "Back") and a Presets button in the wheel's empty centre.
/// Only the paint tool's quick menu is affected; other quick menus are left alone.
/// </summary>
internal sealed class RadialMenuIntegration : IDisposable
{
    private const string Label = "[FNS Paint Color Picker] ";
    private const string HarmonyId = PaletteController.OwnerId + ".radial";

    private static RadialMenuIntegration? _instance;
    private readonly ConditionalWeakTable<QuickMenuScreen, ScreenState> _screens = new();
    private readonly Plugin _plugin;
    private readonly Harmony? _harmony;

    public RadialMenuIntegration(Plugin plugin)
    {
        _plugin = plugin;
        try
        {
            var constructor = AccessTools.Constructor(typeof(QuickMenuScreen), Type.EmptyTypes)
                ?? throw new InvalidOperationException("Quick menu screen constructor not found");
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(RadialMenuIntegration), nameof(OnScreenCreated)));
            _instance = this;
        }
        catch (Exception error)
        {
            // The settings screen still works without the wheel integration.
            Log.Default.WriteLine(Label + "Colour wheel integration unavailable: " + error.GetType().Name + " " + error.Message);
            _harmony?.UnpatchAll(HarmonyId);
        }
    }

    private static void OnScreenCreated(QuickMenuScreen __instance)
    {
        try
        {
            _instance?.Track(__instance);
        }
        catch (Exception error)
        {
            Log.Default.WriteLine(Label + "Could not extend quick menu: " + error.GetType().Name);
        }
    }

    private void Track(QuickMenuScreen screen)
    {
        var state = new ScreenState(this, screen);
        _screens.AddOrUpdate(screen, state);
        screen.AddHandler(InputElement.PointerReleasedEvent, state.OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        screen.AttachedToVisualTree += (_, _) => state.Attached();
        screen.DetachedFromVisualTree += (_, _) => state.Detached();
    }

    /// <summary>The colour layer of the paint tool's wheel, when it is the active layer.</summary>
    private static bool TryGetColorLayer(QuickMenuScreen screen, out RadialMenuViewModel layer)
    {
        layer = null!;
        if (screen.DataContext is not PaintToolMenuScreenViewModel menu || menu.ActiveLayer is not { } active)
            return false;
        if (!active.RadialMenuPanels.OfType<ColorRadialMenuPanel>().Any()) return false;
        layer = active;
        return true;
    }

    // Vanilla builds the colour layer as [Return, palette[0], palette[1], ...].
    private static List<ColorRadialMenuPanel> ColorPanels(RadialMenuViewModel layer) =>
        layer.RadialMenuPanels.OfType<ColorRadialMenuPanel>().ToList();

    /// <summary>Recolours slices in place so the open wheel matches what was sent.</summary>
    private static void ShowColors(RadialMenuViewModel layer, IReadOnlyDictionary<int, HexColor> colors)
    {
        var panels = ColorPanels(layer);
        foreach (var (slot, color) in colors)
        {
            if (slot < 0 || slot >= panels.Count) continue;
            var old = panels[slot];
            var position = layer.RadialMenuPanels.IndexOf(old);
            if (position < 0) continue;
            layer.RadialMenuPanels[position] = new ColorRadialMenuPanel(PaletteController.ToHsv(color))
            {
                DisplayName = old.DisplayName,
                Icon = old.Icon,
                HoverIcon = old.HoverIcon,
                IsEnabled = old.IsEnabled
            };
        }
    }

    public void Dispose()
    {
        _harmony?.UnpatchAll(HarmonyId);
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    private sealed class ScreenState
    {
        private readonly RadialMenuIntegration _owner;
        private readonly WeakReference<QuickMenuScreen> _screen;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private Control? _centre;

        public ScreenState(RadialMenuIntegration owner, QuickMenuScreen screen)
        {
            _owner = owner;
            _screen = new WeakReference<QuickMenuScreen>(screen);
            _timer.Tick += (_, _) => UpdateCentre();
        }

        private bool _centreTried;

        public void Attached()
        {
            UpdateCentre();
            _timer.Start();
        }

        public void Detached() => _timer.Stop();

        public void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Middle) return;
            if (!_screen.TryGetTarget(out var screen) || !TryGetColorLayer(screen, out var layer)) return;
            if (layer.HoveredItem is not ColorRadialMenuPanel hovered) return;
            var slot = ColorPanels(layer).IndexOf(hovered);
            if (slot < 0) return;
            e.Handled = true;
            var model = new SlotEditorModel(_owner._plugin, slot,
                color => ShowColors(layer, new Dictionary<int, HexColor> { [slot] = color }));
            _owner._plugin.ShowScreen<SlotEditorScreen>(model, ScreenLayer.Top);
        }

        private void OpenPresets()
        {
            if (!_screen.TryGetTarget(out var screen) || !TryGetColorLayer(screen, out var layer)) return;
            var model = new PresetsModel(_owner._plugin, colors => ShowColors(layer, colors));
            _owner._plugin.ShowScreen<PresetsScreen>(model, ScreenLayer.Top);
        }

        private void UpdateCentre()
        {
            if (!_screen.TryGetTarget(out var screen))
            {
                _timer.Stop();
                return;
            }
            // The view model may arrive after attachment; only the paint tool's menu gets the button.
            if (!_centreTried && screen.DataContext is PaintToolMenuScreenViewModel)
            {
                _centreTried = true;
                _centre = AddCentre(screen);
            }
            if (_centre is not null) _centre.IsVisible = TryGetColorLayer(screen, out _);
        }

        // Overlays the wheel's empty centre without changing vanilla controls.
        private Control? AddCentre(QuickMenuScreen screen)
        {
            var presets = new Button { Content = "Presets", HorizontalAlignment = HorizontalAlignment.Center };
            presets.Click += (_, _) => OpenPresets();
            var centre = new StackPanel
            {
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false
            };
            centre.Children.Add(presets);
            centre.Children.Add(new TextBlock
            {
                Text = "Middle-click a colour\nto type a hex code",
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.White,
                Opacity = 0.8,
                IsHitTestVisible = false
            });
            switch (screen.Content)
            {
                case Panel panel:
                    panel.Children.Add(centre);
                    return centre;
                case Control content:
                    screen.Content = null;
                    var grid = new Grid();
                    grid.Children.Add(content);
                    grid.Children.Add(centre);
                    screen.Content = grid;
                    return centre;
                default:
                    Log.Default.WriteLine(Label + "Quick menu layout not recognised; Presets button unavailable");
                    return null;
            }
        }
    }
}
