# Contributing to Ultimate Custom Run

Bug reports, gameplay testing, documentation improvements, and focused code changes are welcome. Use English for issues, pull requests, documentation, and code comments; localized in-game text may use the target language.

## Report a bug or suggest a feature

Search [existing issues](https://github.com/vertopolkaLF/ultimate-custom-run/issues) first, then use the [issue templates](https://github.com/vertopolkaLF/ultimate-custom-run/issues/new/choose).

For bugs, include your mod version, game version, OS, installation method, enabled modifiers and values, other active mods, and whether the run is singleplayer or co-op. Provide reproducible steps, expected and actual behavior, and relevant logs or screenshots. For co-op, state the number of players and whether you were the host or a client.

Remove personal information, tokens, and unrelated data before sharing logs. Do not upload proprietary game assemblies or game asset packs.

For feature requests, describe the gameplay problem and the desired behavior. Include interactions with existing modifiers and co-op when relevant.

## Set up a development environment

1. Install .NET SDK 9 or later and the Windows version of Slay the Spire 2.
2. Fork and clone the repository, then create a branch for your change.
3. Run PowerShell from the repository root.

```powershell
.\build.ps1
dotnet run --project tests/Smoke -c Release
```

The default Steam library is `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2`. For another installation:

```powershell
$gamePath = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2'
.\build.ps1 -GamePath $gamePath
dotnet run --project tests/Smoke -c Release "-p:Sts2Path=$gamePath" -- $gamePath
```

References come from the installed game's `data_sts2_windows_x86_64` directory. NuGet sources are intentionally empty: the mod has no external NuGet dependencies. A clean checkout without the game cannot build the mod or run the integration suite.

## Project layout

| Path | Purpose |
| --- | --- |
| `src/UltimateCustomRun/` | Modifier models, native UI integration, and Harmony patches |
| `tests/Smoke/` | Managed integration checks against the installed game |
| `content/UltimateCustomRun/` | Packaged mod DLL and manifest |
| `build.ps1` | Release build, ZIP packaging, and optional local installation |
| `workshop.json` | Workshop title, description, visibility, and change note |
| `mod_id.txt` | Current Workshop item ID; do not replace it with an older ID |
| `AGENTS.md` | Automation rules and safe installation procedure |

## Make a focused change

- Keep gameplay behavior, descriptions, and slider ranges consistent.
- Preserve native RNG, reward hooks, and synchronized player choices when extending existing modifiers.
- Keep Custom Run additions isolated from Daily Challenge pools and defaults.
- Ensure configurable values survive save/load, model cloning, and network serialization.
- Respect host/client controls and singleplayer restrictions.
- Use the existing game resources and Control/container patterns for UI changes.
- Preserve unrelated local work. Do not reset or stash another contributor's changes.
- Avoid dependencies unless the change needs them and the tradeoff is explained.

## Validate your change

For mod implementation changes:

```powershell
.\build.ps1 -Install
dotnet run --project tests/Smoke -c Release
git diff --check
```

Use `-GamePath` and the matching test arguments for a non-default installation. If the running game locks the DLL, follow [AGENTS.md](AGENTS.md): stage the replacement, move the old DLL outside `mods`, and verify the installed DLL and manifest hashes. Never terminate someone else's game process.

Restart the game yourself before playtesting. Check the behavior affected by your change: menu layout and input, starting rewards, save/load, modifier combinations, and co-op host/client behavior as applicable. State which checks you actually ran and what remains unverified. Passing the smoke suite does not establish visual or live multiplayer correctness.

Documentation-only changes do not require rebuilding or installing the mod. Check spelling, relative links, commands, and Markdown/YAML/JSON syntax instead.

Keep uploader logs, build intermediates, tooling, and local secrets out of commits. The repository tracks the mod's packaged DLL; refresh it for implementation changes, but never include game dependencies in the package.

## Submit a pull request

Use the pull request template. Explain the problem, the resulting behavior, and validation. Link a related issue if one exists. Keep each pull request focused and make clear which game version you tested against.

Do not publish to Workshop or change the item's visibility as part of a contribution. Publishing is handled by the maintainer. Keep the existing Workshop identity intact.

By submitting a contribution, you agree to license it under the project's [GNU GPL version 3 only license](LICENSE). Third-party code or assets must retain their applicable notices and have compatible usage terms.
