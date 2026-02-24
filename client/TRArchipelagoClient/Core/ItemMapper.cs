using TRLevelControl.Model;

namespace TRArchipelagoClient.Core;

/// <summary>
/// Maps between Archipelago item IDs and TR1 item types.
/// Uses the same ID schema as the APWorld: base_id (770000) + TR1Type enum value.
/// </summary>
public static class ItemMapper
{
    private const int BaseId = 770_000;
    private const int TrapBaseId = 769_000;

    private static readonly Dictionary<int, TR1Type> _idToType = new()
    {
        // Weapons
        [BaseId + 85] = TR1Type.Shotgun_S_P,
        [BaseId + 86] = TR1Type.Magnums_S_P,
        [BaseId + 87] = TR1Type.Uzis_S_P,
        // Ammo
        [BaseId + 89] = TR1Type.ShotgunAmmo_S_P,
        [BaseId + 90] = TR1Type.MagnumAmmo_S_P,
        [BaseId + 91] = TR1Type.UziAmmo_S_P,
        // Medipacks
        [BaseId + 93] = TR1Type.SmallMed_S_P,
        [BaseId + 94] = TR1Type.LargeMed_S_P,
    };

    // Key item alias ranges per level
    private static readonly Dictionary<int, string> _keyItemBaseLevels = new()
    {
        [10000] = "LEVEL1.PHD",
        [11000] = "LEVEL2.PHD",
        [12000] = "LEVEL3A.PHD",
        [13000] = "LEVEL3B.PHD",
        [14000] = "LEVEL4.PHD",
        [15000] = "LEVEL5.PHD",
        [16000] = "LEVEL6.PHD",
        [17000] = "LEVEL7A.PHD",
        [18000] = "LEVEL7B.PHD",
        [19000] = "LEVEL8A.PHD",
        [20000] = "LEVEL8B.PHD",
        [21000] = "LEVEL8C.PHD",
        [22000] = "LEVEL10A.PHD",
        [23000] = "LEVEL10B.PHD",
        [24000] = "LEVEL10C.PHD",
    };

    public enum ItemCategory
    {
        Weapon,
        Ammo,
        Medipack,
        KeyItem,
        Trap,
        Unknown,
    }

    public static ItemCategory GetCategory(long apItemId)
    {
        int id = (int)apItemId;
        if (id >= TrapBaseId && id < BaseId)
            return ItemCategory.Trap;
        if (_idToType.ContainsKey(id))
        {
            var type = _idToType[id];
            if (type == TR1Type.SmallMed_S_P || type == TR1Type.LargeMed_S_P)
                return ItemCategory.Medipack;
            if (type == TR1Type.Shotgun_S_P || type == TR1Type.Magnums_S_P || type == TR1Type.Uzis_S_P)
                return ItemCategory.Weapon;
            return ItemCategory.Ammo;
        }
        // Check if it's a key item (alias range: 770000 + 10000..24999)
        int offset = id - BaseId;
        if (offset >= 10000 && offset < 25000)
            return ItemCategory.KeyItem;
        return ItemCategory.Unknown;
    }

    public static TR1Type? GetTR1Type(long apItemId)
    {
        int id = (int)apItemId;
        if (_idToType.TryGetValue(id, out var type))
            return type;
        return null;
    }

    public static string GetKeyItemLevel(long apItemId)
    {
        int offset = (int)apItemId - BaseId;
        int baseKey = (offset / 1000) * 1000;
        return _keyItemBaseLevels.GetValueOrDefault(baseKey);
    }

    public static long GetApId(TR1Type type)
    {
        return BaseId + (int)(uint)type;
    }
}
