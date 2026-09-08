# CampaignHelper2

Passive campaign guide plugin for [GameHelper2](https://github.com/Gordin/GameHelper2) and Path of Exile 2.

## Features

- Numbered campaign tasks with goals, optional steps, and hierarchical tips.
- Local progress tracking and bounded guide-data updates.
- Bundled guide seed with documented Exile-UI provenance.
- No game input, process writes, injection, or packet manipulation.

## Build

Requires the .NET 10 SDK and a GameHelper2 checkout.

```bash
export GAMEHELPER2_HOST_ROOT=/path/to/GameHelper2

dotnet test test/CampaignHelper.Tests.csproj -c Release
dotnet build CampaignHelper.csproj -c Release -p:EnableWindowsTargeting=true
```

The real in-game comparison remains the final acceptance gate for UI changes.

## License

GPL-3.0-or-later. See `LICENSE` and `Data/PROVENANCE.md`.

## Repository layout verification

Production source and the plugin project live at repository root; tests and any
auxiliary tools belong under `test/`. See [ROOT_LAYOUT.md](ROOT_LAYOUT.md) for the
verified Git importer contract, test commands, and builds against an already-built
GameHelper2 host without modifying it.
