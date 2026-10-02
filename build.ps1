param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2',
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\UltimateCustomRun\UltimateCustomRun.csproj'
dotnet build $project -c Release "-p:Sts2Path=$GamePath"
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
$package = Join-Path $PSScriptRoot 'content\UltimateCustomRun'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\UltimateCustomRun\bin\Release\net9.0\UltimateCustomRun.dll') -Destination $package
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
Compress-Archive -LiteralPath $package -DestinationPath (Join-Path $artifacts 'UltimateCustomRun-1.7.0.zip') -Force
if ($Install) {
    $destination = Join-Path $GamePath 'mods\UltimateCustomRun'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($file in 'UltimateCustomRun.dll', 'UltimateCustomRun.json') {
        Copy-Item -LiteralPath (Join-Path $package $file) -Destination $destination
    }
    Write-Host "Installed: $destination"
}
Write-Host "Workshop content: $package"
Write-Host "ZIP: $(Join-Path $artifacts 'UltimateCustomRun-1.7.0.zip')"
