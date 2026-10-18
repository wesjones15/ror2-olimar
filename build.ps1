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
# Where the assets extracted from your own disc live (libpikmin/assets). If it isn't there
# yet the plugin builds it on first run from the disc set in its config.
$assets = [System.IO.Path]::GetFullPath((Join-Path $LibPikmin "assets"))
[System.IO.File]::WriteAllText((Join-Path $dist "assets_dir.txt"), $assets)
Write-Host "built $dist (assets: $assets)"

if (-not $NoInstall) {
    $plugins = Join-Path $InstallDir "BepInEx\plugins\OlimarTest"
    New-Item -ItemType Directory -Force $plugins | Out-Null
    foreach ($file in @("OlimarTest.dll", "pikmin.dll", "assets_dir.txt")) {
        Copy-Item -LiteralPath (Join-Path $dist $file) -Destination $plugins -Force
    }
    $legacyIntro = Join-Path $plugins "spawn.wav"
    if (Test-Path -LiteralPath $legacyIntro) { Remove-Item -LiteralPath $legacyIntro }
    Write-Host "installed into $plugins"
}
