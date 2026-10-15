# Builds libpikmin's DLL and the OlimarTest plugin, and installs both into the game's
# BepInEx/plugins/OlimarTest/ folder along with a pointer to libpikmin's extracted assets.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1 [-RoR2Dir <game to compile against>]
#       [-InstallDir <game to install into>] [-NoInstall]
#
param(
    [string]$RoR2Dir = (Join-Path $PSScriptRoot "..\2026 Mod Stuff\RiskOfRain2_DLC4"),
    [string]$InstallDir = $RoR2Dir,
    [string]$LibPikmin = (Join-Path $PSScriptRoot "..\libpikmin"),
    [switch]$NoInstall
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path (Join-Path $InstallDir "BepInEx"))) { $InstallDir = $RoR2Dir }

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
# piki_scene.c maps the Impact Site to BGM_Tutorial (4, tutorial.jam).
Push-Location $LibPikmin
try {
    & $cargo run --offline --release -p libpikmin-audio --example render -- $dist 4 3.5 --bgm-only
    if ($LASTEXITCODE) { throw "spawn music render failed" }
    Move-Item -LiteralPath (Join-Path $dist "bgm_4.wav") -Destination (Join-Path $dist "spawn.wav") -Force
} finally { Pop-Location }
Copy-Item (Join-Path $PSScriptRoot "OlimarTest\bin\Release\netstandard2.1\OlimarTest.dll") $dist -Force
Copy-Item (Join-Path $LibPikmin "target\release\pikmin.dll") $dist -Force
# Where the assets extracted from your own disc live (libpikmin/assets). If it isn't there
# yet the plugin builds it on first run from the disc set in its config.
$assets = [System.IO.Path]::GetFullPath((Join-Path $LibPikmin "assets"))
[System.IO.File]::WriteAllText((Join-Path $dist "assets_dir.txt"), $assets)
Write-Host "built $dist (assets: $assets)"

if (-not $NoInstall) {
    $plugins = Join-Path $InstallDir "BepInEx\plugins\OlimarTest"
    New-Item -ItemType Directory -Force $plugins | Out-Null
    Copy-Item (Join-Path $dist "*") $plugins -Force
    Write-Host "installed into $plugins"
}
