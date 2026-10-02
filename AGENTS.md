# Agent instructions

- After mod implementation changes, build and install locally with `./build.ps1 -Install` before finishing. A documentation-only task does not require rebuilding or installing unrelated unfinished mod work.
- Never stop the user's game. If the running game locks the installed DLL, follow the staged replacement procedure below. A full restart is required to load the new code; installation is not hot reload.
- Do not publish to Workshop or run ModUploader without an explicit user request.
- Preserve unrelated uncommitted changes. Do not reset or stash another contributor's work.
- Always commit after finishing the task, including only the files belonging to the task.
- Use English for repository documentation and code comments. Preserve intentional in-game localization.

## Local installation

Run PowerShell from the repository root. The mod ID, namespace, assembly name, and installed directory are `UltimateCustomRun`.

### 1. Standard installation

```powershell
$gamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2'
.\build.ps1 -GamePath $gamePath -Install
```

For another Steam library, change only `$gamePath`, for example to `E:\SteamLibrary\steamapps\common\Slay the Spire 2`.

The script builds Release, refreshes `content/UltimateCustomRun`, creates a ZIP in `artifacts`, and installs both files:

```text
<gamePath>/mods/UltimateCustomRun/UltimateCustomRun.dll
<gamePath>/mods/UltimateCustomRun/UltimateCustomRun.json
```

`dotnet build` or `./build.ps1` without `-Install` does not update the installed mod. Do not copy the entire `bin` directory, game dependencies, source files, `.deps.json`, or `.runtimeconfig.json` into the game. This mod requires neither BaseLib nor an additional PCK.

### 2. If the running game locks the installed DLL

An error stating `The process cannot access the file ... because it is being used by another process` means the installed DLL is locked. Do not close the game or terminate the user's process.

The build and package are usually already ready after this error. Stage the new DLL beside the installed one, move the old DLL **outside the scanned `mods` directory**, and replace it with the staged file:

```powershell
$packagePath = Join-Path (Get-Location).Path 'content\UltimateCustomRun'
$modPath = Join-Path $gamePath 'mods\UltimateCustomRun'
$installedDll = Join-Path $modPath 'UltimateCustomRun.dll'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$stagedDll = $installedDll + '.pending-' + $stamp
$backupPath = Join-Path $gamePath ('mod-backups\UltimateCustomRun\' + $stamp)
$backupDll = Join-Path $backupPath 'UltimateCustomRun.dll'

# Inspect these exact absolute paths before moving any file.
[IO.Path]::GetFullPath($installedDll)
[IO.Path]::GetFullPath($stagedDll)
[IO.Path]::GetFullPath($backupDll)
Get-Item -LiteralPath $installedDll | Select-Object FullName, Length

# The new build must exist on disk before moving the old one.
Copy-Item -LiteralPath (Join-Path $packagePath 'UltimateCustomRun.dll') -Destination $stagedDll -ErrorAction Stop
New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
if (Test-Path -LiteralPath $backupDll) { throw 'Backup already exists' }
Move-Item -LiteralPath $installedDll -Destination $backupDll -ErrorAction Stop
try {
    Move-Item -LiteralPath $stagedDll -Destination $installedDll -ErrorAction Stop
}
catch {
    if (-not (Test-Path -LiteralPath $installedDll)) {
        Move-Item -LiteralPath $backupDll -Destination $installedDll -ErrorAction Stop
    }
    throw
}

# Repeat installation so the script updates both the DLL and JSON manifest.
.\build.ps1 -GamePath $gamePath -Install
```

Windows often permits renaming a loaded assembly even when overwriting it is forbidden. Keep the backup on the same drive, beside `mods`, so moving it does not become a cross-drive copy.

Before moving a file, verify the resolved absolute source and destination paths. Move only the named DLL: never move entire directories, use wildcards, or touch neighboring mods. The staged filename has a suffix after `.dll`, so the loader does not see a second DLL.

If `Move-Item` is also blocked, stop and ask the user to close the game. Do not terminate it yourself. If the old DLL was moved but installation of the new DLL failed, restore the backup **only when the canonical path is absent**. Never overwrite an existing file blindly or delete backups.

This installs the update for the next launch. The currently running game continues executing the old assembly.

### 3. Verify the installed result

After a successful installation, check both the DLL and manifest using the same `$gamePath`:

```powershell
$packagePath = Join-Path (Get-Location).Path 'content\UltimateCustomRun'
$modPath = Join-Path $gamePath 'mods\UltimateCustomRun'
foreach ($name in 'UltimateCustomRun.dll', 'UltimateCustomRun.json') {
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $packagePath $name)).Hash
    $installedHash = (Get-FileHash -LiteralPath (Join-Path $modPath $name)).Hash
    if ($sourceHash -ne $installedHash) { throw "Installed file differs: $name" }
}
$manifest = Get-Content -LiteralPath (Join-Path $modPath 'UltimateCustomRun.json') -Raw -Encoding utf8 | ConvertFrom-Json
if ($manifest.id -ne 'UltimateCustomRun') { throw 'Wrong mod ID' }
$manifest | Select-Object id, name, version
```

Installation is complete only when the script finishes without errors, prints `Installed: ...`, and both file hashes match. `Build succeeded` confirms only the build.

After implementation changes, the final response must report local installation and the need to **fully restart the game** to load the new DLL. Do not claim the update is active in an open game or visually verified unless that was actually tested. Documentation-only changes do not require a game restart.

## Workshop identity and publishing

- `build.ps1 -Install` does not publish anything.
- The current Workshop item ID is stored in the repository's `mod_id.txt`. Keep it intact when updating the item. Do not restore an older `mod_id.txt` from an identity backup.
- The legacy `SpecializedChoice` identity is disabled. Do not recreate its installation or enable its old Workshop copy alongside Ultimate Custom Run; duplicate models can conflict at startup.
- `workshop.json` is the maintained source for the Workshop description. Uploading with a non-null `description` replaces manual Steam edits. Use `null` only when the user explicitly chooses to maintain the description in Steam instead.
- Preserve the configured Workshop visibility unless the user explicitly requests a change.
