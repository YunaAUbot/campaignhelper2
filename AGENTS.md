# CampaignHelper2 Agent Guide

## Project

CampaignHelper2 is a passive Path of Exile 2 campaign-guide plugin for GameHelper2. The repository contains plugin source, game-independent tests, and attributed immutable guide seed data.

## Host boundary

- Treat GameHelper2 as an external build dependency supplied via `GAMEHELPER2_HOST_ROOT` or `-p:GameHelperHostRoot=...`.
- Do not vendor GameHelper2 binaries or source.
- Preserve the GPL/provenance files under `Data/`.

## Verification

```bash
export GAMEHELPER2_HOST_ROOT=/path/to/GameHelper2
dotnet test test/CampaignHelper.Tests.csproj -c Release
dotnet build CampaignHelper.csproj -c Release -p:EnableWindowsTargeting=true
git diff --check
```

UI changes require a real GameHelper2/PoE2 comparison before being called complete.

## Safety

Keep the plugin passive: no game input, process control, process writes, injection, or packet manipulation. The bounded guide-data updater is its only network path.

## Git importer layout

Keep the production project and source at repository root. Every auxiliary project
and tool belongs under `test/` (tools under `test/tools/`). Preserve assembly identity
and exclude test/tools and nested obj/bin from all production SDK items.
Run `python3 test/check_import_layout.py` with `GAMEHELPER2_HOST_ROOT` and `DOTNET` set.
See [ROOT_LAYOUT.md](ROOT_LAYOUT.md) for the pinned importer contract and verification
commands that use existing host artifacts without rebuilding or modifying the host.
