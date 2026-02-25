# TR-Archipelago Memory & Architecture Notes

## Memory Map Summary (TR1 Remastered, Patch 4.1)

All offsets relative to `tomb1.dll` base address unless stated otherwise.

### Inventory Ring System

The game uses two inventory rings, each with the same structure:
- **count** (Int16): number of items currently in the ring
- **items[]** (24 x Int64): pointers to INVENTORY_ITEM structs
- **qtys[]** (24 x Int16): quantity for each item

All INVENTORY_ITEM structs are stored in a global sequential table in tomb1.dll
with stride **0xCD0** (3280 bytes). Any item can be addressed as:
```
target_ptr = pistols_ptr + relIdx * 0xCD0
```

#### Main Ring (weapons, medipacks, compass)

| Field | Offset | Type |
|---|---|---|
| Count | 0xE2ABC | Int16 |
| Items[] | 0xF8D20 | 24 x Int64 |
| Qtys[] | 0xF8DD8 | 24 x Int16 |

Confirmed items (relIdx from Pistols):

| Item | RelIdx | ObjId | Status |
|---|---|---|---|
| Small Medipack | -6 | 0x6C | CONFIRMED |
| Large Medipack | -2 | 0x6D | CONFIRMED |
| Pistols | 0 | - | CONFIRMED (reference) |
| Shotgun | +1 | - | CONFIRMED |
| Compass | +6 | - | CONFIRMED |
| Uzis | +9 | - | CONFIRMED |
| Magnums | +12 | - | CONFIRMED |

#### Keys Ring (key items, puzzles)

| Field | Offset | Type | Status |
|---|---|---|---|
| Count | 0xFD6CC | Int16 | Known |
| Items[] | 0xF95A0 | 24 x Int64 | Known |
| Qtys[] | 0xF9660 | 24 x Int16 | ESTIMATED (needs confirmation) |

Key item relIdx values: **TBD** - use MemoryTest Mode 6 table scanner to discover.

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
| Ammo | Direct write to LARA_INFO fields |
| Medipacks | Inject into Main Ring (qty increment if exists) |
| Key Items | Inject into Keys Ring |
| Traps | Direct write to Lara health / LARA_INFO ammo |

## Next Steps

1. Run Mode 6 table scanner to discover key item relIdx values
2. Confirm Keys Ring qtys[] offset (0xF9660 is estimated)
3. Test key item injection (Silver Key at Vilcabamba)
4. Fill in ResolveKeyItemRelIdx() in InventoryManager.cs
