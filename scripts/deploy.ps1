<#
.SYNOPSIS
  Builds LoMSaveManager (Release) and copies the single DLL into the game's BepInEx plugins folder.

.DESCRIPTION
  Refuses to deploy while the game (the "Mortal" process) is running, because BepInEx has the plugin
  loaded and the copy would either fail or leave a half-replaced plugin behind.

  If script execution is disabled on this machine, run it as:
    powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1

.PARAMETER GameDir
  The game's install folder (the one that contains BepInEx and Mortal_Data).
#>
[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\LegendOfMortal'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Plugin\LoMSaveManager.csproj'
$builtDll = Join-Path $repoRoot 'src\Plugin\bin\Release\net48\LoMSaveManager.dll'
$pluginDir = Join-Path $GameDir 'BepInEx\plugins\LoMSaveManager'

dotnet build $project -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) {
    throw "Build failed (exit code $LASTEXITCODE). Nothing was deployed."
}

if (Get-Process -Name 'Mortal' -ErrorAction SilentlyContinue) {
    throw 'The game (Mortal) is running. Close it and run this again. Nothing was deployed.'
}

New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item -LiteralPath $builtDll -Destination $pluginDir -Force
Write-Host "Deployed LoMSaveManager.dll to $pluginDir"
