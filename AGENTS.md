# Agent instructions

- After any mod changes, build it and install it locally with `./build.ps1 -Install` before finishing. If a running game locks the installed DLL, stage the new DLL beside it, rename the old DLL to a backup outside the scanned mod folder, move the staged DLL to the canonical filename, and verify its SHA-256. Never stop the game; a restart is only needed to load the updated code. Do not publish it to Workshop unless explicitly requested.

## Локальная установка: точный порядок

Работать из корня проекта `C:\Dev\sts2_ultimate_custom` в PowerShell. ID, namespace и имя сборки — `UltimateCustomRun`.

### 1. Обычная установка

```powershell
Set-Location 'C:\Dev\sts2_ultimate_custom'
$gamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2'
.\build.ps1 -GamePath $gamePath -Install
```

Для другой библиотеки Steam заменить только `$gamePath`, например на `E:\SteamLibrary\steamapps\common\Slay the Spire 2`.

Скрипт собирает Release, обновляет пакет `content/UltimateCustomRun`, создаёт ZIP в `artifacts` и устанавливает **оба** файла:

```text
<gamePath>/mods/UltimateCustomRun/UltimateCustomRun.dll
<gamePath>/mods/UltimateCustomRun/UltimateCustomRun.json
```

Один `dotnet build` или `./build.ps1` без `-Install` не обновляет установленный мод. Не копировать в игру весь `bin`, зависимости игры, исходники, `.deps.json` или `.runtimeconfig.json`. BaseLib и PCK нашему моду не нужны.

### 2. Если открытая игра заблокировала установленную DLL

Ошибка `The process cannot access the file ... because it is being used by another process` означает блокировку установленной DLL. Не закрывать игру и не убивать процесс пользователя.

После этой ошибки сборка и пакет обычно уже готовы. Подготовить новую DLL рядом со старой под временным именем, затем **переместить старую за пределы сканируемой папки `mods`** и заменить её новой:

```powershell
$packagePath = Join-Path (Get-Location).Path 'content\UltimateCustomRun'
$modPath = Join-Path $gamePath 'mods\UltimateCustomRun'
$installedDll = Join-Path $modPath 'UltimateCustomRun.dll'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$stagedDll = $installedDll + '.pending-' + $stamp
$backupPath = Join-Path $gamePath ('mod-backups\UltimateCustomRun\' + $stamp)
$backupDll = Join-Path $backupPath 'UltimateCustomRun.dll'

# Сначала новая сборка должна быть готова на диске.
Copy-Item -LiteralPath (Join-Path $packagePath 'UltimateCustomRun.dll') -Destination $stagedDll -ErrorAction Stop
New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
Get-Item -LiteralPath $installedDll | Select-Object FullName, Length
if (Test-Path -LiteralPath $backupDll) { throw 'Backup already exists' }
Move-Item -LiteralPath $installedDll -Destination $backupDll -ErrorAction Stop
Move-Item -LiteralPath $stagedDll -Destination $installedDll -ErrorAction Stop

# Повторить обязательную установку, чтобы скрипт обновил и DLL, и JSON.
.\build.ps1 -GamePath $gamePath -Install
```

Windows часто разрешает переименовать загруженную сборку, хотя запрещает её перезапись. Резервная папка находится на том же диске, рядом с `mods`, чтобы перемещение не превращалось в копирование между дисками.

Перед перемещением проверить конкретные абсолютные пути. Не перемещать каталоги целиком, не использовать маски, не трогать соседние моды. Временный файл имеет суффикс после `.dll`, поэтому не является второй DLL для загрузчика.

Если `Move-Item` тоже запрещён, остановиться и попросить пользователя закрыть игру; самостоятельно процесс не завершать. Если старая DLL уже перемещена, а новая не установилась, вернуть резервную DLL на канонический путь **только если он отсутствует**. Не перезаписывать существующий файл вслепую и не удалять резервные копии.

Это установка **для следующего запуска**, а не hot reload: открытая игра продолжает исполнять старую сборку.

### 3. Проверить установленный результат

После успешного `./build.ps1 -Install` проверить DLL **и** манифест. Использовать тот же `$gamePath`:

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

Установка считается законченной, когда скрипт завершился без ошибок, вывел `Installed: ...` и хеши обоих файлов совпали. `Build succeeded` само по себе подтверждает только сборку.

В финальном ответе сообщить о локальном обновлении и необходимости **полностью перезапустить игру** для загрузки новой DLL. Не утверждать, что изменение уже применилось к открытой игре или было визуально проверено, если этого не было.

### Ограничения

- Не публиковать в Workshop и не запускать `ModUploader` без явного запроса пользователя. `build.ps1 -Install` ничего не публикует.
- Не возвращать старый `mod_id.txt` из резервной папки: проект подготовлен к будущей публикации новым Workshop item.
- Старый ID `SpecializedChoice` отключён. Не воссоздавать его установку и не включать старую Workshop-копию вместе с новой: одинаковые модели могут вызвать конфликт при старте.
- Сохранять чужие незакоммиченные изменения. Задача только по документации не требует пересборки и установки чужой незавершённой реализации мода.
