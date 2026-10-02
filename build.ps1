param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2',
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\SpecializedChoice\SpecializedChoice.csproj'
dotnet build $project -c Release "-p:Sts2Path=$GamePath"
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
$package = Join-Path $PSScriptRoot 'content\SpecializedChoice'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\SpecializedChoice\bin\Release\net9.0\SpecializedChoice.dll') -Destination $package
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
Compress-Archive -LiteralPath $package -DestinationPath (Join-Path $artifacts 'UltimateCustomRun-1.6.1.zip') -Force
if ($Install) {
    $destination = Join-Path $GamePath 'mods\SpecializedChoice'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($file in 'SpecializedChoice.dll', 'SpecializedChoice.json') {
        Copy-Item -LiteralPath (Join-Path $package $file) -Destination $destination
    }
    Write-Host "Installed: $destination"
}
Write-Host "Workshop content: $package"
Write-Host "ZIP: $(Join-Path $artifacts 'UltimateCustomRun-1.6.1.zip')"
