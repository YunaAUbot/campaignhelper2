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

dotnet test CampaignHelper.Tests/CampaignHelper.Tests.csproj -c Release
dotnet build CampaignHelper/CampaignHelper.csproj -c Release -p:EnableWindowsTargeting=true
```

The real in-game comparison remains the final acceptance gate for UI changes.

## License

GPL-3.0-or-later. See `LICENSE` and `CampaignHelper/Data/PROVENANCE.md`.
