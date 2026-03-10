# Archipelago - Tomb Raider 1 Remastered

[Archipelago](https://archipelago.gg/) multiworld randomizer integration for **Tomb Raider I Remastered** (Patch 4.1).

> **Status**: Beta

## Quick Start

1. Download `TRArchipelagoClient.exe` and `tr1r.apworld` from the [latest release](../../releases/latest)
2. Install the APWorld: copy `tr1r.apworld` into your Archipelago `lib/worlds/` folder
3. Generate a multiworld with TR1 Remastered as one of the games
4. Launch Tomb Raider I-III Remastered
5. Run `TRArchipelagoClient.exe` and connect to your Archipelago server

### Features

- **351 randomized locations** across 15 levels (all pickups + key items)
- **45 secrets** as trackable locations
- **Real-time item injection** via process memory (no save file manipulation)
- **DeathLink** support
- **Traps**: damage, ammo drain, medipack drain
- **Multiple goal types**: Final Boss, All Secrets, N Levels completed
- **Configurable secrets mode**: excluded, useful, or progression-required

### Requirements

- Tomb Raider I-III Remastered (Patch 4.1) — Windows only
- [Archipelago](https://archipelago.gg/) server

## Building from Source

### Client

Requires .NET 8.0 SDK.

```
dotnet publish client/TRArchipelagoClient/TRArchipelagoClient.csproj -c Release -o publish
```

### APWorld

Zip the `apworld/tr1r/` folder as `tr1r.apworld` and place it in Archipelago's `lib/worlds/`.

## Project Structure

```
apworld/tr1r/       Python APWorld (items, locations, regions, rules, options)
client/              C# client that bridges the game and AP server
tools/               Data exporter — extracts game data into tr1r_data.json
docs/                Technical documentation (memory map, RE notes)
```

### Client (`client/TRArchipelagoClient/`)

Connects to the Archipelago server and communicates with the running game in real-time by reading/writing process memory (`tomb1.dll`). Handles:

- **Level patching**: replaces all pickups with sentinel items before gameplay
- **Pickup detection**: polls entity flags at 100ms intervals
- **Inventory injection**: writes directly to the game's inventory ring structures
- **Secret tracking**: monitors the secrets bitmask in the WorldStateBackup buffer
- **Save/load reconciliation**: detects save number changes to resync state after reloads

### Data Exporter (`tools/TRDataExporter/`)

Offline tool that extracts pickup locations, key item mappings, and secret data from TR1 level files using [TRLevelControl](https://github.com/LostArtefacts/TR-Rando). Outputs `tr1r_data.json` consumed by the APWorld. Only needs to be re-run if game data changes.

## License

[MIT](LICENSE)
