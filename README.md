# FNS Paint Gun Color Picker

**v0.1.0 preview** — optional Pulsar Modern client plugin for Space Engineers 2.

Type hex colour codes (`#RRGGBB` or `#RGB`) into your paint gun palette slots,
see the colour the game will actually store before you apply it, and keep
named presets (for example the FNS livery) to reuse in other worlds.

Requires **Pulsar Modern** and **SE2 4.0.0.3169**. Other game builds are
disabled until validated. Windows is the release test platform.

## Install

1. Install Pulsar from https://github.com/SpaceGT/Pulsar/releases and run Modern.
2. Close SE2/Pulsar. Extract `FNSPaintColorPicker.dll` and
   `FNSPaintColorPicker.xml` from the release ZIP into the existing
   `Modern/Local` directory under your Pulsar installation.
3. Enable **FNSPaintColorPicker** in your Pulsar profile and launch SE2.

## Use

1. Load a world and equip the paint gun.
2. Open the plugin's settings from the Pulsar plugin menu. Status should read
   **Ready**, and the slots list your current palette (◄ marks the selected slot).
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

## How it works

The plugin calls the game's own networked palette update — the same call the
vanilla colour-pick action uses. The server applies it to the **sender's own**
palette only, so the plugin cannot change other players' colours. It sends only
slots that exist and colours already in the game's 0–1 HSV range. One Harmony
postfix observes when the local paint tool loads its palette; nothing else is
patched, and nothing is written to world files directly.

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
