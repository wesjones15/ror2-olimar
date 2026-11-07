# Development

Source layout, builds and checks for `ror2-olimar`. Run the commands below from the repository root. Player installation, controls and configuration are in the [README](../README.md).

## Repository relationships

| Repository | Role |
| --- | --- |
| **ror2-olimar** (this repository) | C# survivor/mode registration and world, combat, item, input, UI and network adapters. The DLL retains its historical name, `OlimarTest.dll`. |
| [libpikmin](https://github.com/wesjones15/libpikmin) | Native simulation, enemies, extraction, presentation, audio and the `pikmin.dll` API. |
| [ror2-common](https://github.com/wesjones15/ror2-common) | Shared C# systems compiled into the plugin. |
| [libcommon](https://github.com/wesjones15/libcommon), [libsms](https://github.com/wesjones15/libsms), [libssbm](https://github.com/wesjones15/libssbm) | Native source dependencies for shared utilities and presentation/asset support. |
| [ror2-characters](https://github.com/wesjones15/ror2-characters) | Package assembly and releases. |

## Build and checks

Build on Windows with PowerShell 5.1, Rust supporting edition 2024, a .NET SDK and local DLC4/BepInEx assemblies. The plugin targets `netstandard2.1`; console checks target .NET 6. Keep the repositories above as siblings: use `modern/1.5.0` for the host, native library, Common and other Rust dependencies, and `main` for `libssbm`.

From this repository:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -RoR2Dir "C:\path\to\Risk of Rain 2" -NoInstall
```

This builds `pikmin.dll` and `OlimarTest.dll` into `dist/OlimarTest/`. Omit `-NoInstall` to also stage the plugin and SharedFiles beneath a workspace `target/BepInEx/plugins/` directory; `-InstallDir` selects the staging root. Copy staged files into the intended game/profile yourself. The default compile path is the workspace's `2026 Mod Stuff/RiskOfRain2_DLC4` copy.

Example headless checks:

```powershell
dotnet run --project tests/OnionMenuCheck -c Release
dotnet run --project tests/SkinUnlockCheck -c Release
```

Run native checks from `libpikmin/` with `cargo test --release`. Some boundary checks need the built native library and local assets. Headless checks cover adapter and native API contracts. Manual checks remain for recent optional enemies/treasures, movement/VR changes, extraction/lobby behavior and co-op changes.

Further reading: [native behavior spec](https://github.com/wesjones15/libpikmin/blob/modern/1.5.0/docs/behavior-spec.md), [gameplay notes](../DOCS/2026-10-30-gameplay.md), [enemy integration](../DOCS/pikmin-enemies.md), and [shared multiplayer architecture](https://github.com/wesjones15/ror2-common/blob/modern/1.5.0/MULTIPLAYER.md). Current defaults are defined in `Plugin.cs`.
