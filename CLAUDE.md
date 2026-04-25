# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

EmoTracker is a C# / .NET 10 desktop application built with **Avalonia UI 11.3.3** for cross-platform desktop (Windows, macOS, Linux). It is an advanced item and location tracker for randomized games like A Link to the Past Randomizer, providing item tracking, interactive maps, auto-tracking via memory emulation, NDI broadcasting, Twitch chat HUD overlays, voice recognition, Markdown notes, and a Lua scripting console.

## Solution Structure

**Solution file:** `EmoTracker.sln` (4 projects)

| Project | Type | Role | Depends On |
|---------|------|------|------------|
| `EmoTracker` | WinExe | Application entry point, UI shell, extensions, services | Core, Data, UI |
| `EmoTracker.Core` | Library | Cross-cutting infrastructure: MVVM primitives, observables, commands, services | (none) |
| `EmoTracker.Data` | Library | Domain models: items, locations, game packages, auto-tracking, Lua scripting | Core |
| `EmoTracker.UI` | Library | Reusable Avalonia controls and value converters | Core, Data |

Dependency flow: `EmoTracker` -> `Core` <- `Data` -> `Core`, `EmoTracker.UI` -> `Core` + `Data`

## Architecture

- **No DI container.** Uses a custom singleton-based architecture (`ObservableSingleton<T>`) combined with MVVM.
- **Service Backend Pattern:** Abstractions like `Log` and `Dispatch` declare interfaces; concrete backends are registered at startup in `App.axaml.cs`.
- **Transaction System:** `TransactableObject` adds undo-able transactions. `TransactionProcessor` is a static setter-based registration; implementation is `LocalTransactionProcessorWithUndo`.
- **Extension System:** Runtime reflection scans for types implementing `Extension` interface. Built-in extensions: AutoTracker, NDI, Twitch, VoiceRecognition, NoteTaking, VariantSwitcher, McpServer (debug-only).
- **JSON Polymorphism:** Custom `JsonTypeTagsAttribute` + `TypeRegistry<T>` for polymorphic deserialization (items, layouts).

## Key Entry Points

- `EmoTracker/Program.cs` - Entry point, builds Avalonia app
- `EmoTracker/App.axaml.cs` - Framework initialization: registers service backends, creates settings, starts background services
- `EmoTracker/MainWindow.axaml` - Main window layout with `LayoutControl`

## Key Domains

- **Items** (`EmoTracker.Data.Items`): `ItemBase` hierarchy - ToggleItem, ConsumableItem, ProgressiveItem, CompositeToggleItem, etc.
- **Locations** (`EmoTracker.Data.Locations`): Location, Section, Map, Group, AccessibilityRule
- **Game Packages** (`EmoTracker.Data.Packages`): GamePackage, PackageManager, PackageRepository - community-created configs
- **Layout Engine** (`EmoTracker.Data.Layout`): Layout, LayoutItem, panels (Container, DockPanel, TabPanel, ArrayPanel, etc.)
- **Auto-Tracking** (`EmoTracker.Data.AutoTracking`): Provider/device model for memory emulation reading
- **Scripting** (`EmoTracker.Data.Scripting`): LuaItem, ScriptManager via NLua

## Development Commands

**Prerequisites:** .NET 10 SDK

```bash
# Restore packages
dotnet restore EmoTracker.sln

# Build entire solution (Debug, default)
dotnet build EmoTracker.sln

# Build Release
dotnet build EmoTracker.sln --configuration Release

# Run the app (Debug)
dotnet run --project EmoTracker/EmoTracker.csproj

# Run with MCP server enabled (dev mode)
dotnet run --project EmoTracker/EmoTracker.csproj -- -dev -localservice

# Publish self-contained for distribution
dotnet publish EmoTracker/EmoTracker.csproj --framework net10.0 --configuration Release --runtime win-x64 --self-contained --output publish/
# Other runtimes: linux-x64, osx-x64, osx-arm64

# Clean
dotnet clean EmoTracker.sln
```

**VS Code:** Tasks configured in `.vscode/tasks.json`. Default `Ctrl+Shift+B` builds the solution. Launch configs in `.vscode/launch.json` include Debug and Dev-Debug (-dev flag) profiles.

## CI/CD

- **Build** (`.github/workflows/build-avalonia.yml`): Cross-platform publish on push/PR to `main` and `avalonia` branches. Uses `EnableWindowsTargeting=true` for non-Windows runners. Bundles native Vosk libraries per-platform.
- **Release** (`.github/workflows/release.yml`): Triggered by `v*.*.*.*` git tags. Builds all 4 platforms, creates macOS `.app` bundle, packages archives, creates GitHub Release via `gh`.

## Build Configurations

MCP server code is excluded from non-Debug builds (`EmoTracker.csproj`).

## Important Notes

- **No unit tests exist** in this repository.
- Native libraries in `External/` (NDI, pre-compiled binaries) are committed in full - override the default `.gitignore` exclusion for `x64/` and `x86/`.
- Full documentation: [EmoTracker Wiki](https://github.com/EmoTracker-Community/EmoTracker/wiki)
