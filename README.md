# Archipelago - Tomb Raider 1 Remastered

[Archipelago](https://archipelago.gg/) multiworld randomizer integration for **Tomb Raider I Remastered** (Patch 4.1).

> **Status**: Work in progress

## What is this?

This project turns Tomb Raider 1 Remastered into an Archipelago game. All pickups (medipacks, ammo, weapons, key items) are shuffled into the multiworld item pool. When you pick up an item in TR1, it may be sent to another player's game, and you receive items from other players in real-time.

### Features

- **351 randomized locations** across 15 levels (all pickups + key items)
- **45 secrets** as trackable locations
- **Real-time item injection** via process memory (no save file manipulation)
- **DeathLink** support
- **Traps**: damage, ammo drain, medipack drain
- **Multiple goal types**: Final Boss, All Secrets, N Levels completed
- **Configurable secrets mode**: excluded, useful, or progression-required

## Architecture

```
apworld/tr1r/       Python APWorld for the Archipelago server
client/              C# client that bridges the game and AP server
data-exporter/       C# tool that extracts item/location data from TR1 level files
```

### APWorld (`apworld/tr1r/`)

Standard Archipelago world definition. Defines items, locations, regions, rules, and options. Installed into the Archipelago server.

### Client (`client/TRArchipelagoClient/`)

Connects to the Archipelago server and communicates with the running game in real-time by reading/writing process memory (`tomb1.dll`). Handles:

- **Level patching**: replaces all pickups with sentinel items before gameplay
- **Pickup detection**: polls entity flags at 100ms intervals to detect when items are collected
- **Inventory injection**: writes directly to the game's inventory ring structures to give items
- **Secret tracking**: monitors the secrets bitmask in the WorldStateBackup buffer
- **Save/load handling**: detects save number changes to resync state after reloads

### Data Exporter (`data-exporter/`)

Extracts pickup locations, key item mappings, secret data, and route information from TR1 level files using [TRLevelControl](https://github.com/LostArtefacts/TR-Rando). Outputs `tr1r_data.json` consumed by the APWorld.

## Requirements

- Tomb Raider I-III Remastered (Patch 4.1)
- [Archipelago](https://archipelago.gg/) server
- .NET 8.0 Runtime (for the client)

## License

[MIT](LICENSE)
