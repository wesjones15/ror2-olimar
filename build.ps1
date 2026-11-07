# Builds libpikmin's DLL and OlimarTest, staging both with an extracted-assets pointer.
# Intro audio is generated on first run under assets/ror2_audio, just like Mario.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1 [-RoR2Dir <game to compile against>]
#       [-InstallDir <game to install into>] [-NoInstall]
#
param(
    [string]$RoR2Dir = (Join-Path $PSScriptRoot "..\2026 Mod Stuff\RiskOfRain2_DLC4"),
    [string]$InstallDir = "",
    [string]$LibPikmin = (Join-Path $PSScriptRoot "..\libpikmin"),
    [switch]$NoInstall
)
$ErrorActionPreference = "Stop"
if (-not $NoInstall) {
    & (Join-Path $PSScriptRoot '../ror2-common/tests/CheckReleaseBranches.ps1') -HostDir $PSScriptRoot -NativeDir $LibPikmin
}
& (Join-Path $PSScriptRoot "../ror2-common/tests/CheckOverlapApi.ps1") -RoR2Dir $RoR2Dir
if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = [System.IO.Path]::GetFullPath((Join-Path $RoR2Dir "..\..\target"))
}

$cargo = Join-Path $env:USERPROFILE ".cargo\bin\cargo.exe"
if (-not (Test-Path $cargo)) { $cargo = "cargo" }

Write-Host "== libpikmin-ffi (pikmin.dll)"
Push-Location $LibPikmin
try { & $cargo build -p libpikmin-ffi --release; if ($LASTEXITCODE) { throw "cargo failed" } }
finally { Pop-Location }

Write-Host "== OlimarTest.dll"
dotnet build (Join-Path $PSScriptRoot "OlimarTest\OlimarTest.csproj") -c Release "-p:RoR2Dir=$RoR2Dir"
if ($LASTEXITCODE) { throw "dotnet build failed" }

$dist = Join-Path $PSScriptRoot "dist\OlimarTest"
New-Item -ItemType Directory -Force $dist | Out-Null
# Remove the old build-generated clip from an existing dist folder.
$legacyIntro = Join-Path $dist "spawn.wav"
if (Test-Path -LiteralPath $legacyIntro) { Remove-Item -LiteralPath $legacyIntro }
Copy-Item (Join-Path $PSScriptRoot "OlimarTest\bin\Release\netstandard2.1\OlimarTest.dll") $dist -Force
Copy-Item (Join-Path $LibPikmin "target\release\pikmin.dll") $dist -Force
# Discs and extracted files live in the wesleyasaurus-SharedFiles package beside the plugins (ror2-common
# SharedFiles); nothing path-related is staged here.
Remove-Item (Join-Path $dist "assets_dir.txt") -ErrorAction SilentlyContinue
Write-Host "built $dist"

if (-not $NoInstall) {
    $plugins = Join-Path $InstallDir "BepInEx\plugins\OlimarTest"
    New-Item -ItemType Directory -Force $plugins | Out-Null
    foreach ($file in @("OlimarTest.dll", "pikmin.dll")) {
        Copy-Item -LiteralPath (Join-Path $dist $file) -Destination $plugins -Force
    }
    # Older builds staged a path pointer here; it is no longer read.
    Remove-Item (Join-Path $plugins "assets_dir.txt") -ErrorAction SilentlyContinue
    & (Join-Path $PSScriptRoot '../ror2-common/sharedfiles/Stage.ps1') -PluginsDir (Split-Path $plugins)
    $legacyIntro = Join-Path $plugins "spawn.wav"
    if (Test-Path -LiteralPath $legacyIntro) { Remove-Item -LiteralPath $legacyIntro }
    Write-Host "installed into $plugins"
}
