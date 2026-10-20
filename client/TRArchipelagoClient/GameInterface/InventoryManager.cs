using TRArchipelagoClient.Core;
using TRArchipelagoClient.UI;
using TRLevelControl.Model;

namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Injects received items into the game in real-time via process memory.
///
/// Injection strategy by item type:
///   - Weapons: call the game's own Inv_AddItem(objId) — the native path
///     inserts the ring entry, grants starting ammo (shotgun +12, magnums
///     +50, uzis +100) and converts any matching ammo ring items; we then
///     set the WSB weapon flag for save persistence
///   - Ammo: write directly to LARA_INFO when the weapon is owned (verified
///     working); otherwise call Inv_AddItem so the game inserts the ammo
///     ring item natively
///   - Medipacks: call Inv_AddItem (stacks qty natively when present)
///   - Key items: call Inv_AddItem — the game routes to the Keys Ring itself
///     (insert switches rings on the item's sort key)
///   - Traps: direct write to Lara's health / ammo fields
///
/// Inv_AddItem (tomb1.dll+0x1F7D0) is invoked inside the game process via
/// CreateRemoteThread (ProcessMemory.CallRemote) with objId as the thread
/// parameter (RCX). The game performs all ring bookkeeping. Success is
/// verified by ring read-back (count/qty matched on InvItem_ObjectId) —
/// exit codes are ambiguous (refusal and several success paths both return
/// 0). Remote calls are refused (and logged) when the Inv_AddItem prologue
/// does not match this build or the Ring offset group is not usable.
/// </summary>
public class InventoryManager
{
    private readonly ProcessMemory _memory;

    // Received key items per level — kept permanently for idempotent
    // re-injection after death/reload. EnsureKeyItemsInRing tops the ring
    // up to the expected qty (never past it), so duplicates are safe.
    private readonly Dictionary<int, List<long>> _receivedKeyItems = new();

    // Once all key items have been successfully injected after a load,
    // stop reconciling until the next load — prevents re-giving used keys.
    private bool _keyItemsEnsured;

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

    // =================================================================
    // NATIVE GIVE/REMOVE (Inv_AddItem / ring-remove via CreateRemoteThread)
    // =================================================================

    /// <summary>
    /// Calls the game's own Inv_AddItem(objId) inside the game process
    /// (thread-proc argument arrives in RCX). The game handles ring routing
    /// (main vs keys), qty stacking, pickup/canonical id aliasing
    /// (tomb1.dll+0x1F590) and the per-level loaded-object flag.
    /// objId may be a canonical inventory id (InvObjId.*) — the game remaps
    /// aliases itself, so either member of an id pair works when either is
    /// loaded in the current level.
    /// Returns true only when a ring read-back shows the effect (item
    /// appeared or qty increased). Exit codes are informational only:
    /// refusal and several success paths both return 0.
    /// </summary>
    public bool GiveByObjId(int objId, string what, bool keysRing = false)
    {
        IntPtr t1 = _memory.Tomb1Base;
        if (t1 == IntPtr.Zero)
        {
            ConsoleUI.Warning($"[INV] {what}: tomb1.dll not loaded");
            return false;
        }

        if (!OffsetModel.Usable(OffsetGroup.Ring))
        {
            ConsoleUI.Warning($"[INV] {what}: Ring offsets not usable — cannot verify give(objId={objId})");
            return false;
        }

        if (!EnsureSignature(TR1RMemoryMap.InvAddItem, TR1RMemoryMap.InvAddItemSignature, "Inv_AddItem"))
            return false;

        int itemsOff = keysRing ? TR1RMemoryMap.KeysRingItems : TR1RMemoryMap.MainRingItems;
        int countOff = keysRing ? TR1RMemoryMap.KeysRingCount : TR1RMemoryMap.MainRingCount;
        int qtysOff = keysRing ? TR1RMemoryMap.KeysRingQtys : TR1RMemoryMap.MainRingQtys;

        var (_, beforeQty) = FindInRing(itemsOff, countOff, qtysOff, objId);

        uint? exit = _memory.CallRemote(t1 + TR1RMemoryMap.InvAddItem, objId);

        var (_, afterQty) = FindInRing(itemsOff, countOff, qtysOff, objId);

        if (afterQty > beforeQty)
        {
            ConsoleUI.Info($"[INV] {what}: give(objId={objId}) OK — qty {beforeQty} -> {afterQty}" +
                (exit.HasValue ? $" (exit 0x{exit:X})" : " (call timed out)"));
            return true;
        }

        byte flag = _memory.ReadByte(t1 + objId * TR1RMemoryMap.InvLoadedFlagStride + TR1RMemoryMap.InvLoadedFlagTable);
        string aliasInfo = "";
        int? alias = AliasOfObjId(objId);
        if (alias.HasValue)
        {
            byte aliasFlag = _memory.ReadByte(t1 + alias.Value * TR1RMemoryMap.InvLoadedFlagStride + TR1RMemoryMap.InvLoadedFlagTable);
            aliasInfo = $", alias {alias.Value} flag={aliasFlag}";
        }
        ConsoleUI.Warning(
            $"[INV] {what}: give(objId={objId}) had NO effect — flag[{objId}]={flag}{aliasInfo}, " +
            $"qty {beforeQty} -> {afterQty}" +
            (exit.HasValue ? $", exit 0x{exit:X}" : ", call timed out") +
            " (object not loaded in this level?)");
        return false;
    }

    /// <summary>
    /// Calls the game's own ring-remove (tomb1.dll+0x1FF80, arg = objId) —
    /// qty-- natively, with full removal (shift + count--) at qty 0; it
    /// searches the main ring first, then the keys ring. Returns true only
    /// when the main-ring read-back shows the qty dropped. Used for the
    /// sentinel medipack cancel (the game natively adds it on pickup).
    /// </summary>
    private bool RemoveByObjId(int objId, string what)
    {
        IntPtr t1 = _memory.Tomb1Base;
        if (t1 == IntPtr.Zero || !OffsetModel.Usable(OffsetGroup.Ring))
            return false;

        if (!EnsureSignature(TR1RMemoryMap.InvRemoveItem, TR1RMemoryMap.InvRemoveItemSignature, "Inv_RemoveItem"))
            return false;

        var (_, beforeQty) = FindInRing(
            TR1RMemoryMap.MainRingItems, TR1RMemoryMap.MainRingCount,
            TR1RMemoryMap.MainRingQtys, objId);
        if (beforeQty <= 0)
            return false; // not in the ring yet — caller retries next tick

        uint? exit = _memory.CallRemote(t1 + TR1RMemoryMap.InvRemoveItem, objId);

        var (_, afterQty) = FindInRing(
            TR1RMemoryMap.MainRingItems, TR1RMemoryMap.MainRingCount,
            TR1RMemoryMap.MainRingQtys, objId);

        if (afterQty < beforeQty)
        {
            ConsoleUI.Info($"[INV] {what}: remove(objId={objId}) OK — qty {beforeQty} -> {afterQty}");
            return true;
        }

        ConsoleUI.Warning(
            $"[INV] {what}: remove(objId={objId}) had NO effect — qty {beforeQty} -> {afterQty}" +
            (exit.HasValue ? $", exit 0x{exit:X}" : ", call timed out"));
        return false;
    }

    /// <summary>
    /// Validates the live prologue of a tomb1.dll function before the first
    /// remote call (and on every call — 21 bytes, negligible). A mismatch
    /// means the game patched under us; every give/remove is refused and
    /// logged instead of executing unknown code.
    /// </summary>
    private bool EnsureSignature(int rva, byte[] expected, string name)
    {
        IntPtr t1 = _memory.Tomb1Base;
        if (t1 == IntPtr.Zero) return false;

        byte[] live = _memory.ReadBytes(t1 + rva, expected.Length);
        if (live.SequenceEqual(expected))
            return true;

        ConsoleUI.Warning(
            $"[INV] {name} signature MISMATCH at tomb1.dll+0x{rva:X} " +
            $"(live: {BitConverter.ToString(live)}) — refusing remote calls; offsets need updating for this patch");
        return false;
    }

    /// <summary>
    /// Finds a ring entry by the inventory object id stored in the struct
    /// itself (INVENTORY_ITEM+0x08). Returns (-1, 0) when absent.
    /// Matching is ALWAYS on objId — never on pointer identity (the
    /// stride/relIdx pointer model is wrong for Key2/Key3/Key4).
    /// Uses the proven count field; falls back to the NULL-walk if the
    /// count reads out of range.
    /// </summary>
    public (int Index, short Qty) FindInRing(int itemsOff, int countOff, int qtysOff, int objId)
    {
        IntPtr t1 = _memory.Tomb1Base;
        short count = TR1RMemoryMap.MainRingCountConfirmed
            ? _memory.ReadInt16(t1 + countOff)
            : OffsetIdentifier.RingWalk(_memory, itemsOff);
        if (count < 0 || count > TR1RMemoryMap.MaxRingItems)
            count = OffsetIdentifier.RingWalk(_memory, itemsOff);

        for (int i = 0; i < count; i++)
        {
            IntPtr ptr = _memory.ReadPointer(t1 + itemsOff + i * 8);
            if (ptr == IntPtr.Zero) continue;
            if (_memory.ReadInt16(ptr + TR1RMemoryMap.InvItem_ObjectId) != objId) continue;
            return (i, _memory.ReadInt16(t1 + qtysOff + i * 2));
        }
        return (-1, 0);
    }

    /// <summary>
    /// Pickup id &lt;-> canonical id pairs the game's internal remap
    /// (tomb1.dll+0x1F590) understands. Diagnostic only: Inv_AddItem
    /// performs the remap itself — used here to explain refusals
    /// ("flag[canonical]=0 but alias is loaded").
    /// </summary>
    public static int? AliasOfObjId(int objId) => objId switch
    {
        84 => 99, 99 => 84,
        85 => 100, 100 => 85,
        86 => 101, 101 => 86,
        87 => 102, 102 => 87,
        89 => 104, 104 => 89,
        90 => 105, 105 => 90,
        91 => 106, 106 => 91,
        93 => 108, 108 => 93,
        94 => 109, 109 => 94,
        110 => 114, 114 => 110,
        111 => 115, 115 => 111,
        112 => 116, 116 => 112,
        113 => 117, 117 => 113,
        126 => 127, 127 => 126,
        129 => 133, 133 => 129,
        130 => 134, 134 => 130,
        131 => 135, 135 => 131,
        132 => 136, 136 => 132,
        141 => 148, 148 => 141,
        142 => 149, 149 => 142,
        72 => 82, 82 => 72,
        _ => null,
    };

    /// <summary>
    /// Gives a weapon via the game's native Inv_AddItem(objId): inserts the
    /// ring entry and grants starting ammo natively (shotgun +12 rounds,
    /// magnums +50, uzis +100; any matching ammo ring items are converted to
    /// LARA_INFO by the same native path). Then sets the WSB weapon flag for
    /// save persistence. Already owned → no-op success (a re-give would only
    /// stack qty).
    /// </summary>
    public bool GiveWeapon(long apItemId)
    {
        var type = ItemMapper.GetTR1Type(apItemId);
        if (type == null) return false;

        int objId = type.Value switch
        {
            TR1Type.Shotgun_S_P => TR1RMemoryMap.InvObjId.Shotgun,
            TR1Type.Magnums_S_P => TR1RMemoryMap.InvObjId.Magnums,
            TR1Type.Uzis_S_P => TR1RMemoryMap.InvObjId.Uzis,
            _ => -1,
        };

        if (objId < 0) return false;

        bool ok;
        bool owned = OffsetModel.Usable(OffsetGroup.Ring) &&
            FindInRing(TR1RMemoryMap.MainRingItems, TR1RMemoryMap.MainRingCount,
                TR1RMemoryMap.MainRingQtys, objId).Index >= 0;

        if (owned)
        {
            ConsoleUI.Info($"[INV] {type.Value}: already owned");
            ok = true;
        }
        else
        {
            ok = GiveByObjId(objId, type.Value.ToString());
            if (!ok) return false;
        }

        // WSB weapon flag for save persistence (idempotent OR).
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
            WriteGate.CheckedWrite(_memory, OffsetGroup.Wsb, weaponAddr, (byte)(weaponByte | weaponFlag), "WSB weapon flag");
        }

        return ok;
    }

    /// <summary>
    /// Gives ammo. If the player owns the weapon, writes directly to LARA_INFO
    /// ammo fields (instant, verified working). If not, calls Inv_AddItem so
    /// the game inserts the ammo ring item natively — it converts to LARA_INFO
    /// the moment the weapon arrives.
    /// Shotgun ammo is stored internally as displayed * 6.
    /// </summary>
    public bool GiveAmmo(long apItemId)
    {
        var type = ItemMapper.GetTR1Type(apItemId);
        if (type == null) return false;

        (int weaponObjId, int ammoObjId, int laraInfoOffset, int amount) = type.Value switch
        {
            TR1Type.ShotgunAmmo_S_P => (TR1RMemoryMap.InvObjId.Shotgun, TR1RMemoryMap.InvObjId.ShotgunAmmo,
                TR1RMemoryMap.Lara_ShotgunAmmo, 2 * TR1RMemoryMap.ShotgunAmmoMultiplier),
            TR1Type.MagnumAmmo_S_P => (TR1RMemoryMap.InvObjId.Magnums, TR1RMemoryMap.InvObjId.MagnumAmmo,
                TR1RMemoryMap.Lara_MagnumAmmo, 50),
            TR1Type.UziAmmo_S_P => (TR1RMemoryMap.InvObjId.Uzis, TR1RMemoryMap.InvObjId.UziAmmo,
                TR1RMemoryMap.Lara_UziAmmo, 100),
            _ => (-1, -1, -1, 0),
        };

        if (ammoObjId < 0) return false;

        // Check if the player has the weapon in the Main Ring (objId match)
        bool hasWeapon = OffsetModel.Usable(OffsetGroup.Ring) &&
            FindInRing(TR1RMemoryMap.MainRingItems, TR1RMemoryMap.MainRingCount,
                TR1RMemoryMap.MainRingQtys, weaponObjId).Index >= 0;

        if (hasWeapon)
        {
            // Player has the weapon — add directly to LARA_INFO ammo counter
            IntPtr ammoAddr = _memory.Tomb1Base + laraInfoOffset;
            int current = _memory.ReadInt32(ammoAddr);
            int newVal = Math.Min(current + amount, 999999);
            bool ok = WriteGate.CheckedWrite(_memory, OffsetGroup.Core, ammoAddr, newVal, "LARA_INFO ammo");
            if (ok)
                ConsoleUI.Info($"[INV] Ammo: {current} -> {newVal} (LARA_INFO)");
            return ok;
        }

        // Weapon not owned — game inserts the ammo item into the ring natively.
        return GiveByObjId(ammoObjId, $"{type.Value} item");
    }

    /// <summary>
    /// Gives a medipack via the game's Inv_AddItem(objId): inserts the ring
    /// entry, or stacks the qty natively when already present.
    /// </summary>
    public bool GiveMedipack(long apItemId)
    {
        var type = ItemMapper.GetTR1Type(apItemId);
        if (type == null) return false;

        int objId = type.Value switch
        {
            TR1Type.SmallMed_S_P => TR1RMemoryMap.InvObjId.SmallMedipack,
            TR1Type.LargeMed_S_P => TR1RMemoryMap.InvObjId.LargeMedipack,
            _ => -1,
        };

        if (objId < 0) return false;

        return GiveByObjId(objId, type.Value.ToString());
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
    /// Attempts to give a key item into the Keys Ring via Inv_AddItem.
    /// Called from GiveKeyItem (immediate) and from OnLevelChanged (deferred).
    /// Safe to call repeatedly — GiveByObjId verifies by read-back and the
    /// ensure loop only tops up to the expected qty.
    /// </summary>
    private bool TryInjectKeyItem(long apItemId)
    {
        int objId = ResolveKeyItemObjId(apItemId);
        if (objId < 0)
        {
            ConsoleUI.Warning($"[INV] Key item AP ID {apItemId}: unknown type, cannot inject.");
            return false;
        }

        return GiveByObjId(objId, $"Key item AP#{apItemId}", keysRing: true);
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
                    if (WriteGate.CheckedWrite(_memory, OffsetGroup.Core, healthAddr, newHealth, "Lara health"))
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
                if (meds > 0 && WriteGate.CheckedWrite(_memory, OffsetGroup.Wsb, smallMedAddr, (byte)(meds - 1), "WSB small medipack"))
                    ConsoleUI.Warning("TRAP! Lost a small medipack!");
                break;
        }
    }

    /// <summary>
    /// Gets received key items for a specific level. Items are NOT removed —
    /// they are kept for idempotent re-injection after death/reload
    /// (EnsureKeyItemsInRing tops the ring up to the expected qty, so
    /// duplicates are safe).
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
        if (!OffsetModel.Usable(OffsetGroup.Ring)) return; // cannot read/verify yet

        // Count expected qty per objId from received items (skip used copies only)
        var remainingUsed = new Dictionary<long, int>(_usedKeyItems);
        var expectedQty = new Dictionary<int, short>();
        foreach (long apItemId in items)
        {
            if (remainingUsed.TryGetValue(apItemId, out int usedCount) && usedCount > 0)
            {
                remainingUsed[apItemId] = usedCount - 1;
                continue;
            }

            int objId = ResolveKeyItemObjId(apItemId);
            if (objId < 0) continue;
            expectedQty.TryGetValue(objId, out short current);
            expectedQty[objId] = (short)(current + 1);
        }

        // Top up each slot through the game's own Inv_AddItem: never below the
        // ring's current qty (idempotent), never above the expected qty (used
        // keys stay used; a higher-than-expected qty is game-granted — leave it).
        bool injectedAny = false;

        foreach (var (objId, targetQty) in expectedQty)
        {
            var (ringIdx, currentQty) = FindInRing(
                TR1RMemoryMap.KeysRingItems, TR1RMemoryMap.KeysRingCount,
                TR1RMemoryMap.KeysRingQtys, objId);

            if (ringIdx >= 0 && currentQty >= targetQty)
                continue;

            for (short n = currentQty; n < targetQty; n++)
            {
                if (!GiveByObjId(objId, $"Key item objId {objId} (ensure)", keysRing: true))
                    break; // refused (object not loaded in this level?) — cooldown will retry
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
    /// After a save reload, re-arm key-item ensurance for the level: the used
    /// set from the save is applied here, then EnsureKeyItemsInRing tops the
    /// Keys Ring back up to the expected qty (idempotent — it compares against
    /// the live ring, so items restored by the save are not doubled and used
    /// keys are skipped).
    /// </summary>
    public void ReconcileKeyItems(int mapperIdx, Dictionary<long, int> usedKeyItems)
    {
        if (!_receivedKeyItems.ContainsKey(mapperIdx))
            return;

        _usedKeyItems = usedKeyItems ?? new();
        if (_usedKeyItems.Count > 0)
            ConsoleUI.Info($"[INV] Reconcile: used set from save — {string.Join(", ", _usedKeyItems.Select(kv => $"AP#{kv.Key}×{kv.Value}"))}");

        _keyItemsEnsured = false;
        _keyItemEnsureCooldown = KeyItemEnsureCooldownTicks;
    }

    /// <summary>
    /// Resolves a Keys Ring pointer back to an AP item ID.
    /// Used by KeyItemMonitor to identify which AP item was consumed:
    /// the pointed struct's own objId is matched against the objId each
    /// received AP item maps to (K1-K4/P1-P4/Scion/LeadBar slots).
    /// Returns 0 if not found.
    /// </summary>
    public long ResolvePointerToApId(long pointer, int mapperIdx)
    {
        if (!_receivedKeyItems.TryGetValue(mapperIdx, out var items))
            return 0;

        short objId = _memory.ReadInt16(new IntPtr(pointer) + TR1RMemoryMap.InvItem_ObjectId);
        if (objId < 0)
            return 0;

        foreach (long apItemId in items)
        {
            if (ResolveKeyItemObjId(apItemId) == objId)
                return apItemId;
        }

        return 0;
    }

    // =================================================================
    // READINESS
    // =================================================================

    /// <summary>Retained for existing level-change call sites. The give path
    /// no longer caches any pistols/relIdx pointer (matching is on objId), so
    /// there is nothing to invalidate.</summary>
    public void InvalidatePistolsPointer() { }

    /// <summary>
    /// Returns true when a native give can be attempted: tomb1.dll is loaded
    /// and the Inv_AddItem prologue matches this build. Ring-offset usability
    /// is checked separately inside GiveByObjId (the Ring group is validated
    /// per level transition by OffsetIdentifier.IdentifyRing).
    /// </summary>
    public bool IsInventoryReady() => EnsureSignature(
        TR1RMemoryMap.InvAddItem, TR1RMemoryMap.InvAddItemSignature, "Inv_AddItem");

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
    /// Removes one small medipack from the Main Ring by calling the game's
    /// own ring-remove (qty-- natively; full removal at qty 0).
    /// Returns false while the medipack is not yet in the ring — the caller
    /// retries next tick (the game may add it on a later tick than the
    /// entity flag change).
    /// </summary>
    private bool RemoveOneSentinelMedipack()
    {
        var (idx, qty) = FindInRing(
            TR1RMemoryMap.MainRingItems, TR1RMemoryMap.MainRingCount,
            TR1RMemoryMap.MainRingQtys, TR1RMemoryMap.InvObjId.SmallMedipack);
        if (idx < 0 || qty <= 0)
            return false; // small medipack not found in ring yet — retry next tick

        return RemoveByObjId(TR1RMemoryMap.InvObjId.SmallMedipack, "Sentinel medipack");
    }

    /// <summary>
    /// Resolves the canonical inventory object id (INVENTORY_ITEM+0x08 value,
    /// i.e. what the ring stores and what Inv_AddItem expects) for a key item
    /// AP ID. AP IDs use level-specific TR1Type aliases (e.g. Folly_K4_ThorKey
    /// = 14280), not generic types. We cast to TR1Type and parse the enum name
    /// to determine the generic slot (K1-K4, P1-P4, Scion, LeadBar).
    /// Returns -1 when the AP id maps to no known slot.
    /// </summary>
    public int ResolveKeyItemObjId(long apItemId)
    {
        int enumValue = (int)(apItemId - 770_000);
        var tr1Type = (TR1Type)enumValue;
        string name = tr1Type.ToString();

        // If ToString() returns just a number, the enum value is undefined
        if (name == enumValue.ToString())
            return -1;

        // Determine generic slot from the alias name pattern.
        // K4 must be checked before K1 (to avoid "_K4_" matching "_K1" substring
        // issue — not possible, but order is cleaner).
        if (name.Contains("_K1_") || name.Contains("_K1"))
            return TR1RMemoryMap.InvObjId.Key1;
        if (name.Contains("_K2_") || name.Contains("_K2"))
            return TR1RMemoryMap.InvObjId.Key2;
        if (name.Contains("_K3_") || name.Contains("_K3"))
            return TR1RMemoryMap.InvObjId.Key3;
        if (name.Contains("_K4_") || name.Contains("_K4"))
            return TR1RMemoryMap.InvObjId.Key4;
        if (name.Contains("_P1_") || name.Contains("_P1") || name.Contains("_LeadBar"))
            return TR1RMemoryMap.InvObjId.Puzzle1;
        if (name.Contains("_P2_") || name.Contains("_P2"))
            return TR1RMemoryMap.InvObjId.Puzzle2;
        if (name.Contains("_P3_") || name.Contains("_P3"))
            return TR1RMemoryMap.InvObjId.Puzzle3;
        if (name.Contains("_P4_") || name.Contains("_P4"))
            return TR1RMemoryMap.InvObjId.Puzzle4;
        if (name.Contains("_Scion") || name.Contains("Scion"))
            return TR1RMemoryMap.InvObjId.Scion;

        return -1;
    }

    private void HalveAmmoInt32(IntPtr addr)
    {
        int current = _memory.ReadInt32(addr);
        if (current > 0)
            WriteGate.CheckedWrite(_memory, OffsetGroup.Core, addr, current / 2, "LARA_INFO ammo halve");
    }
}
