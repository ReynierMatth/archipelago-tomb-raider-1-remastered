using TRLevelControl.Model;

namespace TRArchipelagoClient.Core;

/// <summary>
/// Maps between Archipelago item IDs and game item types.
/// Uses the same ID schema as the APWorld: base_id + type enum value.
/// </summary>
public class ItemMapper
{
    private readonly GameConfig _config;
    private readonly Dictionary<int, TR1Type> _idToTR1Type;

    // TR1 type mappings (offset -> TR1Type)
    private static readonly Dictionary<int, TR1Type> _tr1TypeMap = new()
    {
        [85] = TR1Type.Shotgun_S_P,
        [86] = TR1Type.Magnums_S_P,
        [87] = TR1Type.Uzis_S_P,
        [89] = TR1Type.ShotgunAmmo_S_P,
        [90] = TR1Type.MagnumAmmo_S_P,
        [91] = TR1Type.UziAmmo_S_P,
        [93] = TR1Type.SmallMed_S_P,
        [94] = TR1Type.LargeMed_S_P,
    };

    public ItemMapper(GameConfig config)
    {
        _config = config;

        // Build the full ID -> TR1Type map from config base + offsets
        _idToTR1Type = new();
        foreach (var (offset, tr1Type) in _tr1TypeMap)
        {
            _idToTR1Type[config.ItemBaseId + offset] = tr1Type;
        }
    }

    public ItemCategory GetCategory(long apItemId)
    {
        int id = (int)apItemId;
        if (id >= _config.TrapBaseId && id < _config.ItemBaseId)
            return ItemCategory.Trap;

        int offset = id - _config.ItemBaseId;
        if (_config.GenericItems.ContainsKey(offset))
            return _config.GenericItems[offset].Category;

        // Check if it's a key item (alias range)
        foreach (var baseKey in _config.KeyItemLevelBases.Keys)
        {
            if (offset >= baseKey && offset < baseKey + 1000)
                return ItemCategory.KeyItem;
        }

        return ItemCategory.Unknown;
    }

    public TR1Type? GetTR1Type(long apItemId)
    {
        int id = (int)apItemId;
        if (_idToTR1Type.TryGetValue(id, out var type))
            return type;
        return null;
    }

    public string GetKeyItemLevel(long apItemId)
    {
        int offset = (int)apItemId - _config.ItemBaseId;
        int baseKey = (offset / 1000) * 1000;
        return _config.KeyItemLevelBases.GetValueOrDefault(baseKey);
    }

    public long GetApId(TR1Type type)
    {
        return _config.ItemBaseId + (int)(uint)type;
    }
}
