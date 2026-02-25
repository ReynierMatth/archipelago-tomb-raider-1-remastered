using TRArchipelagoClient.Core;
using TRArchipelagoClient.UI;
using TRLevelControl.Model;

namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Injects received items into the game in real-time via process memory.
///
/// Injection strategy by item type:
///   - Weapons: Inject into Main Ring + set WSB weapon flag + give starting ammo
///   - Ammo: Write directly to LARA_INFO ammo fields (instant, no ring needed)
///   - Medipacks: Inject into Main Ring (qty increment if already present)
///   - Key items: Inject into Keys Ring (same mechanic, different ring offsets)
///   - Traps: Direct write to Lara's health / ammo fields
///
/// Both Main Ring and Keys Ring use the same structure:
///   count (Int16) + items[] (Int64 pointers) + qtys[] (Int16)
///   Items are INVENTORY_ITEM struct pointers computed as:
///     target_ptr = pistols_ptr + relIdx * 0xCD0
/// </summary>
public class InventoryManager
{
    private readonly ProcessMemory _memory;

    // Pending key items for levels not currently loaded
    private readonly Dictionary<int, List<long>> _pendingKeyItems = new();

    // Cached Pistols pointer (reference for all relIdx calculations)
    private IntPtr _pistolsPtr;

    /// <summary>Set by GameStateWatcher once the scanner finds the live inventory address.</summary>
    public InventoryScanner? Scanner { get; set; }

    public InventoryManager(ProcessMemory memory)
    {
        _memory = memory;
    }

    /// <summary>
    /// Address of the WorldStateBackup buffer (live inventory state).
    /// </summary>
    private IntPtr WorldStateAddr => _memory.Tomb1Base + TR1RMemoryMap.WorldStateBackup;

    /// <summary>
    /// Call on level change to refresh the Pistols pointer cache.
    /// </summary>
    public void RefreshPistolsPointer()
    {
        _pistolsPtr = FindPistolsPointer();
        if (_pistolsPtr != IntPtr.Zero)
            ConsoleUI.Info($"[INV] Pistols reference: 0x{_pistolsPtr:X}");
    }

    /// <summary>
    /// Gives a weapon to the player by injecting into the Main Ring
    /// and setting the WSB weapon flag for save persistence.
    /// </summary>
    public void GiveWeapon(long apItemId)
    {
        var type = ItemMapper.GetTR1Type(apItemId);
        if (type == null) return;

        int relIdx = type.Value switch
        {
            TR1Type.Shotgun_S_P => TR1RMemoryMap.InvItemRelIndex.Shotgun,
            TR1Type.Magnums_S_P => TR1RMemoryMap.InvItemRelIndex.Magnums,
            TR1Type.Uzis_S_P => TR1RMemoryMap.InvItemRelIndex.Uzis,
            _ => int.MinValue,
        };

        if (relIdx == int.MinValue) return;

        // Inject into Main Ring for immediate visibility
        if (EnsurePistolsPointer())
        {
            bool injected = InjectToRing(
                TR1RMemoryMap.MainRingCount,
                TR1RMemoryMap.MainRingItems,
                TR1RMemoryMap.MainRingQtys,
                relIdx, 1);

            if (injected)
                ConsoleUI.Info($"[INV] {type.Value} injected into Main Ring (relIdx={relIdx})");
        }

        // Also set WSB weapon flag for save persistence
        byte weaponFlag = type.Value switch
        {
            TR1Type.Shotgun_S_P => TR1RMemoryMap.Weapon_Shotgun,
            TR1Type.Magnums_S_P => TR1RMemoryMap.Weapon_Magnums,
            TR1Type.Uzis_S_P => TR1RMemoryMap.Weapon_Uzis,
            _ => (byte)0,
        };

        if (weaponFlag != 0)
        {
            IntPtr weaponAddr = WorldStateAddr + TR1RMemoryMap.Save_WeaponsConfig;
            byte current = _memory.ReadByte(weaponAddr);
            _memory.Write(weaponAddr, (byte)(current | weaponFlag));
        }

        // Give starting ammo for the weapon
        var ammoType = type.Value switch
        {
            TR1Type.Shotgun_S_P => TR1Type.ShotgunAmmo_S_P,
            TR1Type.Magnums_S_P => TR1Type.MagnumAmmo_S_P,
            TR1Type.Uzis_S_P => TR1Type.UziAmmo_S_P,
            _ => (TR1Type?)null,
        };

        if (ammoType != null)
            GiveAmmo(ItemMapper.GetApId(ammoType.Value));
    }

    /// <summary>
    /// Gives ammo by writing directly to LARA_INFO ammo fields (live, instant).
    /// These are Int32 fields at fixed offsets in tomb1.dll.
    /// Shotgun ammo is stored internally as displayed * 6.
    /// </summary>
    public void GiveAmmo(long apItemId)
    {
        var type = ItemMapper.GetTR1Type(apItemId);
        if (type == null) return;

        (int laraInfoOffset, int amount) = type.Value switch
        {
            TR1Type.ShotgunAmmo_S_P => (TR1RMemoryMap.Lara_ShotgunAmmo, 2 * TR1RMemoryMap.ShotgunAmmoMultiplier),
            TR1Type.MagnumAmmo_S_P => (TR1RMemoryMap.Lara_MagnumAmmo, 50),
            TR1Type.UziAmmo_S_P => (TR1RMemoryMap.Lara_UziAmmo, 100),
            _ => (-1, 0),
        };

        if (laraInfoOffset < 0) return;

        IntPtr ammoAddr = _memory.Tomb1Base + laraInfoOffset;
        int current = _memory.ReadInt32(ammoAddr);
        int newVal = Math.Min(current + amount, 999999);
        _memory.Write(ammoAddr, newVal);
        ConsoleUI.Info($"[INV] Ammo: {current} -> {newVal} at tomb1.dll+0x{laraInfoOffset:X}");
    }

    /// <summary>
    /// Gives a medipack by injecting into the Main Ring.
    /// If the item is already in the ring, increments its qty.
    /// Falls back to WSB if Pistols pointer is unavailable.
    /// </summary>
    public void GiveMedipack(long apItemId)
    {
        var type = ItemMapper.GetTR1Type(apItemId);
        if (type == null) return;

        int relIdx = type.Value switch
        {
            TR1Type.SmallMed_S_P => TR1RMemoryMap.InvItemRelIndex.SmallMedipack,
            TR1Type.LargeMed_S_P => TR1RMemoryMap.InvItemRelIndex.LargeMedipack,
            _ => int.MinValue,
        };

        if (relIdx == int.MinValue) return;

        // Try Main Ring injection first
        if (EnsurePistolsPointer())
        {
            bool injected = InjectToRing(
                TR1RMemoryMap.MainRingCount,
                TR1RMemoryMap.MainRingItems,
                TR1RMemoryMap.MainRingQtys,
                relIdx, 1);

            if (injected)
            {
                ConsoleUI.Info($"[INV] {type.Value} injected into Main Ring (relIdx={relIdx})");
                return;
            }
        }

        // Fallback: WSB (may not take effect until save/load)
        int offset = type.Value switch
        {
            TR1Type.SmallMed_S_P => TR1RMemoryMap.Save_SmallMedipacks,
            TR1Type.LargeMed_S_P => TR1RMemoryMap.Save_LargeMedipacks,
            _ => -1,
        };

        if (offset < 0) return;

        IntPtr medAddr = WorldStateAddr + offset;
        byte current = _memory.ReadByte(medAddr);
        if (current < 255)
        {
            _memory.Write(medAddr, (byte)(current + 1));
            ConsoleUI.Info($"[INV] Medipack: {current} -> {current + 1} at WSB+0x{offset:X} (fallback)");
        }
    }

    /// <summary>
    /// Handles a key item. If the target level is currently active, injects
    /// into the Keys Ring. Otherwise, stores for later when that level is loaded.
    /// </summary>
    public void GiveKeyItem(long apItemId, int currentRuntimeLevelId)
    {
        string? targetLevelFile = ItemMapper.GetKeyItemLevel(apItemId);
        if (targetLevelFile == null) return;

        int targetMapperIdx = LocationMapper.GetLevelIndex(targetLevelFile);
        int currentMapperIdx = TR1RMemoryMap.ToLocationMapperIndex(currentRuntimeLevelId);

        if (targetMapperIdx == currentMapperIdx && currentMapperIdx >= 0)
        {
            // Resolve the key item's relIdx from the AP item ID
            int relIdx = ResolveKeyItemRelIdx(apItemId);
            if (relIdx == int.MinValue)
            {
                ConsoleUI.Warning($"[INV] Key item AP ID {apItemId}: unknown relIdx, cannot inject.");
                return;
            }

            if (EnsurePistolsPointer())
            {
                bool injected = InjectToRing(
                    TR1RMemoryMap.KeysRingCount,
                    TR1RMemoryMap.KeysRingItems,
                    TR1RMemoryMap.KeysRingQtys,
                    relIdx, 1);

                if (injected)
                {
                    ConsoleUI.Info($"[INV] Key item injected into Keys Ring (relIdx={relIdx})");
                    return;
                }
            }

            ConsoleUI.Warning($"[INV] Failed to inject key item (relIdx={relIdx}). Pistols ptr not found?");
        }
        else
        {
            // Store for pre-patching when the target level is loaded
            if (!_pendingKeyItems.ContainsKey(targetMapperIdx))
                _pendingKeyItems[targetMapperIdx] = new();
            _pendingKeyItems[targetMapperIdx].Add(apItemId);
        }
    }

    /// <summary>
    /// Applies a trap effect to the player in real-time.
    /// Damage trap writes directly to Lara's health via the pointer chain.
    /// Ammo/med drains write to LARA_INFO / WSB.
    /// </summary>
    public void ApplyTrap(long apItemId)
    {
        int trapType = (int)(apItemId - 769_000);

        switch (trapType)
        {
            case 1: // Damage Trap — reduce health by 25%
                IntPtr laraPtr = _memory.ReadPointer(_memory.Tomb1Base, TR1RMemoryMap.LaraBase);
                if (laraPtr != IntPtr.Zero)
                {
                    IntPtr healthAddr = laraPtr + TR1RMemoryMap.Item_HitPoints;
                    short health = _memory.ReadInt16(healthAddr);
                    short damage = (short)(health / 4);
                    short newHealth = (short)Math.Max(health - damage, TR1RMemoryMap.MinHealth);
                    _memory.Write(healthAddr, newHealth);
                    ConsoleUI.Warning($"TRAP! Took {damage} damage ({newHealth} HP remaining)");
                }
                break;

            case 2: // Ammo Drain — halve all ammo (live LARA_INFO)
                HalveAmmoInt32(_memory.Tomb1Base + TR1RMemoryMap.Lara_MagnumAmmo);
                HalveAmmoInt32(_memory.Tomb1Base + TR1RMemoryMap.Lara_UziAmmo);
                HalveAmmoInt32(_memory.Tomb1Base + TR1RMemoryMap.Lara_ShotgunAmmo);
                ConsoleUI.Warning("TRAP! All ammo halved!");
                break;

            case 3: // Medipack Drain — lose 1 small medipack
                IntPtr smallMedAddr = WorldStateAddr + TR1RMemoryMap.Save_SmallMedipacks;
                byte meds = _memory.ReadByte(smallMedAddr);
                if (meds > 0)
                    _memory.Write(smallMedAddr, (byte)(meds - 1));
                ConsoleUI.Warning("TRAP! Lost a small medipack!");
                break;
        }
    }

    /// <summary>
    /// Gets pending key items for a specific level (for pre-patching).
    /// </summary>
    public List<long> GetPendingKeyItems(int locationMapperIndex)
    {
        if (_pendingKeyItems.TryGetValue(locationMapperIndex, out var items))
        {
            _pendingKeyItems.Remove(locationMapperIndex);
            return items;
        }
        return new();
    }

    // =================================================================
    // RING INJECTION
    // =================================================================

    /// <summary>
    /// Injects an item into an inventory ring (Main or Keys).
    /// If the item already exists in the ring, increments its qty.
    /// Otherwise appends it at the end with the given qty.
    /// </summary>
    /// <returns>True if injection succeeded.</returns>
    private bool InjectToRing(int ringCountOffset, int ringItemsOffset, int ringQtysOffset, int relIdx, short qty)
    {
        if (_pistolsPtr == IntPtr.Zero) return false;

        IntPtr t1 = _memory.Tomb1Base;
        IntPtr targetPtr = _pistolsPtr + relIdx * TR1RMemoryMap.InventoryItemStride;
        short ringCount = _memory.ReadInt16(t1 + ringCountOffset);

        // Check if item already exists in ring
        for (int i = 0; i < ringCount; i++)
        {
            IntPtr existingPtr = _memory.ReadPointer(t1 + ringItemsOffset + i * 8);
            if (existingPtr == targetPtr)
            {
                // Increment qty
                IntPtr qtyAddr = t1 + ringQtysOffset + i * 2;
                short currentQty = _memory.ReadInt16(qtyAddr);
                short newQty = (short)Math.Min(currentQty + qty, 255);
                _memory.Write(qtyAddr, newQty);
                return true;
            }
        }

        // Append new item
        if (ringCount >= TR1RMemoryMap.MaxRingItems)
            return false;

        // Write item pointer
        _memory.Write(t1 + ringItemsOffset + ringCount * 8, targetPtr.ToInt64());
        // Write qty
        _memory.Write(t1 + ringQtysOffset + ringCount * 2, qty);
        // Increment count
        _memory.Write(t1 + ringCountOffset, (short)(ringCount + 1));

        return true;
    }

    /// <summary>
    /// Ensures the Pistols pointer is cached. Tries to find it if not set.
    /// </summary>
    private bool EnsurePistolsPointer()
    {
        if (_pistolsPtr != IntPtr.Zero) return true;
        RefreshPistolsPointer();
        return _pistolsPtr != IntPtr.Zero;
    }

    /// <summary>
    /// Finds the Pistols INVENTORY_ITEM pointer by cross-referencing Main Ring items.
    /// Compass (items[0]) should be at Pistols + 6 * stride.
    /// </summary>
    private IntPtr FindPistolsPointer()
    {
        IntPtr t1 = _memory.Tomb1Base;
        short ringCount = _memory.ReadInt16(t1 + TR1RMemoryMap.MainRingCount);
        if (ringCount < 2) return IntPtr.Zero;

        IntPtr item0 = _memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems);

        for (int i = 1; i < ringCount; i++)
        {
            IntPtr itemPtr = _memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + i * 8);
            if (itemPtr == IntPtr.Zero) continue;

            long diff = item0.ToInt64() - itemPtr.ToInt64();
            if (diff == TR1RMemoryMap.InvItemRelIndex.Compass * TR1RMemoryMap.InventoryItemStride)
                return itemPtr;
        }

        // Fallback: items[1] is almost always Pistols
        return _memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + 1 * 8);
    }

    /// <summary>
    /// Resolves the INVENTORY_ITEM relIdx for a key item AP ID.
    /// Key items use generic slots (Key1-4, Puzzle1-4) shared across levels.
    /// The TR1Type enum value determines which slot.
    ///
    /// NOTE: relIdx values for key items must be discovered via Mode 6 table scanner
    /// and added here. Currently returns int.MinValue for unmapped items.
    /// </summary>
    private static int ResolveKeyItemRelIdx(long apItemId)
    {
        // Key item AP IDs encode: BaseId + (levelBase + typeOffset)
        // The typeOffset within a level determines Key1/Key2/Puzzle1/etc.
        // We need to map from the TR1Type alias to the generic inventory item relIdx.
        //
        // TODO: Fill these in after running Mode 6 table scanner.
        // The relIdx values below are placeholders — replace with actual values
        // discovered by scanning the INVENTORY_ITEM table.
        //
        // Expected mapping (to be confirmed):
        //   Key1    -> relIdx TBD
        //   Key2    -> relIdx TBD
        //   Key3    -> relIdx TBD
        //   Key4    -> relIdx TBD
        //   Puzzle1 -> relIdx TBD
        //   Puzzle2 -> relIdx TBD
        //   Puzzle3 -> relIdx TBD
        //   Puzzle4 -> relIdx TBD

        return int.MinValue;
    }

    private void HalveAmmoInt32(IntPtr addr)
    {
        int current = _memory.ReadInt32(addr);
        if (current > 0)
            _memory.Write(addr, current / 2);
    }
}
