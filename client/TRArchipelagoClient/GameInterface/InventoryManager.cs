using TRArchipelagoClient.Core;
using TRArchipelagoClient.UI;
using TRLevelControl.Model;

namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Injects received items into the game in real-time via process memory.
///
/// Uses the WorldStateBackup buffer (tomb1.dll+0x4c4e00) which has the same
/// layout as a save file slot. Writing to this buffer modifies the live game
/// state for inventory items (ammo, medipacks, weapons).
///
/// For health (traps), writes directly to Lara's ITEM struct via the
/// LaraBase pointer chain.
/// </summary>
public class InventoryManager
{
    private readonly ProcessMemory _memory;

    // Pending key items for levels not currently loaded
    private readonly Dictionary<int, List<long>> _pendingKeyItems = new();

    public InventoryManager(ProcessMemory memory)
    {
        _memory = memory;
    }

    /// <summary>
    /// Address of the WorldStateBackup buffer (live inventory state).
    /// </summary>
    private IntPtr WorldStateAddr => _memory.Tomb1Base + TR1RMemoryMap.WorldStateBackup;

    /// <summary>
    /// Gives a weapon to the player by setting the weapon flag in the
    /// WorldStateBackup weapons config byte.
    /// </summary>
    public void GiveWeapon(long apItemId)
    {
        var type = ItemMapper.GetTR1Type(apItemId);
        if (type == null) return;

        byte weaponFlag = type.Value switch
        {
            TR1Type.Shotgun_S_P => TR1RMemoryMap.Weapon_Shotgun,
            TR1Type.Magnums_S_P => TR1RMemoryMap.Weapon_Magnums,
            TR1Type.Uzis_S_P => TR1RMemoryMap.Weapon_Uzis,
            _ => 0,
        };

        if (weaponFlag == 0) return;

        IntPtr weaponAddr = WorldStateAddr + TR1RMemoryMap.Save_WeaponsConfig;
        byte current = _memory.ReadByte(weaponAddr);
        _memory.Write(weaponAddr, (byte)(current | weaponFlag));

        // Also give starting ammo for the weapon
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
    /// Gives ammo by incrementing the inventory counter in WorldStateBackup.
    /// </summary>
    public void GiveAmmo(long apItemId)
    {
        var type = ItemMapper.GetTR1Type(apItemId);
        if (type == null) return;

        (int offset, ushort amount) = type.Value switch
        {
            TR1Type.ShotgunAmmo_S_P => (TR1RMemoryMap.Save_ShotgunAmmo, (ushort)12), // shells * 6 internally
            TR1Type.MagnumAmmo_S_P => (TR1RMemoryMap.Save_MagnumAmmo, (ushort)50),
            TR1Type.UziAmmo_S_P => (TR1RMemoryMap.Save_UziAmmo, (ushort)100),
            _ => (-1, (ushort)0),
        };

        if (offset < 0) return;

        IntPtr ammoAddr = WorldStateAddr + offset;
        ushort current = _memory.ReadUInt16(ammoAddr);
        ushort newVal = (ushort)Math.Min(current + amount, ushort.MaxValue);
        _memory.Write(ammoAddr, newVal);
    }

    /// <summary>
    /// Gives a medipack by incrementing the inventory counter.
    /// </summary>
    public void GiveMedipack(long apItemId)
    {
        var type = ItemMapper.GetTR1Type(apItemId);
        if (type == null) return;

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
            _memory.Write(medAddr, (byte)(current + 1));
    }

    /// <summary>
    /// Handles a key item. If the target level is currently active, injects directly.
    /// Otherwise, stores for later when that level is loaded.
    /// </summary>
    public void GiveKeyItem(long apItemId, int currentRuntimeLevelId)
    {
        string? targetLevelFile = ItemMapper.GetKeyItemLevel(apItemId);
        if (targetLevelFile == null) return;

        int targetMapperIdx = LocationMapper.GetLevelIndex(targetLevelFile);
        int currentMapperIdx = TR1RMemoryMap.ToLocationMapperIndex(currentRuntimeLevelId);

        if (targetMapperIdx == currentMapperIdx && currentMapperIdx >= 0)
        {
            // TODO: Inject key item into live inventory.
            // Key items use a separate inventory structure that needs further RE.
            // For now, log it — the item will be available if pre-patched into the level.
            ConsoleUI.Warning($"Key item received for current level (AP ID {apItemId}). " +
                            "Key item injection requires pre-patching.");
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
    /// Ammo/med drains write to the WorldStateBackup buffer.
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

            case 2: // Ammo Drain — halve all ammo
                HalveAmmo(WorldStateAddr + TR1RMemoryMap.Save_MagnumAmmo);
                HalveAmmo(WorldStateAddr + TR1RMemoryMap.Save_UziAmmo);
                HalveAmmo(WorldStateAddr + TR1RMemoryMap.Save_ShotgunAmmo);
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

    private void HalveAmmo(IntPtr addr)
    {
        ushort current = _memory.ReadUInt16(addr);
        if (current > 0)
            _memory.Write(addr, (ushort)(current / 2));
    }
}
