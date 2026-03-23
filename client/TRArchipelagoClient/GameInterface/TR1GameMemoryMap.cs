namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// TR1 implementation of IGameMemoryMap. Delegates to TR1RMemoryMap static constants
/// and provides TR1-specific behavioral logic (pointer chain HP, bitmask secrets, all stride-aligned).
/// </summary>
public class TR1GameMemoryMap : IGameMemoryMap
{
    // --- Identity ---
    public string GameKey => "tr1";
    public string ModuleName => TR1RMemoryMap.TR1ModuleName;

    public IntPtr GetDllBase(ProcessMemory memory) => memory.Tomb1Base;

    // --- Level & Game State ---
    public int LevelId => TR1RMemoryMap.LevelId;
    public int LevelCompleted => TR1RMemoryMap.LevelCompleted;
    public int IsInGameScene => TR1RMemoryMap.IsInGameScene;
    public int BinaryTick => TR1RMemoryMap.BinaryTick;
    public int ActionKeys => TR1RMemoryMap.ActionKeys;
    public int Level_Home => TR1RMemoryMap.Level_Home;
    public int Level_MainMenu => TR1RMemoryMap.Level_MainMenu;

    // --- Entity Array ---
    public int EntitiesPointer => TR1RMemoryMap.EntitiesPointer;
    public int EntitiesCount => TR1RMemoryMap.EntitiesCount;
    public int EntitySize => TR1RMemoryMap.EntitySize;
    public int Item_ObjectId => TR1RMemoryMap.Item_ObjectId;
    public int Item_Flags => TR1RMemoryMap.Item_Flags;
    public int Item_HitPoints => TR1RMemoryMap.Item_HitPoints;
    public int Item_PosX => TR1RMemoryMap.Item_PosX;
    public int Item_PosY => TR1RMemoryMap.Item_PosY;
    public int Item_PosZ => TR1RMemoryMap.Item_PosZ;
    public int Item_RoomNum => TR1RMemoryMap.Item_RoomNum;

    // --- Inventory Rings ---
    public int MainRingCount => TR1RMemoryMap.MainRingCount;
    public int MainRingItems => TR1RMemoryMap.MainRingItems;
    public int MainRingQtys => TR1RMemoryMap.MainRingQtys;
    public int KeysRingCount => TR1RMemoryMap.KeysRingCount;
    public int KeysRingItems => TR1RMemoryMap.KeysRingItems;
    public int KeysRingQtys => TR1RMemoryMap.KeysRingQtys;
    public int MaxRingItems => TR1RMemoryMap.MaxRingItems;
    public int InventoryItemStride => TR1RMemoryMap.InventoryItemStride;
    public int InvItem_ObjectId => TR1RMemoryMap.InvItem_ObjectId;

    // --- Anchor ---
    public int AnchorObjId => TR1RMemoryMap.InvObjId.Compass;

    // --- WSB ---
    public int WorldStateBackup => TR1RMemoryMap.WorldStateBackup;
    public int WorldStateBackupSize => TR1RMemoryMap.WorldStateBackupSize;
    public int WSB_SaveCounter => TR1RMemoryMap.WSB_SaveCounter;

    // --- Health ---
    public short MaxHealth => TR1RMemoryMap.MaxHealth;
    public short MinHealth => TR1RMemoryMap.MinHealth;

    // --- Dictionaries ---
    public Dictionary<int, string> LevelNames => TR1RMemoryMap.LevelNames;
    public Dictionary<int, string> InvObjIdNames => TR1RMemoryMap.InvObjIdNames;

    // =============== BEHAVIORAL METHODS ===============

    public short ReadHealth(ProcessMemory memory, IntPtr dllBase)
    {
        // TR1: pointer chain LaraBase → dereference → +0x26 (Int16)
        IntPtr laraPtr = memory.ReadPointer(dllBase + TR1RMemoryMap.LaraBase);
        if (laraPtr == IntPtr.Zero) return MaxHealth;
        return memory.ReadInt16(laraPtr + TR1RMemoryMap.Item_HitPoints);
    }

    public ushort ReadSecrets(ProcessMemory memory, IntPtr dllBase)
    {
        // TR1: bitmask in WSB at Runtime_SecretsFound offset
        return memory.ReadUInt16(dllBase + TR1RMemoryMap.WorldStateBackup + TR1RMemoryMap.Runtime_SecretsFound);
    }

    public List<int> DetectNewSecrets(ushort previous, ushort current)
    {
        // TR1: bitmask — XOR to find newly set bits
        var result = new List<int>();
        ushort newBits = (ushort)(current & ~previous);
        for (int s = 0; s < 16; s++)
        {
            if ((newBits & (1 << s)) != 0)
                result.Add(s);
        }
        return result;
    }

    public int ToLocationMapperIndex(int runtimeLevelId) =>
        TR1RMemoryMap.ToLocationMapperIndex(runtimeLevelId);

    public IntPtr FindAnchorPointer(ProcessMemory memory, IntPtr dllBase)
    {
        short count = memory.ReadInt16(dllBase + MainRingCount);
        if (count < 1) return IntPtr.Zero;

        IntPtr ptr = memory.ReadPointer(dllBase + MainRingItems);
        if (ptr == IntPtr.Zero) return IntPtr.Zero;

        short objId = memory.ReadInt16(ptr + InvItem_ObjectId);
        return objId == AnchorObjId ? ptr : IntPtr.Zero;
    }

    public IntPtr ResolveInventoryItemPointer(IntPtr anchorPtr, int invObjId)
    {
        if (anchorPtr == IntPtr.Zero) return IntPtr.Zero;

        // Key4 special case (not stride-aligned)
        if (invObjId == TR1RMemoryMap.InvObjId.Key4)
            return anchorPtr + TR1RMemoryMap.Key4ByteOffset;

        // All other TR1 items are stride-aligned
        int? relIdx = ObjIdToRelIdx(invObjId);
        if (relIdx == null) return IntPtr.Zero;

        return anchorPtr + relIdx.Value * InventoryItemStride;
    }

    public void WriteHealth(ProcessMemory memory, IntPtr dllBase, short health)
    {
        IntPtr laraPtr = memory.ReadPointer(dllBase + TR1RMemoryMap.LaraBase);
        if (laraPtr != IntPtr.Zero)
            memory.Write(laraPtr + TR1RMemoryMap.Item_HitPoints, health);
    }

    public bool IsKeyItem(int invObjId) =>
        invObjId is (>= 0x72 and <= 0x75)  // Puzzle1-4
            or (>= 0x85 and <= 0x88)       // Key1-4
            or 0x96;                         // Scion

    public int SlotTypeToInvObjId(string slotType) => slotType switch
    {
        "K1" => TR1RMemoryMap.InvObjId.Key1,
        "K2" => TR1RMemoryMap.InvObjId.Key2,
        "K3" => TR1RMemoryMap.InvObjId.Key3,
        "K4" => TR1RMemoryMap.InvObjId.Key4,
        "P1" => TR1RMemoryMap.InvObjId.Puzzle1,
        "P2" => TR1RMemoryMap.InvObjId.Puzzle2,
        "P3" => TR1RMemoryMap.InvObjId.Puzzle3,
        "P4" => TR1RMemoryMap.InvObjId.Puzzle4,
        "Scion" => TR1RMemoryMap.InvObjId.Scion,
        _ when slotType.StartsWith("LeadBar") => TR1RMemoryMap.InvObjId.Puzzle1,
        _ => -1,
    };

    // =============== ITEM INJECTION RECIPES ===============

    // AP item offsets from ItemBaseId (must match APWorld itemDefinitions)
    private const int _shotgunOffset = 85;
    private const int _magnumsOffset = 86;
    private const int _uzisOffset = 87;
    private const int _shotgunAmmoOffset = 89;
    private const int _magnumAmmoOffset = 90;
    private const int _uziAmmoOffset = 91;
    private const int _smallMedOffset = 93;
    private const int _largeMedOffset = 94;

    public WeaponRecipe? GetWeaponRecipe(long apItemId, int itemBaseId)
    {
        int offset = (int)apItemId - itemBaseId;
        return offset switch
        {
            _shotgunOffset => new WeaponRecipe
            {
                WeaponObjId = TR1RMemoryMap.InvObjId.Shotgun, WeaponFlag = TR1RMemoryMap.Weapon_Shotgun,
                AmmoObjId = TR1RMemoryMap.InvObjId.ShotgunAmmo,
                LaraAmmoOffset = TR1RMemoryMap.Lara_ShotgunAmmo,
                StartingAmmo = 2 * TR1RMemoryMap.ShotgunAmmoMultiplier, Name = "Shotgun"
            },
            _magnumsOffset => new WeaponRecipe
            {
                WeaponObjId = TR1RMemoryMap.InvObjId.Magnums, WeaponFlag = TR1RMemoryMap.Weapon_Magnums,
                AmmoObjId = TR1RMemoryMap.InvObjId.MagnumAmmo,
                LaraAmmoOffset = TR1RMemoryMap.Lara_MagnumAmmo,
                StartingAmmo = 50, Name = "Magnums"
            },
            _uzisOffset => new WeaponRecipe
            {
                WeaponObjId = TR1RMemoryMap.InvObjId.Uzis, WeaponFlag = TR1RMemoryMap.Weapon_Uzis,
                AmmoObjId = TR1RMemoryMap.InvObjId.UziAmmo,
                LaraAmmoOffset = TR1RMemoryMap.Lara_UziAmmo,
                StartingAmmo = 100, Name = "Uzis"
            },
            _ => null,
        };
    }

    public AmmoRecipe? GetAmmoRecipe(long apItemId, int itemBaseId)
    {
        int offset = (int)apItemId - itemBaseId;
        return offset switch
        {
            _shotgunAmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR1RMemoryMap.InvObjId.Shotgun, AmmoObjId = TR1RMemoryMap.InvObjId.ShotgunAmmo,
                LaraAmmoOffset = TR1RMemoryMap.Lara_ShotgunAmmo,
                Amount = 2 * TR1RMemoryMap.ShotgunAmmoMultiplier, Name = "Shotgun Ammo"
            },
            _magnumAmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR1RMemoryMap.InvObjId.Magnums, AmmoObjId = TR1RMemoryMap.InvObjId.MagnumAmmo,
                LaraAmmoOffset = TR1RMemoryMap.Lara_MagnumAmmo,
                Amount = 50, Name = "Magnum Ammo"
            },
            _uziAmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR1RMemoryMap.InvObjId.Uzis, AmmoObjId = TR1RMemoryMap.InvObjId.UziAmmo,
                LaraAmmoOffset = TR1RMemoryMap.Lara_UziAmmo,
                Amount = 100, Name = "Uzi Ammo"
            },
            _ => null,
        };
    }

    public int GetMedipackObjId(long apItemId, int itemBaseId)
    {
        int offset = (int)apItemId - itemBaseId;
        return offset switch
        {
            _smallMedOffset => TR1RMemoryMap.InvObjId.SmallMedipack,
            _largeMedOffset => TR1RMemoryMap.InvObjId.LargeMedipack,
            _ => -1,
        };
    }

    public TrapRecipe? GetTrapRecipe(long apItemId, int trapBaseId)
    {
        int offset = (int)apItemId - trapBaseId;
        return offset switch
        {
            1 => new TrapRecipe { Type = TrapType.Damage, Name = "Damage Trap" },
            2 => new TrapRecipe { Type = TrapType.AmmoDrain, Name = "Ammo Drain" },
            3 => new TrapRecipe { Type = TrapType.SmallDrain, Name = "Small Drain" },
            _ => null,
        };
    }

    // --- Internal helpers ---

    private static int? ObjIdToRelIdx(int invObjId) => invObjId switch
    {
        TR1RMemoryMap.InvObjId.ShotgunAmmo => TR1RMemoryMap.InvItemRelIndex.ShotgunAmmo,
        TR1RMemoryMap.InvObjId.SmallMedipack => TR1RMemoryMap.InvItemRelIndex.SmallMedipack,
        TR1RMemoryMap.InvObjId.LargeMedipack => TR1RMemoryMap.InvItemRelIndex.LargeMedipack,
        TR1RMemoryMap.InvObjId.Pistols => TR1RMemoryMap.InvItemRelIndex.Pistols,
        TR1RMemoryMap.InvObjId.Shotgun => TR1RMemoryMap.InvItemRelIndex.Shotgun,
        TR1RMemoryMap.InvObjId.MagnumAmmo => TR1RMemoryMap.InvItemRelIndex.MagnumAmmo,
        TR1RMemoryMap.InvObjId.Compass => TR1RMemoryMap.InvItemRelIndex.Compass,
        TR1RMemoryMap.InvObjId.Uzis => TR1RMemoryMap.InvItemRelIndex.Uzis,
        TR1RMemoryMap.InvObjId.UziAmmo => TR1RMemoryMap.InvItemRelIndex.UziAmmo,
        TR1RMemoryMap.InvObjId.Magnums => TR1RMemoryMap.InvItemRelIndex.Magnums,
        TR1RMemoryMap.InvObjId.Key1 => TR1RMemoryMap.InvItemRelIndex.Key1,
        TR1RMemoryMap.InvObjId.Key2 => TR1RMemoryMap.InvItemRelIndex.Key2,
        TR1RMemoryMap.InvObjId.Key3 => TR1RMemoryMap.InvItemRelIndex.Key3,
        // Key4 handled separately (non-stride)
        TR1RMemoryMap.InvObjId.Puzzle1 => TR1RMemoryMap.InvItemRelIndex.Puzzle1,
        TR1RMemoryMap.InvObjId.Puzzle2 => TR1RMemoryMap.InvItemRelIndex.Puzzle2,
        TR1RMemoryMap.InvObjId.Puzzle3 => TR1RMemoryMap.InvItemRelIndex.Puzzle3,
        TR1RMemoryMap.InvObjId.Puzzle4 => TR1RMemoryMap.InvItemRelIndex.Puzzle4,
        TR1RMemoryMap.InvObjId.Scion => TR1RMemoryMap.InvItemRelIndex.Scion,
        _ => null,
    };
}
