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

    // Received key items per level — kept permanently for idempotent re-injection
    // after death/reload. InjectToRingRaw handles duplicates safely.
    private readonly Dictionary<int, List<long>> _receivedKeyItems = new();

    // Once all key items have been successfully injected after a load,
    // stop reconciling until the next load — prevents re-giving used keys.
    private bool _keyItemsEnsured;

    // Pending sentinel medipack removals — queued by CheckEntityPickups,
    // processed every tick. Uses a counter because the game may not have
    // added the medipack to the ring yet when the entity flag changes.
    private int _pendingSentinelRemovals;

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
            byte weaponByte = _memory.ReadByte(weaponAddr);
            _memory.Write(weaponAddr, (byte)(weaponByte | weaponFlag));
        }

        // Convert any existing ammo items in the ring to LARA_INFO, then give starting ammo
        (int ammoRelIdx, int laraAmmoOffset, int ammoPerPickup) = type.Value switch
        {
            TR1Type.Shotgun_S_P => (TR1RMemoryMap.InvItemRelIndex.ShotgunAmmo,
                TR1RMemoryMap.Lara_ShotgunAmmo, 2 * TR1RMemoryMap.ShotgunAmmoMultiplier),
            TR1Type.Magnums_S_P => (TR1RMemoryMap.InvItemRelIndex.MagnumAmmo,
                TR1RMemoryMap.Lara_MagnumAmmo, 50),
            TR1Type.Uzis_S_P => (TR1RMemoryMap.InvItemRelIndex.UziAmmo,
                TR1RMemoryMap.Lara_UziAmmo, 100),
            _ => (int.MinValue, -1, 0),
        };

        if (laraAmmoOffset < 0) return;

        // Remove ammo item from ring if it exists, and convert its qty to LARA_INFO
        short ammoQty = RemoveFromRing(
            TR1RMemoryMap.MainRingCount, TR1RMemoryMap.MainRingItems,
            TR1RMemoryMap.MainRingQtys, ammoRelIdx);

        // Write to LARA_INFO: converted ring qty + starting ammo
        IntPtr ammoAddr = _memory.Tomb1Base + laraAmmoOffset;
        int current = _memory.ReadInt32(ammoAddr);
        int toAdd = (ammoQty * ammoPerPickup) + ammoPerPickup; // ring pickups + starting ammo
        int newVal = Math.Min(current + toAdd, 999999);
        _memory.Write(ammoAddr, newVal);
        ConsoleUI.Info($"[INV] Ammo: {current} -> {newVal} (converted {ammoQty} ring pickups + starting ammo)");
    }

    /// <summary>
    /// Gives ammo. If the player owns the weapon, writes directly to LARA_INFO
    /// ammo fields (instant). If not, injects the ammo item into the Main Ring
    /// so it appears as a visible pickup (e.g. "Magnum Clips").
    /// Shotgun ammo is stored internally as displayed * 6.
    /// </summary>
    public void GiveAmmo(long apItemId)
    {
        var type = ItemMapper.GetTR1Type(apItemId);
        if (type == null) return;

        // Map ammo type to its weapon relIdx and ammo relIdx
        (int weaponRelIdx, int ammoRelIdx, int laraInfoOffset, int amount) = type.Value switch
        {
            TR1Type.ShotgunAmmo_S_P => (TR1RMemoryMap.InvItemRelIndex.Shotgun, TR1RMemoryMap.InvItemRelIndex.ShotgunAmmo,
                TR1RMemoryMap.Lara_ShotgunAmmo, 2 * TR1RMemoryMap.ShotgunAmmoMultiplier),
            TR1Type.MagnumAmmo_S_P => (TR1RMemoryMap.InvItemRelIndex.Magnums, TR1RMemoryMap.InvItemRelIndex.MagnumAmmo,
                TR1RMemoryMap.Lara_MagnumAmmo, 50),
            TR1Type.UziAmmo_S_P => (TR1RMemoryMap.InvItemRelIndex.Uzis, TR1RMemoryMap.InvItemRelIndex.UziAmmo,
                TR1RMemoryMap.Lara_UziAmmo, 100),
            _ => (int.MinValue, int.MinValue, -1, 0),
        };

        if (laraInfoOffset < 0) return;

        // Check if the player has the weapon in the Main Ring
        bool hasWeapon = EnsurePistolsPointer() && HasItemInRing(
            TR1RMemoryMap.MainRingCount, TR1RMemoryMap.MainRingItems, weaponRelIdx);

        if (hasWeapon)
        {
            // Player has the weapon — add directly to LARA_INFO ammo counter
            IntPtr ammoAddr = _memory.Tomb1Base + laraInfoOffset;
            int current = _memory.ReadInt32(ammoAddr);
            int newVal = Math.Min(current + amount, 999999);
            _memory.Write(ammoAddr, newVal);
            ConsoleUI.Info($"[INV] Ammo: {current} -> {newVal} (LARA_INFO)");
        }
        else if (EnsurePistolsPointer())
        {
            // Player doesn't have the weapon — inject ammo item into Main Ring
            bool injected = InjectToRing(
                TR1RMemoryMap.MainRingCount,
                TR1RMemoryMap.MainRingItems,
                TR1RMemoryMap.MainRingQtys,
                ammoRelIdx, 1);

            if (injected)
                ConsoleUI.Info($"[INV] Ammo item injected into Main Ring (relIdx={ammoRelIdx})");
            else
                ConsoleUI.Warning($"[INV] Failed to inject ammo item (relIdx={ammoRelIdx})");
        }
    }

    /// <summary>
    /// Gives a medipack by injecting into the Main Ring.
    /// If the item is already in the ring, increments its qty.
    /// Caller (ProcessReceivedItems) ensures Pistols pointer is ready.
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

        // Always store for idempotent re-injection (death/reload/reconnect)
        if (!_receivedKeyItems.ContainsKey(targetMapperIdx))
            _receivedKeyItems[targetMapperIdx] = new();
        if (!_receivedKeyItems[targetMapperIdx].Contains(apItemId))
        {
            _receivedKeyItems[targetMapperIdx].Add(apItemId);
            _keyItemsEnsured = false; // new item — need to re-check ring
        }

        // Try immediate injection if we're on the right level
        if (targetMapperIdx == currentMapperIdx && currentMapperIdx >= 0)
            TryInjectKeyItem(apItemId);
    }

    /// <summary>
    /// Attempts to inject a key item into the Keys Ring.
    /// Called from GiveKeyItem (immediate) and from OnLevelChanged (deferred).
    /// Safe to call multiple times — InjectToRingRaw handles duplicates.
    /// </summary>
    private bool TryInjectKeyItem(long apItemId)
    {
        if (!EnsurePistolsPointer())
            return false;

        IntPtr targetPtr = ResolveKeyItemPointer(apItemId);
        if (targetPtr == IntPtr.Zero)
        {
            ConsoleUI.Warning($"[INV] Key item AP ID {apItemId}: unknown type, cannot inject.");
            return false;
        }

        bool injected = InjectToRingRaw(
            TR1RMemoryMap.KeysRingCount,
            TR1RMemoryMap.KeysRingItems,
            TR1RMemoryMap.KeysRingQtys,
            targetPtr, 1);

        if (injected)
            ConsoleUI.Info($"[INV] Key item injected into Keys Ring (ptr=0x{targetPtr:X})");
        return injected;
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
    /// Gets received key items for a specific level. Items are NOT removed —
    /// they are kept for idempotent re-injection after death/reload.
    /// InjectToRingRaw handles duplicates safely (increments qty if already present).
    /// </summary>
    public List<long> GetReceivedKeyItems(int locationMapperIndex)
    {
        if (_receivedKeyItems.TryGetValue(locationMapperIndex, out var items))
            return items;
        return new();
    }

    /// <summary>
    /// Ensures received key items are present in the Keys Ring.
    /// Called every poll tick but skips quickly once items have been injected.
    ///
    /// After successful injection, stops touching the ring — prevents re-giving
    /// keys the player has legitimately used (e.g. 1 of 2 Silver Keys).
    ///
    /// Reset triggers (event-driven, not polling):
    ///   - OnLevelChanged: new level → inject all items for that level
    ///   - Save_Number change: save/load detected → re-compare ring vs AP items
    ///   - New AP item received: GiveKeyItem → inject the new item
    /// </summary>
    public void EnsureKeyItemsInRing(int currentRuntimeLevelId)
    {
        if (_keyItemsEnsured) return;

        int mapperIdx = TR1RMemoryMap.ToLocationMapperIndex(currentRuntimeLevelId);
        if (mapperIdx < 0) return;

        var items = GetReceivedKeyItems(mapperIdx);
        if (items.Count == 0) return;
        if (!EnsurePistolsPointer()) return;

        // Count expected qty per pointer from received items
        var expectedQty = new Dictionary<IntPtr, short>();
        foreach (long apItemId in items)
        {
            IntPtr ptr = ResolveKeyItemPointer(apItemId);
            if (ptr == IntPtr.Zero) continue;
            expectedQty.TryGetValue(ptr, out short current);
            expectedQty[ptr] = (short)(current + 1);
        }

        // Inject missing items and fix qty on existing ones
        IntPtr t1 = _memory.Tomb1Base;
        short ringCount = _memory.ReadInt16(t1 + TR1RMemoryMap.KeysRingCount);

        foreach (var (targetPtr, targetQty) in expectedQty)
        {
            int ringIdx = -1;
            for (int i = 0; i < ringCount; i++)
            {
                if (_memory.ReadPointer(t1 + TR1RMemoryMap.KeysRingItems + i * 8) == targetPtr)
                { ringIdx = i; break; }
            }

            if (ringIdx >= 0)
            {
                short currentQty = _memory.ReadInt16(t1 + TR1RMemoryMap.KeysRingQtys + ringIdx * 2);
                if (currentQty < targetQty)
                    _memory.Write(t1 + TR1RMemoryMap.KeysRingQtys + ringIdx * 2, targetQty);
            }
            else if (ringCount < TR1RMemoryMap.MaxRingItems)
            {
                _memory.Write(t1 + TR1RMemoryMap.KeysRingItems + ringCount * 8, targetPtr.ToInt64());
                _memory.Write(t1 + TR1RMemoryMap.KeysRingQtys + ringCount * 2, targetQty);
                ringCount++;
                _memory.Write(t1 + TR1RMemoryMap.KeysRingCount, ringCount);
                ConsoleUI.Info($"[INV] Key item ensured in Keys Ring (ptr=0x{targetPtr:X}, qty={targetQty})");
            }
        }

        _keyItemsEnsured = true;
    }

    // =================================================================
    // RING INJECTION
    // =================================================================

    /// <summary>
    /// Injects an item into an inventory ring using a relIdx from Pistols.
    /// If the item already exists in the ring, increments its qty.
    /// Otherwise appends it at the end with the given qty.
    /// </summary>
    private bool InjectToRing(int ringCountOffset, int ringItemsOffset, int ringQtysOffset, int relIdx, short qty)
    {
        if (_pistolsPtr == IntPtr.Zero) return false;
        IntPtr targetPtr = _pistolsPtr + relIdx * TR1RMemoryMap.InventoryItemStride;
        return InjectToRingRaw(ringCountOffset, ringItemsOffset, ringQtysOffset, targetPtr, qty);
    }

    /// <summary>
    /// Injects an item into an inventory ring using a raw INVENTORY_ITEM pointer.
    /// Used for items not in the stride-aligned table (e.g. Key4/Thor Key).
    /// </summary>
    private bool InjectToRingRaw(int ringCountOffset, int ringItemsOffset, int ringQtysOffset, IntPtr targetPtr, short qty)
    {
        IntPtr t1 = _memory.Tomb1Base;
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

        _memory.Write(t1 + ringItemsOffset + ringCount * 8, targetPtr.ToInt64());
        _memory.Write(t1 + ringQtysOffset + ringCount * 2, qty);
        _memory.Write(t1 + ringCountOffset, (short)(ringCount + 1));
        return true;
    }

    /// <summary>
    /// Checks whether an item (by relIdx from Pistols) exists in a ring.
    /// </summary>
    private bool HasItemInRing(int ringCountOffset, int ringItemsOffset, int weaponRelIdx)
    {
        if (_pistolsPtr == IntPtr.Zero) return false;
        IntPtr t1 = _memory.Tomb1Base;
        IntPtr weaponPtr = _pistolsPtr + weaponRelIdx * TR1RMemoryMap.InventoryItemStride;
        short count = _memory.ReadInt16(t1 + ringCountOffset);
        for (int i = 0; i < count; i++)
        {
            if (_memory.ReadPointer(t1 + ringItemsOffset + i * 8) == weaponPtr)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Removes an item (by relIdx) from a ring. Shifts subsequent items down.
    /// Returns the qty the item had, or 0 if not found.
    /// </summary>
    private short RemoveFromRing(int ringCountOffset, int ringItemsOffset, int ringQtysOffset, int relIdx)
    {
        if (_pistolsPtr == IntPtr.Zero) return 0;
        IntPtr t1 = _memory.Tomb1Base;
        IntPtr targetPtr = _pistolsPtr + relIdx * TR1RMemoryMap.InventoryItemStride;
        short count = _memory.ReadInt16(t1 + ringCountOffset);

        // Find the item
        int foundIdx = -1;
        short foundQty = 0;
        for (int i = 0; i < count; i++)
        {
            if (_memory.ReadPointer(t1 + ringItemsOffset + i * 8) == targetPtr)
            {
                foundIdx = i;
                foundQty = _memory.ReadInt16(t1 + ringQtysOffset + i * 2);
                break;
            }
        }
        if (foundIdx < 0) return 0;

        // Shift subsequent items down
        for (int i = foundIdx; i < count - 1; i++)
        {
            long nextPtr = _memory.ReadInt64(t1 + ringItemsOffset + (i + 1) * 8);
            _memory.Write(t1 + ringItemsOffset + i * 8, nextPtr);
            short nextQty = _memory.ReadInt16(t1 + ringQtysOffset + (i + 1) * 2);
            _memory.Write(t1 + ringQtysOffset + i * 2, nextQty);
        }

        // Clear last slot and decrement count
        _memory.Write(t1 + ringItemsOffset + (count - 1) * 8, 0L);
        _memory.Write(t1 + ringQtysOffset + (count - 1) * 2, (short)0);
        _memory.Write(t1 + ringCountOffset, (short)(count - 1));
        return foundQty;
    }

    /// <summary>
    /// Ensures the Pistols pointer is cached. Tries to find it if not set.
    /// </summary>
    private bool EnsurePistolsPointer()
    {
        if (_pistolsPtr != IntPtr.Zero) return true;
        _pistolsPtr = FindPistolsPointer();
        return _pistolsPtr != IntPtr.Zero;
    }

    /// <summary>Invalidate cached pointer (call on level change).</summary>
    public void InvalidatePistolsPointer() => _pistolsPtr = IntPtr.Zero;

    /// <summary>
    /// Returns true if the inventory ring system is ready for injection
    /// (Pistols pointer found). Items that need ring injection should be
    /// deferred until this returns true.
    /// </summary>
    public bool IsInventoryReady() => EnsurePistolsPointer();

    /// <summary>
    /// Reset key item ensurance flag. Call on any game load (level change or same-level reload)
    /// so that EnsureKeyItemsInRing will re-inject items into the fresh ring.
    /// </summary>
    public void ResetKeyItemEnsurance() => _keyItemsEnsured = false;

    /// <summary>
    /// Queue removal of one parasitic small medipack. Called when the player
    /// picks up a sentinel entity (SmallMed_S_P) — the game natively adds a
    /// small medipack that we need to cancel.
    /// </summary>
    public void QueueSentinelRemoval() => _pendingSentinelRemovals++;

    /// <summary>
    /// Discard pending sentinel removals. Call on level change (ring resets).
    /// </summary>
    public void ResetSentinelRemovals() => _pendingSentinelRemovals = 0;

    /// <summary>
    /// Process pending sentinel medipack removals. Call every tick.
    /// Retries until the medipack is found in the ring (the game may not
    /// have added it yet on the same tick as the entity flag change).
    /// </summary>
    public void ProcessSentinelRemovals()
    {
        while (_pendingSentinelRemovals > 0 && RemoveOneSentinelMedipack())
            _pendingSentinelRemovals--;
    }

    /// <summary>
    /// Removes one small medipack from the Main Ring (decrements qty by 1,
    /// or removes the item entirely if qty reaches 0).
    /// </summary>
    private bool RemoveOneSentinelMedipack()
    {
        if (!EnsurePistolsPointer()) return false;

        IntPtr t1 = _memory.Tomb1Base;
        IntPtr targetPtr = _pistolsPtr + TR1RMemoryMap.InvItemRelIndex.SmallMedipack * TR1RMemoryMap.InventoryItemStride;
        short ringCount = _memory.ReadInt16(t1 + TR1RMemoryMap.MainRingCount);

        for (int i = 0; i < ringCount; i++)
        {
            if (_memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + i * 8) != targetPtr)
                continue;

            IntPtr qtyAddr = t1 + TR1RMemoryMap.MainRingQtys + i * 2;
            short qty = _memory.ReadInt16(qtyAddr);

            if (qty > 1)
            {
                _memory.Write(qtyAddr, (short)(qty - 1));
                ConsoleUI.Info($"[INV] Sentinel medipack removed (qty {qty} -> {qty - 1})");
                return true;
            }

            // qty == 1 → remove item from ring entirely (shift subsequent items)
            for (int j = i; j < ringCount - 1; j++)
            {
                long nextPtr = _memory.ReadInt64(t1 + TR1RMemoryMap.MainRingItems + (j + 1) * 8);
                _memory.Write(t1 + TR1RMemoryMap.MainRingItems + j * 8, nextPtr);
                short nextQty = _memory.ReadInt16(t1 + TR1RMemoryMap.MainRingQtys + (j + 1) * 2);
                _memory.Write(t1 + TR1RMemoryMap.MainRingQtys + j * 2, nextQty);
            }
            _memory.Write(t1 + TR1RMemoryMap.MainRingItems + (ringCount - 1) * 8, 0L);
            _memory.Write(t1 + TR1RMemoryMap.MainRingQtys + (ringCount - 1) * 2, (short)0);
            _memory.Write(t1 + TR1RMemoryMap.MainRingCount, (short)(ringCount - 1));
            ConsoleUI.Info("[INV] Sentinel medipack removed (item removed from ring)");
            return true;
        }

        return false; // small medipack not found in ring yet — retry next tick
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

        // No fallback — only return a verified pointer
        return IntPtr.Zero;
    }

    /// <summary>
    /// Resolves the INVENTORY_ITEM pointer for a key item AP ID.
    /// AP IDs use level-specific TR1Type aliases (e.g. Folly_K4_ThorKey = 14280),
    /// not generic types. We cast to TR1Type and parse the enum name to determine
    /// the generic slot (K1-K4, P1-P4, Scion, LeadBar).
    /// </summary>
    private IntPtr ResolveKeyItemPointer(long apItemId)
    {
        int enumValue = (int)(apItemId - 770_000);
        var tr1Type = (TR1Type)enumValue;
        string name = tr1Type.ToString();

        // If ToString() returns just a number, the enum value is undefined
        if (name == enumValue.ToString())
            return IntPtr.Zero;

        // Determine generic slot from the alias name pattern
        // K4 must be checked before K1 (to avoid "_K4_" matching "_K1" substring issue — not possible, but order is cleaner)
        int? relIdx = null;
        bool isKey4 = false;

        if (name.Contains("_K1_") || name.Contains("_K1"))
            relIdx = TR1RMemoryMap.InvItemRelIndex.Key1;
        else if (name.Contains("_K2_") || name.Contains("_K2"))
            relIdx = TR1RMemoryMap.InvItemRelIndex.Key2;
        else if (name.Contains("_K3_") || name.Contains("_K3"))
            relIdx = TR1RMemoryMap.InvItemRelIndex.Key3;
        else if (name.Contains("_K4_") || name.Contains("_K4"))
            isKey4 = true;
        else if (name.Contains("_P1_") || name.Contains("_P1") || name.Contains("_LeadBar"))
            relIdx = TR1RMemoryMap.InvItemRelIndex.Puzzle1;
        else if (name.Contains("_P2_") || name.Contains("_P2"))
            relIdx = TR1RMemoryMap.InvItemRelIndex.Puzzle2;
        else if (name.Contains("_P3_") || name.Contains("_P3"))
            relIdx = TR1RMemoryMap.InvItemRelIndex.Puzzle3;
        else if (name.Contains("_P4_") || name.Contains("_P4"))
            relIdx = TR1RMemoryMap.InvItemRelIndex.Puzzle4;
        else if (name.Contains("_Scion") || name.Contains("Scion"))
            relIdx = TR1RMemoryMap.InvItemRelIndex.Scion;

        if (relIdx.HasValue)
            return _pistolsPtr + relIdx.Value * TR1RMemoryMap.InventoryItemStride;

        if (isKey4)
            return _pistolsPtr + TR1RMemoryMap.Key4ByteOffset;

        return IntPtr.Zero;
    }

    private void HalveAmmoInt32(IntPtr addr)
    {
        int current = _memory.ReadInt32(addr);
        if (current > 0)
            _memory.Write(addr, current / 2);
    }
}
