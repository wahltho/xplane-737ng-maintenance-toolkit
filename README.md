# LevelUp Nav Table Updater

Prototype for a VeloPack-based LevelUp 737NG VNAV descent table installer.

This repository is a temporary public development home for the prototype. It is
intended to transfer to LevelUp/Monsoon ownership once the final GitHub
organization/repository decision is settled.

No official releases, installer signing credentials or distribution secrets
should live here while the repository remains under a personal account.

This first build is intentionally read-only for real aircraft installations:

- It can detect likely LevelUp aircraft folders.
- It can scan a manually selected folder.
- It explicitly detects LevelUp port/no-Lua installs as not applicable to the
  Lua patch package.
- It parses the current v1 package manifest format.
- It classifies hook state in `B738.a_fms.lua`.
- It shows component status, findings and planned changes.
- Install, Update, Repair, Restore and Uninstall buttons only simulate actions
  and write to the UI log.

No aircraft files are changed by this prototype.

## Build

```bash
dotnet build LevelUp.NavTableUpdater.slnx
```

## Test

```bash
dotnet test tests/LevelUp.NavTableUpdater.Core.Tests/LevelUp.NavTableUpdater.Core.Tests.csproj
```

## Run

```bash
dotnet run --project src/LevelUp.NavTableUpdater.App/LevelUp.NavTableUpdater.App.csproj
```

The app requires a usable desktop GUI session.

## Current Scope

- .NET 10.
- Avalonia UI.
- VeloPack SDK startup hook via `VelopackApp.Build().Run()`.
- Manifest-driven package preview.
- Read-only aircraft detection and install-state analysis.
- No VeloPack packaging yet.
- No GitHub Release publishing yet.
- No real patch writes yet.

## VeloPack Integration

The app references the `Velopack` NuGet package and calls
`VelopackApp.Build().Run()` at the start of `Program.Main`, before Avalonia is
initialized. That is the required application-side hook for install/update
lifecycle handling.

Packaging and publishing are intentionally not active in this prototype yet.
The next VeloPack step is a release workflow around `dotnet publish` plus `vpk`
for beta/stable channels and signed platform artifacts.
