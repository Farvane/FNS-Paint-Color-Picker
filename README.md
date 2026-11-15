# \[FNS\] GRIDWORX - Paint Gun Color Picker

**v1.1.0** — optional Pulsar Modern client plugin for Space Engineers 2.

Type hex colour codes (`#RRGGBB` or `#RGB`) into your paint gun palette slots,
see the colour the game will actually store before you apply it, and keep
named presets (for example the FNS livery) to reuse in other worlds.

Requires **Pulsar Modern** and **SE2 4.0.0.3169**. Other game builds are
disabled until validated. Windows is the release test platform.

## Install

1. Install Pulsar from <https://github.com/SpaceGT/Pulsar/releases> and run Modern.
2. Close SE2/Pulsar. Extract `FNSPaintColorPicker.dll` and
   `FNSPaintColorPicker.xml` from the release ZIP into the existing
   `Modern/Local` directory under your Pulsar installation.
3. Enable **FNSPaintColorPicker** in your Pulsar profile and launch SE2.

## Use from the colour wheel

1. Load a world, equip the paint gun, open its quick menu and go to **Colors**.
2. **Middle-click** a colour to type a hex code for that slot. Enter saves,
   Esc cancels. Right-click still goes back, and left-click still selects.
   Slots are edited by position, so duplicate colours (several whites) can
   each be changed individually.
3. Click **Presets** in the centre of the wheel to save the current palette
   under a name, apply a saved preset to all slots, or delete one. After
   **Apply**, the dialog stays open and reports how many slots were sent or
   already matched; close it with **Close** or Esc.

## Use from the plugin settings

1. Load a world and equip the paint gun.
2. Open the plugin's settings from the Pulsar plugin menu. Status should read
   **Ready**, and the slots list your current palette ("(selected)" marks the current slot).
3. Type a hex value next to a slot. The preview swatch and hint show what the
   game will store ("Exact", or "Stored as #…" if it rounds). Click **Set**,
   or fill several slots and click **Apply all entered**.
4. **Save current palette as preset** stores the world's palette under a name.
   **Load into slots** fills the inputs from a preset; review, then apply.

The paint palette is saved **per world** (in the world save, by the game).
Presets are stored by this plugin in `settings.json` in the Pulsar plugin
configuration directory. If that file is invalid, palette editing still works
and the presets section explains the problem; close the game and rename the
file to reset it.

## Built-in SE2 Default preset

Both preset lists start with **SE2 Default (built in)**: the paint gun's stock
palette for this game build. Applying it sends the game's exact HSV values (not
rounded hex), so restored slots match blocks painted with the stock colours. It
cannot be deleted, and the name is reserved; a saved preset already called
"SE2 Default" is kept as "SE2 Default (saved)".

## Troubleshooting and debug logging

Problems (a screen that cannot open, the wheel integration not attaching, an
unreadable `settings.json`) are always written to the SE2 log with the prefix
`[FNS Paint Color Picker]`, plus one startup line with the plugin version.

For more detail, open the plugin settings and tick **Debug logging** under
Troubleshooting. The setting is saved in `settings.json`. While it is on, the
plugin also logs `[debug]` lines for each slot it sends, preset and dialog
actions, why the wheel ignored a click or the Presets button, and whether the
game confirmed the new colours about two seconds after they were sent. Turn it
off again for normal play. The SE2 log is in `%AppData%\SpaceEngineers2\Temp\Logs`.

## How it works

The plugin calls the game's own networked palette update — the same call the
vanilla colour-pick action uses. The server applies it to the **sender's own**
palette only, so the plugin cannot change other players' colours. It sends only
slots that exist and colours already in the game's 0–1 HSV range. Nothing is
written to world files directly.

Two Harmony postfixes are used: one observes when the local paint tool loads
its palette, and one notices when a quick menu screen is created so the paint
tool's colour wheel can get a middle-click handler and the centre Presets
button. Other tools' quick menus are not changed. If the wheel integration
cannot attach, the settings screen still works.

Multiplayer has not been validated. Every player who wants hex entry needs the
plugin; others are unaffected.

Uninstall with SE2/Pulsar closed: disable the plugin in its profile and remove
`FNSPaintColorPicker.dll` from `Modern/Local`. Palette colours you set remain
in your worlds as normal game data.

## Development

Requires the installed game assemblies and .NET 10 SDK:

```powershell
.\build.ps1 -Game2Path 'path-to-your-SE2-Game2-directory'
```

Runs the game-independent unit tests (`Tests/`), builds a Release DLL and
packages it under `artifacts/`. No automatic deployment or publishing.
Source is MIT licensed. See `THIRD-PARTY-NOTICES.md`.

This is a community plugin, not an official Keen Software House product.
