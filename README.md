# Ultimate Custom Run

Customize **Slay the Spire 2** custom runs with native value sliders, new modifier variants, extra rewards, and an organized selection menu.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3811713944) · [Report a bug](https://github.com/vertopolkaLF/ultimate-custom-run/issues/new/choose) · [Contributing](CONTRIBUTING.md) · [GPL-3.0 license](LICENSE)

**Version 1.7.0 · Pre-release · Built against game v0.111.0.** Available publicly on Steam Workshop and GitHub.

![Ultimate Custom Run artwork](image.png)

## Features

- Collapsible modifier groups: **Improved Start**, **Modifiers**, **Card Pool**, **Ascension**, and **Negatives**. Options unavailable in singleplayer appear under **Disabled**.
- Named modifier presets: save the current selection and slider values as a loadout, then restore it from the dropdown. Presets are stored locally.
- **Ascension** contains all ten Ascension effects as independent options, using the game's localized titles and descriptions. For example, enable Double Boss without any other Ascension penalties. Enabling an effect sets Ascension to 0; changing Ascension disables every independent Ascension option while preserving other modifiers.
- Native sliders with live value labels and descriptions. Use the mouse or focus a slider and press left/right to change it by one step.
- Linked mutually exclusive choices. Specialized, All Star, and Friendship keep their variants together under one checkbox.
- Values persist in saves and synchronize from the co-op host.
- Custom-only modifiers stay out of the Daily Challenge pool. Vanilla Daily values are preserved.
- No BaseLib dependency or additional PCK file.

## Screenshots

**Named modifier presets**

![Custom Run modifier presets with a saved loadout selected](previews/modifier-presets.jpg)

**Starting decks and native value sliders**

![Insanity set to 60 cards, All Star variants, and Friendship draft options](previews/custom-run-modifiers.jpg)

**Extra rewards and card pools**

![Grouped modifiers including Rich Loot, Card Swarm, and Colorless Cards](previews/rewards-and-card-pools.jpg)

**Individual Ascension effects**

![Independent Ascension effects selected at Ascension 0, including Double Boss](previews/ascension-modifiers.jpg)

## Adjustable values

Enable a modifier to reveal its slider. Original values remain the defaults.

| Modifier | Range | Step | Default |
| --- | --- | --- | --- |
| Specialized, All Star, Friendship | 1–10 cards or copies | 1 | 5 |
| Draft, Sealed Deck | 5–20 cards | 5 | 10 |
| Insanity | 5–60 cards | 5 | 30 |
| Hoarder | 1–5 additional copies | 1 | 2 |
| Midas | 150–300% of normal gold rewards | 5% | 200% |

Normal, Draft, and Pick Any share the same value within a modifier family. Sealed Deck still offers a pool of 30 cards. Hoarder counts copies **in addition to** the original card and still blocks Merchant card removal. Midas uses a percentage of the normal gold reward, rounded down: 200% means twice the gold. It still blocks Smithing at Rest Sites.

## New options

| Option | Effect |
| --- | --- |
| **Neow!!** | Restores the usual starter relic choice after the other starting effects. |
| **Specialized — Normal** | Adds the selected number of copies of a random eligible card. |
| **Specialized — Draft** | Choose one Card Reward, then add the selected number of copies. Skipping adds no cards. |
| **Specialized — Pick Any** | Choose an eligible common, uncommon, or rare card from your character's pool, then add the selected number of copies. |
| **All Star — Draft** | Choose Colorless card rewards instead of receiving random Colorless cards. Each accepted reward adds one card. |
| **Friendship — Normal** | Adds the selected number of copies of a random Co-Op card from your character's pool. Co-op only. |
| **Friendship — Draft** | Choose the selected number of Co-Op card rewards. Each accepted reward adds one card. Co-op only. |
| **Colorless Cards** | Allows Colorless cards in card rewards through the native Dingy Rug relic. An existing copy is not granted again. |
| **Rich Loot** | Treasure chests contain one extra relic to choose from. Empty chests remain empty. |
| **Card Swarm** | Card rewards contain one extra offer, including standard Draft rewards and the mod's starting drafts. Fixed tutorial rewards are unchanged. |

Variants within each family are mutually exclusive. All Star and Friendship draft rewards can be skipped. Standard reward-generation hooks remain active, so other effects can modify offers.

## Install and play

### Steam Workshop

Subscribe to [Ultimate Custom Run](https://steamcommunity.com/sharedfiles/filedetails/?id=3811713944), then let Steam download it.

### Local package

Copy these two files from `content/UltimateCustomRun/` into `<game-folder>/mods/UltimateCustomRun/`:

```text
UltimateCustomRun.dll
UltimateCustomRun.json
```

The repository includes the packaged DLL; you can also build it yourself using the instructions below. Do not copy the entire build output or the game's dependencies into `mods`.

### In the game

1. Fully restart the game after installing or updating the mod.
2. Enable **Ultimate Custom Run** in **Settings → Mod Settings**.
3. Start a **Custom Run**, select modifiers and adjust their values. Use the loadout dropdown to restore a preset or **Save** to store the current selection.
4. Resolve the selected starting effects at Neow.

Every co-op player needs the same mod version. Only the host can change slider values. Keep one active installation: do not enable the old **SpecializedChoice** mod alongside Ultimate Custom Run, or duplicate models may conflict at startup.

## Build from source

Requirements: Windows, **.NET SDK 9 or later**, and an installed Windows copy of Slay the Spire 2. The project references `sts2.dll`, `GodotSharp.dll`, and `0Harmony.dll` from the game's `data_sts2_windows_x86_64` directory. These dependencies are not included in this repository.

Run PowerShell from the repository root:

```powershell
# Build the Release DLL, refresh the package, and create a ZIP.
.\build.ps1

# Build and install locally using the default Steam library.
.\build.ps1 -Install

# Use a different Steam library.
.\build.ps1 -GamePath 'E:\SteamLibrary\steamapps\common\Slay the Spire 2' -Install
```

The default game path is `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2`. Outputs are `content/UltimateCustomRun/` and `artifacts/UltimateCustomRun-1.7.0.zip`.

An open game can lock the installed DLL. Close the game before a manual update, or use the staged replacement procedure in [AGENTS.md](AGENTS.md). Updating files is not hot reload: a full restart is required to load the new code.

## Testing and compatibility

```powershell
dotnet run --project tests/Smoke -c Release

# Both the build references and the runtime resolver need the custom path.
dotnet run --project tests/Smoke -c Release '-p:Sts2Path=E:\SteamLibrary\steamapps\common\Slay the Spire 2' -- 'E:\SteamLibrary\steamapps\common\Slay the Spire 2'
```

The smoke suite patches the installed game assembly and checks registration, save and network-property serialization, cloning, slider ranges, native count patches, Midas gold rewards, draft selection, modifier grouping, and exclusivity. It also compares modifier selection for 100 Daily seeds with and without the mod's patches.

**These are managed integration checks, not an in-game playtest.** UI layout, input, actual deck acquisition, and live co-op behavior still need testing in the game. Compatibility with later game versions is not guaranteed; changed patch targets can require a mod update.

## Development and publishing

See [CONTRIBUTING.md](CONTRIBUTING.md) for setup, validation, and pull requests, and [AGENTS.md](AGENTS.md) for repository automation rules.

`build.ps1` only builds, packages, and optionally installs locally. Workshop publishing is a separate maintainer action using ModUploader. The current item ID is stored in `mod_id.txt`. Its description and visibility are maintained in `workshop.json`; uploading with a non-null description replaces edits made directly in Steam.

Workshop gallery screenshots are tracked in `previews/`. Keep every gallery image you want to retain there: ModUploader synchronizes that folder and removes additional previews missing from it. Screenshots are displayed in the Workshop gallery and this README; the Workshop description contains text only.

## License

Original project code and documentation are licensed under **GNU GPL version 3 only** (`GPL-3.0-only`); see [LICENSE](LICENSE). Copyright © 2026 vertopolkaLF.

Slay the Spire 2 and its game assets and dependencies belong to their respective owners and are not relicensed by this project. This is an unofficial community mod, not affiliated with or endorsed by Mega Crit.
