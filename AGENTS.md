# CampaignHelper2 Agent Guide

## Project

CampaignHelper2 is a passive Path of Exile 2 campaign-guide plugin for GameHelper2. The repository contains plugin source, game-independent tests, and attributed immutable guide seed data.

## Host boundary

- Treat GameHelper2 as an external build dependency supplied via `GAMEHELPER2_HOST_ROOT` or `-p:GameHelperHostRoot=...`.
- Do not vendor GameHelper2 binaries or source.
- Preserve the GPL/provenance files under `CampaignHelper/Data/`.

## Verification

```bash
export GAMEHELPER2_HOST_ROOT=/path/to/GameHelper2
dotnet test CampaignHelper.Tests/CampaignHelper.Tests.csproj -c Release
dotnet build CampaignHelper/CampaignHelper.csproj -c Release -p:EnableWindowsTargeting=true
git diff --check
```

UI changes require a real GameHelper2/PoE2 comparison before being called complete.

## Safety

Keep the plugin passive: no game input, process control, process writes, injection, or packet manipulation. The bounded guide-data updater is its only network path.
