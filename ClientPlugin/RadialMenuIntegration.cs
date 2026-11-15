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
using Avalonia.VisualTree;
using HarmonyLib;
using Keen.Game2.Client.GameSystems.Toolbars.PaintTool;
using Keen.Game2.Client.UI.Library.Controls.QuickMenu;
using Keen.Game2.Client.UI.Library.Controls.RadialMenu;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.UI.Screens;

namespace Farvane.PaintColorPicker;

/// <summary>
/// Adds hex editing to the paint gun's colour wheel: middle-click a colour slice
/// (right-click stays vanilla "Back") and a Presets button in the wheel's empty centre.
/// Only the paint tool's quick menu is affected; other quick menus are left alone.
/// </summary>
internal sealed class RadialMenuIntegration : IDisposable
{
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
            Diag.Info("Colour wheel integration unavailable: " + error.GetType().Name + " " + error.Message);
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
            Diag.Failed("Extending a quick menu", error);
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
    private static bool TryGetColorLayer(QuickMenuScreen screen, out RadialMenuViewModel layer) =>
        TryGetColorLayer(screen, out layer, out _);

    private static bool TryGetColorLayer(QuickMenuScreen screen, out RadialMenuViewModel layer, out string reason)
    {
        layer = null!;
        if (screen.DataContext is not PaintToolMenuScreenViewModel menu)
        {
            reason = "quick menu is not the paint tool's (" + (screen.DataContext?.GetType().Name ?? "no view model") + ")";
            return false;
        }
        if (menu.ActiveLayer is not { } active)
        {
            reason = "paint tool menu has no active layer";
            return false;
        }
        if (!active.RadialMenuPanels.OfType<ColorRadialMenuPanel>().Any())
        {
            reason = "active layer has no colour slices (" + active.RadialMenuPanels.Count + " panels)";
            return false;
        }
        layer = active;
        reason = string.Empty;
        return true;
    }

    // Vanilla builds the colour layer as [Return, palette[0], palette[1], ...].
    private static List<ColorRadialMenuPanel> ColorPanels(RadialMenuViewModel layer) =>
        layer.RadialMenuPanels.OfType<ColorRadialMenuPanel>().ToList();

    /// <summary>Recolours slices in place so the open wheel matches what was sent.</summary>
    // The wheel control builds its slices in Draw() and does not watch the panel list, so it is redrawn explicitly.
    private static readonly Action<RadialMenu>? RedrawWheel = CreateRedraw();

    private static readonly Func<RadialMenu, object?>? WheelModel = CreateWheelModel();

    private static Func<RadialMenu, object?>? CreateWheelModel()
    {
        var field = AccessTools.Field(typeof(RadialMenu), "_viewModel");
        return field is null ? null : menu => field.GetValue(menu);
    }

    private static Action<RadialMenu>? CreateRedraw()
    {
        var draw = AccessTools.Method(typeof(RadialMenu), "Draw", Type.EmptyTypes);
        if (draw is null) Diag.Info("Wheel redraw unavailable (RadialMenu.Draw not found); colours update when the wheel reopens");
        return draw is null ? null : menu => draw.Invoke(menu, null);
    }

    private static void ShowColors(QuickMenuScreen screen, RadialMenuViewModel layer, IReadOnlyDictionary<int, ColorHSV> colors)
    {
        var panels = ColorPanels(layer);
        var shown = 0;
        foreach (var (slot, color) in colors)
        {
            if (slot < 0 || slot >= panels.Count)
            {
                Diag.Debug("Wheel has no slice for slot " + (slot + 1) + " (" + panels.Count + " colour slices)");
                continue;
            }
            var old = panels[slot];
            var position = layer.RadialMenuPanels.IndexOf(old);
            if (position < 0) continue;
            shown++;
            // Vanilla locates the slot by exact HSV equality when a slice is selected.
            layer.RadialMenuPanels[position] = new ColorRadialMenuPanel(color)
            {
                DisplayName = old.DisplayName,
                Icon = old.Icon,
                HoverIcon = old.HoverIcon,
                IsEnabled = old.IsEnabled
            };
        }
        Diag.Debug("Recoloured " + shown + " of " + colors.Count + " wheel slice(s)");
        if (shown > 0) Redraw(screen, layer);
    }

    private static void Redraw(QuickMenuScreen screen, RadialMenuViewModel layer)
    {
        if (RedrawWheel is null) return;
        try
        {
            var menus = screen.GetVisualDescendants().OfType<RadialMenu>()
                .Where(m => ReferenceEquals(m.DataContext, layer) || ReferenceEquals(WheelModel?.Invoke(m), layer)).ToList();
            foreach (var menu in menus) RedrawWheel(menu);
            if (menus.Count == 0) Diag.Info("Wheel redraw: no wheel control bound to the colour layer; colours update when the wheel reopens");
            else Diag.Debug("Redrew " + menus.Count + " wheel control(s)");
        }
        catch (Exception error)
        {
            Diag.Failed("Wheel redraw", error);
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
            if (!_screen.TryGetTarget(out var screen)) return;
            if (!TryGetColorLayer(screen, out var layer, out var reason))
            {
                Diag.Debug("Middle-click ignored: " + reason);
                return;
            }
            if (layer.HoveredItem is not ColorRadialMenuPanel hovered)
            {
                Diag.Debug("Middle-click ignored: no colour slice hovered (" + (layer.HoveredItem?.GetType().Name ?? "nothing") + ")");
                return;
            }
            var slot = ColorPanels(layer).IndexOf(hovered);
            if (slot < 0)
            {
                Diag.Debug("Middle-click ignored: hovered slice is not in the colour layer");
                return;
            }
            e.Handled = true;
            Diag.Debug("Middle-click on wheel slot " + (slot + 1));
            var model = new SlotEditorModel(_owner._plugin, slot,
                color => ShowColors(screen, layer, new Dictionary<int, ColorHSV> { [slot] = color }));
            _owner._plugin.ShowScreen<SlotEditorScreen>(model, ScreenLayer.Top);
        }

        private void OpenPresets()
        {
            if (!_screen.TryGetTarget(out var screen))
            {
                Diag.Info("Presets button: the colour wheel has already closed");
                return;
            }
            if (!TryGetColorLayer(screen, out var layer, out var reason))
            {
                Diag.Info("Presets button: " + reason);
                return;
            }
            Diag.Debug("Presets button clicked; " + ColorPanels(layer).Count + " colour slices, " +
                       _owner._plugin.Presets.Count + " preset(s)");
            var model = new PresetsModel(_owner._plugin, colors => ShowColors(screen, layer, colors));
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
                Diag.Debug("Paint tool wheel opened; Presets button " + (_centre is null ? "not added" : "added"));
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
                    Diag.Info("Quick menu layout not recognised (" + (screen.Content?.GetType().Name ?? "empty") + "); Presets button unavailable");
                    return null;
            }
        }
    }
}
