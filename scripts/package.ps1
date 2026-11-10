<#
.SYNOPSIS
  Builds LoMSaveManager (Release) and packs a release zip into dist\.

.DESCRIPTION
  Produces dist\LoMSaveManager-<version>.zip, where <version> is read from the PluginVersion constant in
  src\Plugin\SaveManagerPlugin.cs. The zip is meant to be extracted straight into the game folder (the one
  that contains Mortal.exe); its layout is:

    BepInEx/plugins/LoMSaveManager/LoMSaveManager.dll
    BepInEx/plugins/LoMSaveManager/README.md
    BepInEx/plugins/LoMSaveManager/LICENSE

  README.md and LICENSE sit inside the plugin folder (not at the zip root) so that extracting into the game
  folder leaves nothing else behind there, and uninstalling (deleting the plugin folder) removes them too.

  Unlike deploy.ps1 this never copies anything into the game and does not care whether the game is running.
  The build only READS the game's DLLs as compile references (see -GameDir); nothing is written there.

  A <zip>.sha256 file (sha256sum format) is written next to the zip, and the SHA-256 is printed.

  If script execution is disabled on this machine, run it as:
    powershell -ExecutionPolicy Bypass -File scripts\package.ps1

.PARAMETER GameDir
  The game's install folder, used only to resolve compile-time references. When omitted the default in
  Directory.Build.props (the standard Steam path) applies.
#>
[CmdletBinding()]
param(
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Plugin\LoMSaveManager.csproj'
$versionSource = Join-Path $repoRoot 'src\Plugin\SaveManagerPlugin.cs'
$builtDll = Join-Path $repoRoot 'src\Plugin\bin\Release\net48\LoMSaveManager.dll'
$readme = Join-Path $repoRoot 'README.md'
$license = Join-Path $repoRoot 'LICENSE'
$distDir = Join-Path $repoRoot 'dist'

# Version: the single source of truth is the BepInPlugin version constant.
$match = Select-String -LiteralPath $versionSource -Pattern 'PluginVersion\s*=\s*"([^"]+)"' | Select-Object -First 1
if (-not $match) {
    throw "Could not find the PluginVersion constant in $versionSource."
}
$version = $match.Matches[0].Groups[1].Value

foreach ($required in @($readme, $license)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Missing $required. Nothing was packaged."
    }
}

# Release build (warnings are errors via Directory.Build.props). No game-running check: nothing is deployed.
$buildArgs = @($project, '-c', 'Release')
if ($PSBoundParameters.ContainsKey('GameDir')) {
    $buildArgs += "-p:GameDir=$GameDir"
}
dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "Build failed (exit code $LASTEXITCODE). Nothing was packaged."
}
if (-not (Test-Path -LiteralPath $builtDll)) {
    throw "Build succeeded but $builtDll does not exist. Nothing was packaged."
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$zipPath = Join-Path $distDir "LoMSaveManager-$version.zip"
$hashPath = "$zipPath.sha256"
Remove-Item -LiteralPath $zipPath, $hashPath -Force -ErrorAction SilentlyContinue

# Compress-Archive (Windows PowerShell 5.1) writes backslashes into entry names, which breaks extraction on
# some tools, so the entries are added by hand with forward-slash names.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$pluginPrefix = 'BepInEx/plugins/LoMSaveManager/'
$entries = [ordered]@{
    ($pluginPrefix + 'LoMSaveManager.dll') = $builtDll
    ($pluginPrefix + 'README.md')          = $readme
    ($pluginPrefix + 'LICENSE')            = $license
}

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $entries.Keys) {
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $entries[$name], $name, [System.IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $zip.Dispose()
}

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $hashPath -Value "$hash *$(Split-Path -Leaf $zipPath)" -Encoding Ascii

Write-Host ''
Write-Host "Packaged LoMSaveManager $version"
Write-Host "  zip:     $zipPath"
Write-Host "  sha256:  $hash"
Write-Host '  entries:'
$check = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($entry in $check.Entries) {
        Write-Host ('    {0}  ({1} bytes)' -f $entry.FullName, $entry.Length)
    }
}
finally {
    $check.Dispose()
}
