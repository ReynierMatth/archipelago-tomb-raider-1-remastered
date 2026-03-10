# TR-Archipelago Memory & Architecture Notes

## Memory Map Summary (TR1 Remastered, Patch 4.1)

All offsets relative to `tomb1.dll` base address unless stated otherwise.

### Inventory Ring System

The game uses two inventory rings, each with the same structure:
- **count** (Int16): number of items currently in the ring
- **items[]** (24 x Int64): pointers to INVENTORY_ITEM structs
- **qtys[]** (24 x Int16): quantity for each item

Most INVENTORY_ITEM structs are in a global sequential table (stride **0xCD0**):
```
target_ptr = pistols_ptr + relIdx * 0xCD0
```
Exception: Key4 (Thor Key) is dynamically allocated, uses a raw byte offset.

#### Main Ring (weapons, medipacks, compass)

| Field | Offset | Type |
|---|---|---|
| Count | 0xE2ABC | Int16 |
| Items[] | 0xF8D20 | 24 x Int64 |
| Qtys[] | 0xF8DD8 | 24 x Int16 |

Confirmed items (relIdx from Pistols):

| Item | RelIdx | InvObjId | Status |
|---|---|---|---|
| Shotgun Ammo | -8 | 0x68 | CONFIRMED |
| Small Medipack | -6 | 0x6C | CONFIRMED |
| Large Medipack | -2 | 0x6D | CONFIRMED |
| Pistols | 0 | 0x63 | CONFIRMED (reference) |
| Shotgun | +1 | 0x64 | CONFIRMED |
| Magnum Ammo | +4 | 0x69 | CONFIRMED |
| Compass | +6 | 0x48 | CONFIRMED |
| Uzis | +9 | 0x66 | CONFIRMED |
| Uzi Ammo | +10 | 0x6A | CONFIRMED |
| Magnums | +12 | 0x65 | CONFIRMED |

#### Keys Ring (key items, puzzles, scion)

| Field | Offset | Type | Status |
|---|---|---|---|
| Count | 0xFD6CC | Int16 | CONFIRMED |
| Items[] | 0xF95A0 | 24 x Int64 | CONFIRMED |
| Qtys[] | 0xF9660 | 24 x Int16 | CONFIRMED |

Key item relIdx values (all confirmed via Mode 6 + CE injection tests):

| Item | RelIdx | InvObjId | Notes |
|---|---|---|---|
| Key1 | -1 | 0x85 | Neptune/Silver/Gold/Rusty/Sapphire Key |
| Key2 | -7 | 0x86 | Atlas Key, Cistern Silver Key |
| Key3 | +7 | 0x87 | Damocles Key, Cistern Rusty Key |
| Key4 | **N/A** | 0x88 | Thor Key — byte offset -0x9E00 from Pistols |
| Puzzle1 | +11 | 0x72 | Gold Bar, Fuse, Ankh, Scarab |
| Puzzle2 | -4 | 0x73 | Seal of Anubis |
| Puzzle3 | +5 | 0x74 | |
| Puzzle4 | -5 | 0x75 | |
| Scion | -10 | 0x96 | Scion piece |

**Key4 special case:** Its INVENTORY_ITEM is dynamically allocated (not stride-aligned).
Address = `pistols_ptr + Key4ByteOffset` where `Key4ByteOffset = -0x9E00`.
Only used in St. Francis' Folly (Thor Key). Confirmed working via CE injection.

**Ring sorting:** The Keys Ring is sorted by inv_pos on insert (not appended).

### LARA_INFO Ammo (direct write, instant effect)

| Field | Offset | Type | Notes |
|---|---|---|---|
| Magnum ammo | 0x310FC8 | Int32 | Direct value |
| Uzi ammo | 0x310FD0 | Int32 | Direct value |
| Shotgun ammo | 0x310FD8 | Int32 | Internal = displayed * 6 |

### Injection Strategy

| Item Type | Method |
|---|---|
| Weapons | Inject into Main Ring + set WSB weapon flag + give starting ammo |
| Ammo (has weapon) | Direct write to LARA_INFO fields |
| Ammo (no weapon) | Inject ammo item into Main Ring (visible pickup) |
| Medipacks | Inject into Main Ring (qty increment if exists) |
| Key Items (Key1-3, Puzzles, Scion) | Inject into Keys Ring via relIdx |
| Key4 (Thor Key) | Inject into Keys Ring via raw byte offset |
| Traps | Direct write to Lara health / LARA_INFO ammo |
