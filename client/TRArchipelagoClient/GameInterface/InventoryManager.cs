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
///     target_ptr = compass_ptr + relIdx * 0xCD0
/// </summary>
public class InventoryManager
{
    private readonly ProcessMemory _memory;
    private readonly ItemMapper _itemMapper;
    private readonly LocationMapper _locationMapper;
    private GameContext? _context;
    private SlotData? _slotData;

    // Received key items per level — kept permanently for idempotent re-injection
    // after death/reload. InjectToRingRaw handles duplicates safely.
    private readonly Dictionary<int, List<long>> _receivedKeyItems = new();

    // Once all key items have been successfully injected after a load,
    // stop reconciling until the next load — prevents re-giving used keys.
    private bool _keyItemsEnsured;
    private bool _loggedCompassRetry;

    // After injection, keep re-checking for this many ticks to make sure the
    // game engine doesn't overwrite the ring. Only set _keyItemsEnsured once
    // the item survives in the ring for the full cooldown.
    private int _keyItemEnsureCooldown;
    private const int KeyItemEnsureCooldownTicks = 30; // 3 seconds

    // Key items that have been used (consumed in a door/lock): apItemId → count used.
    // EnsureKeyItemsInRing skips these to avoid re-injecting keys the player has already spent.
    private Dictionary<long, int> _usedKeyItems = new();

    // Pending sentinel medipack removals — queued by CheckEntityPickups,
    // processed every tick. Uses a counter because the game may not have
    // added the medipack to the ring yet when the entity flag changes.
    private int _pendingSentinelRemovals;

    // Cached Compass pointer (reference for all relIdx calculations)
    private IntPtr _compassPtr;

    /// <summary>Set by GameStateWatcher once the scanner finds the live inventory address.</summary>
    public InventoryScanner? Scanner { get; set; }

    public InventoryManager(ProcessMemory memory, ItemMapper itemMapper, LocationMapper locationMapper)
    {
        _memory = memory;
        _itemMapper = itemMapper;
        _locationMapper = locationMapper;
    }

    /// <summary>Set by GameStateWatcher after construction.</summary>
    public void SetGameContext(GameContext context) => _context = context;

    /// <summary>Set by GameStateWatcher once slot data is available.</summary>
    public void SetSlotData(SlotData slotData) => _slotData = slotData;

    /// <summary>
    /// Address of the WorldStateBackup buffer (live inventory state).
    /// </summary>
    private IntPtr WorldStateAddr => _context!.DllBase + _context.Map.WorldStateBackup;

    /// <summary>
    /// Call on level change to refresh the Compass pointer cache.
    /// </summary>
    public void RefreshCompassPointer()
    {
        _compassPtr = FindCompassPointer();
        if (_compassPtr != IntPtr.Zero)
            ConsoleUI.Info($"[INV] Compass reference: 0x{_compassPtr:X}");
    }

    /// <summary>
    /// Gives a weapon to the player by injecting into the Main Ring
    /// and setting the WSB weapon flag for save persistence.
    /// Uses game-specific WeaponRecipe from IGameMemoryMap.
    /// </summary>
    public void GiveWeapon(long apItemId)
    {
        var map = _context!.Map;
        var recipe = map.GetWeaponRecipe(apItemId, _itemMapper.Config.ItemBaseId);
        if (recipe == null) return;

        // Inject weapon into Main Ring
        if (EnsureCompassPointer())
        {
            IntPtr weaponPtr = map.ResolveInventoryItemPointer(_compassPtr, recipe.WeaponObjId);
            if (weaponPtr != IntPtr.Zero)
            {
                bool injected = InjectToRingRaw(
                    _context.Map.MainRingCount, _context.Map.MainRingItems,
                    _context.Map.MainRingQtys, weaponPtr, 1);
                if (injected)
                    ConsoleUI.Info($"[INV] {recipe.Name} injected into Main Ring");
            }
        }

        // Set WSB weapon flag for save persistence
        if (recipe.WeaponFlag != 0)
        {
            int weaponConfigOffset = _context.GameVersion == 0
                ? TR1RMemoryMap.Save_WeaponsConfig : TR2RMemoryMap.Save_WeaponsConfig_Base;
            IntPtr weaponAddr = WorldStateAddr + weaponConfigOffset;
            byte weaponByte = _memory.ReadByte(weaponAddr);
            _memory.Write(weaponAddr, (byte)(weaponByte | recipe.WeaponFlag));
        }

        // Remove ammo items from ring (if any) and convert to LARA_INFO, plus starting ammo
        IntPtr ammoPtr = map.ResolveInventoryItemPointer(_compassPtr, recipe.AmmoObjId);
        if (ammoPtr != IntPtr.Zero)
        {
            short ammoQty = RemoveFromRingByPtr(
                _context.Map.MainRingCount, _context.Map.MainRingItems,
                _context.Map.MainRingQtys, ammoPtr);

            if (recipe.LaraAmmoOffset >= 0)
            {
                IntPtr ammoAddr = _context.DllBase + recipe.LaraAmmoOffset;
                int current = _memory.ReadInt32(ammoAddr);
                int toAdd = (ammoQty * recipe.StartingAmmo) + recipe.StartingAmmo;
                int newVal = Math.Min(current + toAdd, 999999);
                _memory.Write(ammoAddr, newVal);
                ConsoleUI.Info($"[INV] Ammo: {current} -> {newVal} (converted {ammoQty} ring pickups + starting ammo)");
            }
        }
    }

    /// <summary>
    /// Gives ammo. If the player owns the weapon, writes directly to LARA_INFO
    /// ammo fields (instant). If not, injects the ammo item into the Main Ring.
    /// Uses game-specific AmmoRecipe from IGameMemoryMap.
    /// </summary>
    public void GiveAmmo(long apItemId)
    {
        var map = _context!.Map;
        var recipe = map.GetAmmoRecipe(apItemId, _itemMapper.Config.ItemBaseId);
        if (recipe == null) return;

        // Check if the player has the weapon in the Main Ring
        bool hasWeapon = false;
        if (EnsureCompassPointer())
        {
            IntPtr weaponPtr = map.ResolveInventoryItemPointer(_compassPtr, recipe.WeaponObjId);
            if (weaponPtr != IntPtr.Zero)
                hasWeapon = HasItemInRingByPtr(_context.Map.MainRingCount, _context.Map.MainRingItems, weaponPtr);
        }

        if (hasWeapon && recipe.LaraAmmoOffset >= 0)
        {
            // Player has the weapon — add directly to LARA_INFO ammo counter
            IntPtr ammoAddr = _context.DllBase + recipe.LaraAmmoOffset;
            int current = _memory.ReadInt32(ammoAddr);
            int newVal = Math.Min(current + recipe.Amount, 999999);
            _memory.Write(ammoAddr, newVal);
            ConsoleUI.Info($"[INV] {recipe.Name}: {current} -> {newVal} (LARA_INFO)");
        }
        else if (EnsureCompassPointer())
        {
            // Player doesn't have the weapon (or no LARA_INFO offset) — inject into Main Ring
            IntPtr ammoPtr = map.ResolveInventoryItemPointer(_compassPtr, recipe.AmmoObjId);
            if (ammoPtr != IntPtr.Zero)
            {
                bool injected = InjectToRingRaw(
                    _context.Map.MainRingCount, _context.Map.MainRingItems,
                    _context.Map.MainRingQtys, ammoPtr, 1);
                if (injected)
                    ConsoleUI.Info($"[INV] {recipe.Name} injected into Main Ring");
                else
                    ConsoleUI.Warning($"[INV] Failed to inject {recipe.Name}");
            }
        }
    }

    /// <summary>
    /// Gives a medipack (or flares in TR2) by injecting into the Main Ring.
    /// Uses game-specific medipack ObjId from IGameMemoryMap.
    /// </summary>
    public void GiveMedipack(long apItemId)
    {
        var map = _context!.Map;
        int objId = map.GetMedipackObjId(apItemId, _itemMapper.Config.ItemBaseId);
        if (objId < 0) return;

        if (EnsureCompassPointer())
        {
            IntPtr itemPtr = map.ResolveInventoryItemPointer(_compassPtr, objId);
            if (itemPtr != IntPtr.Zero)
            {
                bool injected = InjectToRingRaw(
                    _context.Map.MainRingCount, _context.Map.MainRingItems,
                    _context.Map.MainRingQtys, itemPtr, 1);
                if (injected)
                {
                    string name = map.InvObjIdNames.GetValueOrDefault(objId, $"Item 0x{objId:X}");
                    ConsoleUI.Info($"[INV] {name} injected into Main Ring");
                }
            }
        }
    }

    /// <summary>
    /// Handles a key item. If the target level is currently active, injects
    /// into the Keys Ring. Otherwise, stores for later when that level is loaded.
    /// </summary>
    public void GiveKeyItem(long apItemId, int currentRuntimeLevelId)
    {
        string? targetLevelFile = _itemMapper.GetKeyItemLevel(apItemId);
        if (targetLevelFile == null) return;

        int targetMapperIdx = _locationMapper.GetLevelIndex(targetLevelFile);
        int currentMapperIdx = _context!.Map.ToLocationMapperIndex(currentRuntimeLevelId);

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
        if (!EnsureCompassPointer())
            return false;

        IntPtr targetPtr = ResolveKeyItemPointer(apItemId);
        if (targetPtr == IntPtr.Zero)
        {
            ConsoleUI.Warning($"[INV] Key item AP ID {apItemId}: unknown type, cannot inject.");
            return false;
        }

        bool injected = InjectToRingRaw(
            _context.Map.KeysRingCount,
            _context.Map.KeysRingItems,
            _context.Map.KeysRingQtys,
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
        var map = _context!.Map;
        var recipe = map.GetTrapRecipe(apItemId, _itemMapper.Config.TrapBaseId);
        if (recipe == null) return;

        switch (recipe.Type)
        {
            case TrapType.Damage:
                short health = map.ReadHealth(_memory, _context.DllBase);
                short damage = (short)(health / 4);
                short newHealth = (short)Math.Max(health - damage, map.MinHealth);
                map.WriteHealth(_memory, _context.DllBase, newHealth);
                ConsoleUI.Warning($"TRAP! Took {damage} damage ({newHealth} HP remaining)");
                break;

            case TrapType.AmmoDrain:
                // Ammo drain only works when LARA_INFO ammo offsets are known (TR1 for now)
                if (_context.GameVersion == 0)
                {
                    HalveAmmoInt32(_context.DllBase + TR1RMemoryMap.Lara_MagnumAmmo);
                    HalveAmmoInt32(_context.DllBase + TR1RMemoryMap.Lara_UziAmmo);
                    HalveAmmoInt32(_context.DllBase + TR1RMemoryMap.Lara_ShotgunAmmo);
                }
                ConsoleUI.Warning("TRAP! All ammo halved!");
                break;

            case TrapType.SmallDrain:
                // Remove 1 small medipack from the ring directly
                if (EnsureCompassPointer())
                {
                    IntPtr smallMedPtr = map.ResolveInventoryItemPointer(_compassPtr,
                        _context.GameVersion == 0 ? TR1RMemoryMap.InvObjId.SmallMedipack
                                                   : TR2RMemoryMap.InvObjId.SmallMedipack);
                    if (smallMedPtr != IntPtr.Zero)
                    {
                        short count = _memory.ReadInt16(_context.DllBase + map.MainRingCount);
                        for (int i = 0; i < count; i++)
                        {
                            if (_memory.ReadPointer(_context.DllBase + map.MainRingItems + i * 8) == smallMedPtr)
                            {
                                short qty = _memory.ReadInt16(_context.DllBase + map.MainRingQtys + i * 2);
                                if (qty > 0)
                                    _memory.Write(_context.DllBase + map.MainRingQtys + i * 2, (short)(qty - 1));
                                break;
                            }
                        }
                    }
                }
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

        int mapperIdx = _context!.Map.ToLocationMapperIndex(currentRuntimeLevelId);
        if (mapperIdx < 0) return;

        var items = GetReceivedKeyItems(mapperIdx);
        if (items.Count == 0) return;
        if (!EnsureCompassPointer()) return;

        // Count expected qty per pointer from received items (skip used copies only)
        var remainingUsed = new Dictionary<long, int>(_usedKeyItems);
        var expectedQty = new Dictionary<IntPtr, short>();
        foreach (long apItemId in items)
        {
            if (remainingUsed.TryGetValue(apItemId, out int usedCount) && usedCount > 0)
            {
                remainingUsed[apItemId] = usedCount - 1;
                continue;
            }

            IntPtr ptr = ResolveKeyItemPointer(apItemId);
            if (ptr == IntPtr.Zero) continue;
            expectedQty.TryGetValue(ptr, out short current);
            expectedQty[ptr] = (short)(current + 1);
        }

        // Inject missing items and fix qty on existing ones
        IntPtr t1 = _context!.DllBase;
        short ringCount = _memory.ReadInt16(t1 + _context.Map.KeysRingCount);
        bool injectedAny = false;

        foreach (var (targetPtr, targetQty) in expectedQty)
        {
            int ringIdx = -1;
            for (int i = 0; i < ringCount; i++)
            {
                if (_memory.ReadPointer(t1 + _context.Map.KeysRingItems + i * 8) == targetPtr)
                { ringIdx = i; break; }
            }

            if (ringIdx >= 0)
            {
                short currentQty = _memory.ReadInt16(t1 + _context.Map.KeysRingQtys + ringIdx * 2);
                if (currentQty < targetQty)
                    _memory.Write(t1 + _context.Map.KeysRingQtys + ringIdx * 2, targetQty);
            }
            else if (ringCount < _context.Map.MaxRingItems)
            {
                _memory.Write(t1 + _context.Map.KeysRingItems + ringCount * 8, targetPtr.ToInt64());
                _memory.Write(t1 + _context.Map.KeysRingQtys + ringCount * 2, targetQty);
                ringCount++;
                _memory.Write(t1 + _context.Map.KeysRingCount, ringCount);
                injectedAny = true;
            }
        }

        if (injectedAny)
        {
            // Only log on first injection attempt, not re-injections during stabilization
            if (_keyItemEnsureCooldown == 0)
                ConsoleUI.Info($"[INV] Ensuring key items in Keys Ring ({expectedQty.Count} items)");

            // The game engine may overwrite the ring shortly after level load.
            // Reset the cooldown so we keep re-checking and re-injecting.
            _keyItemEnsureCooldown = KeyItemEnsureCooldownTicks;
        }
        else if (_keyItemEnsureCooldown > 0)
        {
            // Items are in the ring — count down. Once the cooldown expires
            // without needing re-injection, the ring is stable.
            _keyItemEnsureCooldown--;
        }
        else
        {
            // Cooldown expired and items are still in the ring — done.
            _keyItemsEnsured = true;
        }
    }

    /// <summary>
    /// Deep-copies the received key items dictionary for snapshot storage.
    /// </summary>
    public Dictionary<int, List<long>> CloneReceivedKeyItems()
    {
        var clone = new Dictionary<int, List<long>>();
        foreach (var (idx, items) in _receivedKeyItems)
            clone[idx] = new List<long>(items);
        return clone;
    }

    /// <summary>
    /// Returns a copy of the live used key items (apItemId → count used).
    /// Used by OnGameSaved to capture the current truth at save time.
    /// </summary>
    public Dictionary<long, int> GetUsedKeyItems() => new Dictionary<long, int>(_usedKeyItems);

    /// <summary>
    /// Sets the used key items set. EnsureKeyItemsInRing will skip these items.
    /// </summary>
    public void SetUsedKeyItems(Dictionary<long, int> usedKeyItems)
    {
        _usedKeyItems = usedKeyItems ?? new();
        if (_usedKeyItems.Count > 0)
            ConsoleUI.Info($"[INV] UsedKeyItems set: {string.Join(", ", _usedKeyItems.Select(kv => $"AP#{kv.Key}×{kv.Value}"))}");
    }

    /// <summary>
    /// Marks a single key item as used at runtime. Called when KeyItemMonitor
    /// detects a key disappearing from the ring during normal gameplay.
    /// </summary>
    public void AddUsedKeyItem(long apItemId)
    {
        _usedKeyItems.TryGetValue(apItemId, out int count);
        _usedKeyItems[apItemId] = count + 1;
        _keyItemsEnsured = false; // force re-evaluation with the updated used set
    }

    /// <summary>
    /// After a save reload, re-injects key items that were received but not yet used.
    /// Items in usedKeyItems are skipped (they were consumed before the save).
    /// </summary>
    public void ReconcileKeyItems(int mapperIdx, Dictionary<long, int> usedKeyItems)
    {
        if (!_receivedKeyItems.TryGetValue(mapperIdx, out var items))
            return;

        if (!EnsureCompassPointer())
            return;

        var remainingUsed = new Dictionary<long, int>(usedKeyItems);
        foreach (long apItemId in items)
        {
            if (remainingUsed.TryGetValue(apItemId, out int usedCount) && usedCount > 0)
            {
                remainingUsed[apItemId] = usedCount - 1;
                ConsoleUI.Info($"[INV] Reconcile: skipping used key item AP#{apItemId}");
                continue;
            }

            IntPtr targetPtr = ResolveKeyItemPointer(apItemId);
            if (targetPtr == IntPtr.Zero) continue;

            ConsoleUI.Info($"[INV] Reconcile: re-injecting key item AP#{apItemId} (ptr=0x{targetPtr:X})");
            InjectToRingRaw(
                _context.Map.KeysRingCount,
                _context.Map.KeysRingItems,
                _context.Map.KeysRingQtys,
                targetPtr, 1);
        }

        _keyItemsEnsured = false;
        _keyItemEnsureCooldown = KeyItemEnsureCooldownTicks;
    }

    /// <summary>
    /// Resolves a Keys Ring pointer back to an AP item ID.
    /// Used by KeyItemMonitor to identify which AP item was consumed.
    /// Returns 0 if not found.
    /// </summary>
    public long ResolvePointerToApId(long pointer, int mapperIdx)
    {
        if (!_receivedKeyItems.TryGetValue(mapperIdx, out var items))
            return 0;

        if (!EnsureCompassPointer())
            return 0;

        IntPtr targetPtr = new IntPtr(pointer);

        foreach (long apItemId in items)
        {
            IntPtr resolvedPtr = ResolveKeyItemPointer(apItemId);
            if (resolvedPtr == targetPtr)
                return apItemId;
        }

        return 0;
    }

    // =================================================================
    // RING INJECTION
    // =================================================================

    /// <summary>
    /// Injects an item into an inventory ring using a relIdx from Compass.
    /// If the item already exists in the ring, increments its qty.
    /// Otherwise appends it at the end with the given qty.
    /// </summary>
    private bool InjectToRing(int ringCountOffset, int ringItemsOffset, int ringQtysOffset, int relIdx, short qty)
    {
        if (_compassPtr == IntPtr.Zero) return false;
        IntPtr targetPtr = _compassPtr + relIdx * _context.Map.InventoryItemStride;
        return InjectToRingRaw(ringCountOffset, ringItemsOffset, ringQtysOffset, targetPtr, qty);
    }

    /// <summary>
    /// Injects an item into an inventory ring using a raw INVENTORY_ITEM pointer.
    /// Used for items not in the stride-aligned table (e.g. Key4/Thor Key).
    /// </summary>
    private bool InjectToRingRaw(int ringCountOffset, int ringItemsOffset, int ringQtysOffset, IntPtr targetPtr, short qty)
    {
        IntPtr t1 = _context!.DllBase;
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
        if (ringCount >= _context.Map.MaxRingItems)
            return false;

        _memory.Write(t1 + ringItemsOffset + ringCount * 8, targetPtr.ToInt64());
        _memory.Write(t1 + ringQtysOffset + ringCount * 2, qty);
        _memory.Write(t1 + ringCountOffset, (short)(ringCount + 1));
        return true;
    }

    /// <summary>
    /// Checks whether an item (by relIdx from Compass) exists in a ring.
    /// </summary>
    private bool HasItemInRing(int ringCountOffset, int ringItemsOffset, int weaponRelIdx)
    {
        if (_compassPtr == IntPtr.Zero) return false;
        IntPtr t1 = _context!.DllBase;
        IntPtr weaponPtr = _compassPtr + weaponRelIdx * _context.Map.InventoryItemStride;
        return HasItemInRingByPtr(ringCountOffset, ringItemsOffset, weaponPtr);
    }

    private bool HasItemInRingByPtr(int ringCountOffset, int ringItemsOffset, IntPtr targetPtr)
    {
        IntPtr t1 = _context!.DllBase;
        short count = _memory.ReadInt16(t1 + ringCountOffset);
        for (int i = 0; i < count; i++)
        {
            if (_memory.ReadPointer(t1 + ringItemsOffset + i * 8) == targetPtr)
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
        if (_compassPtr == IntPtr.Zero) return 0;
        IntPtr targetPtr = _compassPtr + relIdx * _context!.Map.InventoryItemStride;
        return RemoveFromRingByPtr(ringCountOffset, ringItemsOffset, ringQtysOffset, targetPtr);
    }

    private short RemoveFromRingByPtr(int ringCountOffset, int ringItemsOffset, int ringQtysOffset, IntPtr targetPtr)
    {
        IntPtr t1 = _context!.DllBase;
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
    /// Ensures the Compass pointer is cached. Tries to find it if not set.
    /// </summary>
    private bool EnsureCompassPointer()
    {
        if (_compassPtr != IntPtr.Zero) return true;
        _compassPtr = FindCompassPointer();
        return _compassPtr != IntPtr.Zero;
    }

    /// <summary>Invalidate cached Compass pointer (call on level change).</summary>
    public void InvalidateCompassPointer() => _compassPtr = IntPtr.Zero;

    /// <summary>
    /// Returns true if the inventory ring system is ready for injection
    /// (Compass pointer found). Items that need ring injection should be
    /// deferred until this returns true.
    /// </summary>
    public bool IsInventoryReady() => EnsureCompassPointer();

    /// <summary>True once key items are confirmed stable in the ring after injection.</summary>
    public bool KeyItemsEnsured => _keyItemsEnsured;

    /// <summary>
    /// Reset key item ensurance flag. Call on any game load (level change or same-level reload)
    /// so that EnsureKeyItemsInRing will re-inject items into the fresh ring.
    /// </summary>
    public void ResetKeyItemEnsurance()
    {
        _keyItemsEnsured = false;
        _keyItemEnsureCooldown = 0;
    }

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
        if (!EnsureCompassPointer()) return false;

        IntPtr t1 = _context!.DllBase;
        int smallMedObjId = _context.GameVersion == 0
            ? TR1RMemoryMap.InvObjId.SmallMedipack : TR2RMemoryMap.InvObjId.SmallMedipack;
        IntPtr targetPtr = _context.Map.ResolveInventoryItemPointer(_compassPtr, smallMedObjId);
        short ringCount = _memory.ReadInt16(t1 + _context.Map.MainRingCount);

        for (int i = 0; i < ringCount; i++)
        {
            if (_memory.ReadPointer(t1 + _context.Map.MainRingItems + i * 8) != targetPtr)
                continue;

            IntPtr qtyAddr = t1 + _context.Map.MainRingQtys + i * 2;
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
                long nextPtr = _memory.ReadInt64(t1 + _context.Map.MainRingItems + (j + 1) * 8);
                _memory.Write(t1 + _context.Map.MainRingItems + j * 8, nextPtr);
                short nextQty = _memory.ReadInt16(t1 + _context.Map.MainRingQtys + (j + 1) * 2);
                _memory.Write(t1 + _context.Map.MainRingQtys + j * 2, nextQty);
            }
            _memory.Write(t1 + _context.Map.MainRingItems + (ringCount - 1) * 8, 0L);
            _memory.Write(t1 + _context.Map.MainRingQtys + (ringCount - 1) * 2, (short)0);
            _memory.Write(t1 + _context.Map.MainRingCount, (short)(ringCount - 1));
            ConsoleUI.Info("[INV] Sentinel medipack removed (item removed from ring)");
            return true;
        }

        return false; // small medipack not found in ring yet — retry next tick
    }

    /// <summary>
    /// Finds the Compass INVENTORY_ITEM pointer from Main Ring items[0].
    /// Compass is always at index 0 in the Main Ring (lowest inv_pos, always in inventory).
    /// Verified by checking the object_id field.
    /// </summary>
    private IntPtr FindCompassPointer()
    {
        IntPtr t1 = _context!.DllBase;
        short ringCount = _memory.ReadInt16(t1 + _context.Map.MainRingCount);
        if (ringCount < 1) return IntPtr.Zero;

        IntPtr item0 = _memory.ReadPointer(t1 + _context.Map.MainRingItems);
        if (item0 == IntPtr.Zero) return IntPtr.Zero;

        // Verify it's actually the Compass by checking its object_id
        short objId = _memory.ReadInt16(item0 + _context.Map.InvItem_ObjectId);
        if (objId == _context!.Map.AnchorObjId)
            return item0;

        return IntPtr.Zero;
    }

    /// <summary>
    /// Resolves the INVENTORY_ITEM pointer for a key item AP ID.
    /// Uses key_item_slots from slot_data to determine the slot type (K1, K2, P1, etc.),
    /// then maps to the correct InvObjId via the game's IGameMemoryMap.
    /// </summary>
    private IntPtr ResolveKeyItemPointer(long apItemId)
    {
        var map = _context!.Map;

        // Look up slot type from slot_data (e.g. "K2", "P1", "Scion")
        string? slotType = _slotData?.KeyItemSlots.GetValueOrDefault(apItemId);

        if (slotType == null)
        {
            // Fallback for TR1: parse TR1Type enum name (backwards compat)
            if (_context.GameVersion == 0)
            {
                int offset = (int)(apItemId - _itemMapper.Config.ItemBaseId);
                var tr1Type = (TR1Type)offset;
                string name = tr1Type.ToString();
                if (name == offset.ToString())
                    return IntPtr.Zero;

                // Extract slot type from enum name pattern
                slotType = name switch
                {
                    _ when name.Contains("_K4") => "K4",
                    _ when name.Contains("_K1") => "K1",
                    _ when name.Contains("_K2") => "K2",
                    _ when name.Contains("_K3") => "K3",
                    _ when name.Contains("_P1") || name.Contains("_LeadBar") => "P1",
                    _ when name.Contains("_P2") => "P2",
                    _ when name.Contains("_P3") => "P3",
                    _ when name.Contains("_P4") => "P4",
                    _ when name.Contains("Scion") => "Scion",
                    _ => null,
                };
            }
        }

        if (slotType == null) return IntPtr.Zero;

        int invObjId = map.SlotTypeToInvObjId(slotType);
        if (invObjId < 0) return IntPtr.Zero;

        return map.ResolveInventoryItemPointer(_compassPtr, invObjId);
    }

    private void HalveAmmoInt32(IntPtr addr)
    {
        int current = _memory.ReadInt32(addr);
        if (current > 0)
            _memory.Write(addr, current / 2);
    }
}
