# Ultimate Custom Run

Customize **Slay the Spire 2** custom runs with native value sliders, new modifier variants, extra rewards, and an organized selection menu.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3811713944) · [Report a bug](https://github.com/vertopolkaLF/ultimate-custom-run/issues/new/choose) · [Contributing](CONTRIBUTING.md) · [GPL-3.0 license](LICENSE)

**Version 1.7.0 · Pre-release · Built against game v0.111.0.** Available publicly on Steam Workshop and GitHub.

![Ultimate Custom Run artwork](image.png)

## Features

- Collapsible modifier groups: **Improved Start**, **Run Parameters**, **Modifiers**, **Card Pool**, **Ascension**, and **Negatives**. Options unavailable in singleplayer appear under **Disabled**.
- **Run Parameters** can set floors per act (vanilla or 8–30), base hand size (vanilla or 0–10, capped by the engine's 10-card hand limit), base energy (vanilla or 0–10), and enemy HP, enemy damage, and player HP multipliers (25–500%, step 25%).
- Named modifier presets: save the current selection and slider values as a loadout, then restore it from the dropdown. **Empty** clears the selected modifiers. Delete saved loadouts with the game's remove icon beside each dropdown item; deleting a loadout preserves the current run settings. Presets are stored locally, and the built-in Empty preset cannot be overwritten or removed.
- **Ascension** contains all ten Ascension effects as independent options, using the game's localized titles and descriptions. For example, enable Double Boss without any other Ascension penalties. Enabling an effect sets Ascension to 0; changing Ascension disables every independent Ascension option while preserving other modifiers.
- Native sliders with live value labels and descriptions. Use the mouse or focus a slider and press left/right to change it by one step.
- Linked mutually exclusive choices. Specialized, All Star, and Friendship keep their variants together under one checkbox.
- Submodifiers appear beneath their parent only while it is enabled.
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
| Draft | 5–20 cards | 5 | 10 |
| Sealed Deck — cards to choose | 5–(pool size − 5) cards | 5 | 10 |
| Sealed Deck — card pool | 10–60 offers | 5 | 30 |
| Insanity | 5–60 cards | 5 | 30 |
| Hoarder | 1–5 additional copies | 1 | 2 |
| Midas | 150–300% of normal gold rewards | 5% | 200% |
| Double Trouble | 2–3 bosses per act | 1 | 2 |
| Headstart | 1–5 relics | 1 | 1 |
| Rich Loot | 1–3 extra relic choices | 1 | 1 |
| Card Swarm | 1–3 extra card offers | 1 | 1 |
| ??? | 1–5 additional Events | 1 | 3 |
| Speedrun | 10–60 minutes before HP penalties | 5 | 30 |
| Dill — initial max HP | 1–20 HP | 1 | 1 |
| Dill — max HP per fight | 1–5 HP | 1 | 2 |

### Custom Run Parameters

Enable **Custom Run Parameters** to configure these six settings. Values persist in runs and presets and synchronize from the co-op host. The modifier's hover tip lists only changed settings, one per line, with their current values highlighted; unchanged settings are omitted.

| Parameter | Range | Step | Default |
| --- | --- | --- | --- |
| Floors per act | Vanilla or 8–30 total floors | 1 | Vanilla |
| Base hand size | Vanilla or 0–10 cards | 1 | Vanilla |
| Base energy | Vanilla or 0–10 energy | 1 | Vanilla |
| Enemy HP | 25–500% | 25% | 100% |
| Enemy damage | 25–500% | 25% | 100% |
| Player HP | 25–500% | 25% | 100% |

Floors per act includes the Ancient/start floor and the first boss floor. The configured total is the same in singleplayer and co-op. Double Trouble, Campfires between bosses, Double Boss, and ??? can add their own extra floors. New maps use the configured length, including maps generated after loading a run; an already-generated saved map retains its layout. Fully restart the game and start a new run to check the floor-count fix on Act 1.

Base hand size replaces the normal start-of-turn draw count; the engine's hand limit remains 10 cards. Base energy replaces the normal maximum energy. Enemy damage scales damage dealt by monsters to the opposing side, excluding source-less HP loss such as event damage. Player HP scales starting current and maximum HP; Dill overrides that initial HP when selected.

Normal, Draft, and Pick Any share the same value within a modifier family. Sealed Deck has two sliders: cards to choose and pool size. The first slider's maximum is always the second slider's value minus 5; reducing the pool automatically clamps the chosen-card count. Both values persist in saves, co-op settings, and presets. Extra Card Choice / Card Swarm does not enlarge the configured pool. Hoarder counts copies **in addition to** the original card and still blocks Merchant card removal. Midas uses a percentage of the normal gold reward, rounded down: 200% means twice the gold. It still blocks Smithing at Rest Sites.

## Added modifiers and variants

| Option | Effect |
| --- | --- |
| **Neow!!** | Restores the usual starter relic choice after the other starting effects. |
| **Double Trouble** | Fight 2–3 different Bosses at the end of every Act (step 1, default 2), with separate normal rewards from each, including the final Act. A10 does not add another boss on top of this count. The count persists in saves, presets, and co-op settings. |
| **Campfires between bosses** | A Double Trouble submodifier, visible only while its parent is enabled. Adds a normal Rest Site between every pair of bosses (one with 2 bosses, two with 3). Off by default. Saved with runs and presets, synchronized from the co-op host, and inactive without its parent. The boss/rest chain is preserved in native map saves. |
| **???** | Encounter 1–5 additional Events after Neow, before entering the main map (step 1, default 3). The count persists in saves and presets. Each occupies its own extra floor, uses the native shuffled, unlocked event pool, and is guaranteed to be an Event. Events with any custom appearance conditions are excluded even when those conditions currently pass; events never repeat within the chain. If no eligible unvisited events remain, the chain ends early and opens the main map. The generated map and its first three floors are unchanged. Supports saving/loading and synchronized co-op progression after all players proceed. |
| **Headstart** | Choose 1–5 distinct relics at Neow (step 1, default 1). Uses compendium relic tiles in a searchable, scrolling selection grid with native hover tips and explicit confirmation. Includes all unlocked, character-compatible rarities, including Ancient, Event, Shop, and other available Starter relics, subject to native Neow restrictions. Circlet, Deprecated Relic, and already-owned non-stackable relics are excluded. Each co-op player chooses independently through synchronized choice indexes; normal pickup effects remain active. The count persists in saves and presets. |
| **Ultimate Starter** | Replaces the normal basic Strikes and Defends with 3 Ultimate Strikes and 3 Ultimate Defends, preserving special starter cards such as Bash and Zap. Applies once when creating a run, for every co-op player. Draft, Sealed Deck, and Insanity replace the whole deck afterward if selected. |
| **Super Draft** | Choose card rewards to add to your starting deck until you skip. Each card chosen has a 0.5% chance to add a random Curse to your deck, doubling with each reward. Chances above 100% add guaranteed Curses plus a chance for another. |
| **Must Have (negative)** | Requires taking a card from every card reward before leaving. Rerolls remain available; non-card rewards remain optional. Passing during Super Draft is allowed. |
| **Speedrun (negative)** | Every full minute after the selected time limit, lose 5 HP. Limit: 10–60 minutes, step 5, default 30. Uses the native run timer, including its singleplayer pause behavior. Elapsed time and applied penalties persist in saves; the host schedules penalties for all players in co-op. |
| **Dill (negative)** | Start with 1 max HP and gain 2 max HP after each combat victory. Two sliders: initial HP 1–20 (step 1, default 1) and max HP per fight 1–5 (step 1, default 2). Current HP starts at the selected maximum, which overrides initial Ascension/HP scaling. Growth uses the game's normal max-HP gain and healing for surviving players in co-op. Both sliders persist in saves, co-op settings, and presets; loading does not reset earned max HP. |
| **Specialized — Normal** | Adds the selected number of copies of a random eligible card. |
| **Specialized — Draft** | Choose one Card Reward, then add the selected number of copies. Skipping adds no cards. |
| **Specialized — Pick Any** | Choose an eligible common, uncommon, or rare card from your character's pool, then add the selected number of copies. |
| **All Star — Draft** | Choose Colorless card rewards instead of receiving random Colorless cards. Each accepted reward adds one card. |
| **Friendship — Normal** | Adds the selected number of copies of a random Co-Op card from your character's pool. Co-op only. |
| **Friendship — Draft** | Choose the selected number of Co-Op card rewards. Each accepted reward adds one card. Co-op only. |
| **Colorless Cards** | Allows Colorless cards in card rewards through the native Dingy Rug relic. An existing copy is not granted again. |
| **Rich Loot** | Treasure chests contain 1–3 extra relics to choose from (step 1, default 1). Empty chests remain empty. |
| **Card Swarm** | Card rewards contain 1–3 extra offers (step 1, default 1), including standard Draft rewards and the mod's starting drafts. Fixed tutorial rewards are unchanged. |

Variants within each family are mutually exclusive. Draft, Sealed Deck, and Insanity are mutually exclusive. Super Draft adds cards after deck replacement and can be combined with those options. All Star and Friendship draft rewards can be skipped unless Must Have is enabled. Standard reward-generation hooks remain active, so other effects can modify offers; Sealed Deck keeps its configured pool size.

## Included vanilla modifiers

All vanilla custom-run options remain available. The following table completes the catalog alongside the added modifiers and variants above. Adjustable values use the ranges in **Adjustable values**.

| Modifier | Effect |
| --- | --- |
| **Specialized — Normal** | Start with 1–10 copies of one random eligible card (default 5). |
| **All Star — Normal** | Start with 1–10 random Colorless cards (default 5). |
| **Draft** | Choose 5–20 card rewards to replace the starting deck (step 5, default 10). |
| **Sealed Deck** | Replace the starting deck with cards chosen from a fixed offer pool; both the chosen-card count and pool size are adjustable. |
| **Insanity** | Replace the starting deck with 5–60 random cards (step 5, default 30). |
| **Hoarder** | Add 1–5 extra copies whenever a card enters your deck (default 2); Merchant card removal is disabled. |
| **Flight** | Ignore paths when choosing the next room. |
| **Vintage** | Normal enemies give relic rewards instead of card rewards. |
| **Character card pools** | Add the selected character's cards to card rewards and eligible Merchant offerings. Includes Ironclad, Silent, Regent, Necrobinder, and Defect options. |
| **Deadly Events** | Unknown rooms can contain Elites and are more likely to contain Treasure rooms. |
| **Cursed Run** | Add a random Curse to the deck at the start of every Act. |
| **Big Game Hunter** | More Elites appear; Elite card rewards contain Rare cards. |
| **Midas** | Enemies give 150–300% of normal gold rewards (step 5%, default 200%); Smithing is disabled. |
| **Murderous** | Players and enemies start each combat with 3 Strength. |
| **Night Terrors** | Resting heals all HP but costs 5 max HP. |
| **Terminal** | Lose 1 max HP on entering each new room; start each combat with 5 Plating. |

## Individual Ascension modifiers

Each effect can be selected independently. Names and in-game descriptions use the game's localization. Enabling one sets the run's Ascension level to 0; changing the Ascension level disables all individual Ascension modifiers while keeping the rest of the selection.

| Modifier | Effect |
| --- | --- |
| **Swarming Elites** | Elites spawn more often. |
| **Weary Traveler** | Ancients heal only 80% of missing HP. |
| **Poverty** | Enemies and Treasure Chests drop 25% less Gold. |
| **Tight Belt** | Start with one fewer potion slot. |
| **Ascender's Bane** | Start the run Cursed. |
| **Inflation** | Merchant card removal costs more. |
| **Scarcity** | Rare and Upgraded cards appear less often. |
| **Tough Enemies** | Enemies are harder to kill. |
| **Deadly Enemies** | Enemies have deadlier attacks. |
| **Double Boss** | Fight two bosses at the end of Act 3. With Double Trouble, its configured boss count takes precedence. |

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

The smoke suite patches the installed game assembly and checks native map generation for every floor count from 8 to 30 across all acts in singleplayer and co-op, map serialization, loaded-act floor overrides, registration, save and network-property serialization, cloning, slider ranges, native count patches, Midas gold rewards, draft selection, modifier grouping, and exclusivity. It also compares modifier selection for 100 Daily seeds with and without the mod's patches.

**These are managed integration checks, not an in-game playtest.** UI layout, input, actual deck acquisition, and live co-op behavior still need testing in the game. Compatibility with later game versions is not guaranteed; changed patch targets can require a mod update.

## Development and publishing

See [CONTRIBUTING.md](CONTRIBUTING.md) for setup, validation, and pull requests, and [AGENTS.md](AGENTS.md) for repository automation rules.

`build.ps1` only builds, packages, and optionally installs locally. Workshop publishing is a separate maintainer action using ModUploader. The current item ID is stored in `mod_id.txt`. Its description and visibility are maintained in `workshop.json`; uploading with a non-null description replaces edits made directly in Steam.

Workshop gallery screenshots are tracked in `previews/`. Keep every gallery image you want to retain there: ModUploader synchronizes that folder and removes additional previews missing from it. Screenshots are displayed in the Workshop gallery and this README; the Workshop description contains text only.

## License

Original project code and documentation are licensed under **GNU GPL version 3 only** (`GPL-3.0-only`); see [LICENSE](LICENSE). Copyright © 2026 vertopolkaLF.

Slay the Spire 2 and its game assets and dependencies belong to their respective owners and are not relicensed by this project. This is an unofficial community mod, not affiliated with or endorsed by Mega Crit.
