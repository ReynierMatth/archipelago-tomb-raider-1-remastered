namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// TR2 implementation of IGameMemoryMap. Delegates to TR2RMemoryMap static constants.
/// Key differences from TR1:
/// - HP at static address (not pointer chain)
/// - Secrets are a counter (not bitmask)
/// - Some inventory items NOT stride-aligned (use NonStrideByteOffsets)
/// </summary>
public class TR2GameMemoryMap : IGameMemoryMap
{
    // --- Identity ---
    public string GameKey => "tr2";
    public string ModuleName => TR1RMemoryMap.TR2ModuleName;

    public IntPtr GetDllBase(ProcessMemory memory) => memory.Tomb2Base;

    // --- Level & Game State ---
    public int LevelId => TR2RMemoryMap.LevelId;
    public int LevelCompleted => TR2RMemoryMap.LevelCompleted;
    public int IsInGameScene => TR2RMemoryMap.IsInGameScene;
    public int BinaryTick => TR2RMemoryMap.BinaryTick;
    public int ActionKeys => TR2RMemoryMap.ActionKeys;
    public int Level_Home => TR2RMemoryMap.Level_Home;
    public int Level_MainMenu => TR2RMemoryMap.Level_MainMenu;

    // --- Entity Array ---
    public int EntitiesPointer => TR2RMemoryMap.EntitiesPointer;
    public int EntitiesCount => TR2RMemoryMap.EntitiesCount;
    public int EntitySize => TR2RMemoryMap.EntitySize;
    public int Item_ObjectId => TR2RMemoryMap.Item_ObjectId;
    public int Item_Flags => TR2RMemoryMap.Item_Flags;
    public int Item_HitPoints => TR2RMemoryMap.Item_HitPoints;
    public int Item_PosX => TR2RMemoryMap.Item_PosX;
    public int Item_PosY => TR2RMemoryMap.Item_PosY;
    public int Item_PosZ => TR2RMemoryMap.Item_PosZ;
    public int Item_RoomNum => TR2RMemoryMap.Item_RoomNum;

    // --- Inventory Rings ---
    public int MainRingCount => TR2RMemoryMap.MainRingCount;
    public int MainRingItems => TR2RMemoryMap.MainRingItems;
    public int MainRingQtys => TR2RMemoryMap.MainRingQtys;
    public int KeysRingCount => TR2RMemoryMap.KeysRingCount;
    public int KeysRingItems => TR2RMemoryMap.KeysRingItems;
    public int KeysRingQtys => TR2RMemoryMap.KeysRingQtys;
    public int MaxRingItems => TR2RMemoryMap.MaxRingItems;
    public int InventoryItemStride => TR2RMemoryMap.InventoryItemStride;
    public int InvItem_ObjectId => TR2RMemoryMap.InvItem_ObjectId;

    // --- Anchor ---
    public int AnchorObjId => TR2RMemoryMap.InvObjId.Statistiques;

    // --- WSB ---
    public int WorldStateBackup => TR2RMemoryMap.WorldStateBackup;
    public int WorldStateBackupSize => TR2RMemoryMap.WorldStateBackupSize;
    public int WSB_SaveCounter => TR2RMemoryMap.WSB_SaveCounter;

    // --- Health ---
    public short MaxHealth => TR2RMemoryMap.MaxHealth;
    public short MinHealth => TR2RMemoryMap.MinHealth;

    // --- Dictionaries ---
    public Dictionary<int, string> LevelNames => TR2RMemoryMap.LevelNames;
    public Dictionary<int, string> InvObjIdNames => TR2RMemoryMap.InvObjIdNames;

    // =============== BEHAVIORAL METHODS ===============

    public short ReadHealth(ProcessMemory memory, IntPtr dllBase)
    {
        // TR2: static address, Int32 (not pointer chain)
        int hp = memory.ReadInt32(dllBase + TR2RMemoryMap.LaraHP);
        return (short)Math.Clamp(hp, 0, MaxHealth);
    }

    public ushort ReadSecrets(ProcessMemory memory, IntPtr dllBase)
    {
        // TR2: counter at direct offset (NOT in WSB)
        return memory.ReadUInt16(dllBase + TR2RMemoryMap.Runtime_SecretsFound);
    }

    public List<int> DetectNewSecrets(ushort previous, ushort current)
    {
        // TR2: counter — each increment = one new secret found
        var result = new List<int>();
        if (current > previous)
        {
            for (int i = previous; i < current; i++)
                result.Add(i);
        }
        return result;
    }

    public int ToLocationMapperIndex(int runtimeLevelId) =>
        TR2RMemoryMap.ToLocationMapperIndex(runtimeLevelId);

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

        // Check non-stride byte offsets first (AutoPistols, Uzis, UziAmmo, Flares, Puzzle1)
        if (TR2RMemoryMap.NonStrideByteOffsets.TryGetValue(invObjId, out int byteOffset))
            return anchorPtr + byteOffset;

        // Stride-aligned items
        int? relIdx = ObjIdToRelIdx(invObjId);
        if (relIdx == null) return IntPtr.Zero;

        return anchorPtr + relIdx.Value * InventoryItemStride;
    }

    public void WriteHealth(ProcessMemory memory, IntPtr dllBase, short health)
    {
        memory.Write(dllBase + TR2RMemoryMap.LaraHP, (int)health);
    }

    public bool IsKeyItem(int invObjId) =>
        invObjId is (>= 0xB2 and <= 0xB5)  // Puzzle1-4
            or (>= 0xC5 and <= 0xC8);       // Key1-4

    public int SlotTypeToInvObjId(string slotType) => slotType switch
    {
        "K1" => TR2RMemoryMap.InvObjId.Key1,
        "K2" => TR2RMemoryMap.InvObjId.Key2,
        "K3" => TR2RMemoryMap.InvObjId.Key3,
        "K4" => TR2RMemoryMap.InvObjId.Key4,
        "P1" => TR2RMemoryMap.InvObjId.Puzzle1,
        "P2" => TR2RMemoryMap.InvObjId.Puzzle2,
        "P4" => TR2RMemoryMap.InvObjId.Puzzle4,
        _ => -1,
    };

    // =============== ITEM INJECTION RECIPES ===============

    // AP item offsets from ItemBaseId (verified from tr2r_data.json itemDefinitions)
    private const int _shotgunOffset = 136;       // id=870136
    private const int _autoPistolsOffset = 137;   // id=870137
    private const int _uzisOffset = 138;           // id=870138
    private const int _harpoonGunOffset = 139;     // id=870139
    private const int _m16Offset = 140;            // id=870140
    private const int _grenadeLauncherOffset = 141; // id=870141
    private const int _shotgunAmmoOffset = 143;    // id=870143
    private const int _autoPistolAmmoOffset = 144; // id=870144
    private const int _uziAmmoOffset = 145;        // id=870145
    private const int _harpoonAmmoOffset = 146;    // id=870146
    private const int _m16AmmoOffset = 147;        // id=870147
    private const int _grenadeAmmoOffset = 148;    // id=870148
    private const int _smallMedOffset = 149;       // id=870149
    private const int _largeMedOffset = 150;       // id=870150
    private const int _flaresOffset = 151;         // id=870151

    public WeaponRecipe? GetWeaponRecipe(long apItemId, int itemBaseId)
    {
        int offset = (int)apItemId - itemBaseId;
        return offset switch
        {
            _shotgunOffset => new WeaponRecipe
            {
                WeaponObjId = TR2RMemoryMap.InvObjId.Shotgun, WeaponFlag = TR2RMemoryMap.Weapon_Shotgun,
                AmmoObjId = TR2RMemoryMap.InvObjId.ShotgunAmmo,
                LaraAmmoOffset = TR2RMemoryMap.Lara_ShotgunAmmo,
                StartingAmmo = 2 * TR2RMemoryMap.ShotgunAmmoMultiplier, Name = "Shotgun"
            },
            _autoPistolsOffset => new WeaponRecipe
            {
                WeaponObjId = TR2RMemoryMap.InvObjId.AutoPistols, WeaponFlag = TR2RMemoryMap.Weapon_AutoPistols,
                AmmoObjId = TR2RMemoryMap.InvObjId.AutoPistolAmmo,
                LaraAmmoOffset = TR2RMemoryMap.Lara_AutoPistolAmmo,
                StartingAmmo = 50, Name = "Auto Pistols"
            },
            _uzisOffset => new WeaponRecipe
            {
                WeaponObjId = TR2RMemoryMap.InvObjId.Uzis, WeaponFlag = TR2RMemoryMap.Weapon_Uzis,
                AmmoObjId = TR2RMemoryMap.InvObjId.UziAmmo,
                LaraAmmoOffset = TR2RMemoryMap.Lara_UziAmmo,
                StartingAmmo = 100, Name = "Uzis"
            },
            _harpoonGunOffset => new WeaponRecipe
            {
                WeaponObjId = TR2RMemoryMap.InvObjId.HarpoonGun, WeaponFlag = TR2RMemoryMap.Weapon_HarpoonGun,
                AmmoObjId = TR2RMemoryMap.InvObjId.HarpoonAmmo,
                LaraAmmoOffset = TR2RMemoryMap.Lara_HarpoonAmmo,
                StartingAmmo = 3, Name = "Harpoon Gun"
            },
            _m16Offset => new WeaponRecipe
            {
                WeaponObjId = TR2RMemoryMap.InvObjId.M16, WeaponFlag = TR2RMemoryMap.Weapon_M16,
                AmmoObjId = TR2RMemoryMap.InvObjId.M16Ammo,
                LaraAmmoOffset = TR2RMemoryMap.Lara_M16Ammo,
                StartingAmmo = 40, Name = "M16"
            },
            _grenadeLauncherOffset => new WeaponRecipe
            {
                WeaponObjId = TR2RMemoryMap.InvObjId.GrenadeLauncher, WeaponFlag = TR2RMemoryMap.Weapon_GrenadeLauncher,
                AmmoObjId = TR2RMemoryMap.InvObjId.GrenadeAmmo,
                LaraAmmoOffset = TR2RMemoryMap.Lara_GrenadeAmmo,
                StartingAmmo = 2, Name = "Grenade Launcher"
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
                WeaponObjId = TR2RMemoryMap.InvObjId.Shotgun, AmmoObjId = TR2RMemoryMap.InvObjId.ShotgunAmmo,
                LaraAmmoOffset = TR2RMemoryMap.Lara_ShotgunAmmo, Amount = 2 * TR2RMemoryMap.ShotgunAmmoMultiplier, Name = "Shotgun Ammo"
            },
            _autoPistolAmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR2RMemoryMap.InvObjId.AutoPistols, AmmoObjId = TR2RMemoryMap.InvObjId.AutoPistolAmmo,
                LaraAmmoOffset = TR2RMemoryMap.Lara_AutoPistolAmmo, Amount = 50, Name = "Auto Pistol Ammo"
            },
            _uziAmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR2RMemoryMap.InvObjId.Uzis, AmmoObjId = TR2RMemoryMap.InvObjId.UziAmmo,
                LaraAmmoOffset = TR2RMemoryMap.Lara_UziAmmo, Amount = 100, Name = "Uzi Ammo"
            },
            _harpoonAmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR2RMemoryMap.InvObjId.HarpoonGun, AmmoObjId = TR2RMemoryMap.InvObjId.HarpoonAmmo,
                LaraAmmoOffset = TR2RMemoryMap.Lara_HarpoonAmmo, Amount = 3, Name = "Harpoon Ammo"
            },
            _m16AmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR2RMemoryMap.InvObjId.M16, AmmoObjId = TR2RMemoryMap.InvObjId.M16Ammo,
                LaraAmmoOffset = TR2RMemoryMap.Lara_M16Ammo, Amount = 40, Name = "M16 Ammo"
            },
            _grenadeAmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR2RMemoryMap.InvObjId.GrenadeLauncher, AmmoObjId = TR2RMemoryMap.InvObjId.GrenadeAmmo,
                LaraAmmoOffset = TR2RMemoryMap.Lara_GrenadeAmmo, Amount = 2, Name = "Grenade Ammo"
            },
            _ => null,
        };
    }

    public int GetMedipackObjId(long apItemId, int itemBaseId)
    {
        int offset = (int)apItemId - itemBaseId;
        return offset switch
        {
            _smallMedOffset => TR2RMemoryMap.InvObjId.SmallMedipack,
            _largeMedOffset => TR2RMemoryMap.InvObjId.LargeMedipack,
            _flaresOffset => TR2RMemoryMap.InvObjId.Flares,
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

    /// <summary>
    /// Maps InvObjId to relative stride index from Statistiques (anchor).
    /// Only for stride-aligned items; non-stride items use NonStrideByteOffsets.
    /// </summary>
    private static int? ObjIdToRelIdx(int invObjId) => invObjId switch
    {
        TR2RMemoryMap.InvObjId.Pistols => -6,
        TR2RMemoryMap.InvObjId.Shotgun => -4,
        TR2RMemoryMap.InvObjId.HarpoonGun => -16,
        TR2RMemoryMap.InvObjId.M16 => -9,
        TR2RMemoryMap.InvObjId.GrenadeLauncher => -20,
        TR2RMemoryMap.InvObjId.ShotgunAmmo => -18,
        TR2RMemoryMap.InvObjId.AutoPistolAmmo => -2,
        TR2RMemoryMap.InvObjId.HarpoonAmmo => -5,
        TR2RMemoryMap.InvObjId.M16Ammo => -11,
        TR2RMemoryMap.InvObjId.GrenadeAmmo => -7,
        TR2RMemoryMap.InvObjId.SmallMedipack => -15,
        TR2RMemoryMap.InvObjId.LargeMedipack => -10,
        TR2RMemoryMap.InvObjId.Statistiques => 0,
        // Keys Ring stride-aligned
        TR2RMemoryMap.InvObjId.Puzzle2 => -13,
        TR2RMemoryMap.InvObjId.Puzzle4 => -14,
        TR2RMemoryMap.InvObjId.Key1 => -8,
        TR2RMemoryMap.InvObjId.Key2 => -17,
        TR2RMemoryMap.InvObjId.Key3 => 1,
        TR2RMemoryMap.InvObjId.Key4 => -22,
        _ => null,
    };
}
