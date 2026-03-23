namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// TR3 implementation of IGameMemoryMap. Delegates to TR3RMemoryMap static constants.
/// Key similarities with TR2: HP static, secrets counter, some items NOT stride-aligned.
/// </summary>
public class TR3GameMemoryMap : IGameMemoryMap
{
    public string GameKey => "tr3";
    public string ModuleName => TR1RMemoryMap.TR3ModuleName;

    public IntPtr GetDllBase(ProcessMemory memory) => memory.Tomb3Base;

    // --- Level & Game State ---
    public int LevelId => TR3RMemoryMap.LevelId;
    public int LevelCompleted => TR3RMemoryMap.LevelCompleted;
    public int IsInGameScene => TR3RMemoryMap.IsInGameScene;
    public int BinaryTick => TR3RMemoryMap.BinaryTick;
    public int ActionKeys => TR3RMemoryMap.ActionKeys;
    public int Level_Home => TR3RMemoryMap.Level_Home;
    public int Level_MainMenu => TR3RMemoryMap.Level_MainMenu;

    // --- Entity Array ---
    public int EntitiesPointer => TR3RMemoryMap.EntitiesPointer;
    public int EntitiesCount => TR3RMemoryMap.EntitiesCount;
    public int EntitySize => TR3RMemoryMap.EntitySize;
    public int Item_ObjectId => TR3RMemoryMap.Item_ObjectId;
    public int Item_Flags => TR3RMemoryMap.Item_Flags;
    public int Item_HitPoints => TR3RMemoryMap.Item_HitPoints;
    public int Item_PosX => TR3RMemoryMap.Item_PosX;
    public int Item_PosY => TR3RMemoryMap.Item_PosY;
    public int Item_PosZ => TR3RMemoryMap.Item_PosZ;
    public int Item_RoomNum => TR3RMemoryMap.Item_RoomNum;

    // --- Inventory Rings ---
    public int MainRingCount => TR3RMemoryMap.MainRingCount;
    public int MainRingItems => TR3RMemoryMap.MainRingItems;
    public int MainRingQtys => TR3RMemoryMap.MainRingQtys;
    public int KeysRingCount => TR3RMemoryMap.KeysRingCount;
    public int KeysRingItems => TR3RMemoryMap.KeysRingItems;
    public int KeysRingQtys => TR3RMemoryMap.KeysRingQtys;
    public int MaxRingItems => TR3RMemoryMap.MaxRingItems;
    public int InventoryItemStride => TR3RMemoryMap.InventoryItemStride;
    public int InvItem_ObjectId => TR3RMemoryMap.InvItem_ObjectId;

    // --- Anchor ---
    public int AnchorObjId => TR3RMemoryMap.InvObjId.Statistiques;

    // --- WSB ---
    public int WorldStateBackup => TR3RMemoryMap.WorldStateBackup;
    public int WorldStateBackupSize => TR3RMemoryMap.WorldStateBackupSize;
    public int WSB_SaveCounter => TR3RMemoryMap.WSB_SaveCounter;

    // --- Health ---
    public short MaxHealth => TR3RMemoryMap.MaxHealth;
    public short MinHealth => TR3RMemoryMap.MinHealth;

    // --- Dictionaries ---
    public Dictionary<int, string> LevelNames => TR3RMemoryMap.LevelNames;
    public Dictionary<int, string> InvObjIdNames => TR3RMemoryMap.InvObjIdNames;

    // =============== BEHAVIORAL METHODS ===============

    public short ReadHealth(ProcessMemory memory, IntPtr dllBase)
    {
        int hp = memory.ReadInt32(dllBase + TR3RMemoryMap.LaraHP);
        return (short)Math.Clamp(hp, 0, MaxHealth);
    }

    public void WriteHealth(ProcessMemory memory, IntPtr dllBase, short health)
    {
        memory.Write(dllBase + TR3RMemoryMap.LaraHP, (int)health);
    }

    public ushort ReadSecrets(ProcessMemory memory, IntPtr dllBase)
    {
        // TR3: counter at direct offset (like TR2)
        return (ushort)memory.ReadByte(dllBase + TR3RMemoryMap.Runtime_SecretsFound);
    }

    public List<int> DetectNewSecrets(ushort previous, ushort current)
    {
        // Counter — each increment = one new secret
        var result = new List<int>();
        if (current > previous)
        {
            for (int i = previous; i < current; i++)
                result.Add(i);
        }
        return result;
    }

    public int ToLocationMapperIndex(int runtimeLevelId) =>
        TR3RMemoryMap.ToLocationMapperIndex(runtimeLevelId);

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

        // Check non-stride byte offsets first
        if (TR3RMemoryMap.NonStrideByteOffsets.TryGetValue(invObjId, out int byteOffset))
            return anchorPtr + byteOffset;

        // Stride-aligned items
        int? relIdx = ObjIdToRelIdx(invObjId);
        if (relIdx == null) return IntPtr.Zero;

        return anchorPtr + relIdx.Value * InventoryItemStride;
    }

    public bool IsKeyItem(int invObjId) =>
        invObjId is 0xCC                     // Crystal
            or (>= 0xD1 and <= 0xD4)        // Puzzle1-4
            or (>= 0xE4 and <= 0xE7)        // Key1-4
            or (>= 0xF4 and <= 0xF6);       // Pickup1-3 (Infada, Eye of Isis, Element 115)

    public int SlotTypeToInvObjId(string slotType) => slotType switch
    {
        "K1" => 0xE4,
        "K2" => 0xE5,
        "K3" => 0xE6,
        "K4" => 0xE7,
        "P1" => 0xD1,
        "P2" => 0xD2,
        "P3" => 0xD3,
        "P4" => 0xD4,
        // TR3 special artifact pickups
        "Infada" => 0xF4,
        "EyeOfIsis" => 0xF5,
        "Element115" => 0xF6,
        _ => -1,
    };

    // =============== ITEM INJECTION RECIPES ===============

    // AP item offsets from ItemBaseId (verified from tr3r_data.json)
    private const int _shotgunOffset = 161;
    private const int _deagleOffset = 162;
    private const int _uzisOffset = 163;
    private const int _harpoonGunOffset = 164;
    private const int _mp5Offset = 165;
    private const int _rocketLauncherOffset = 166;
    private const int _grenadeLauncherOffset = 167;
    private const int _shotgunAmmoOffset = 169;
    private const int _deagleAmmoOffset = 170;
    private const int _uziAmmoOffset = 171;
    private const int _harpoonAmmoOffset = 172;
    private const int _mp5AmmoOffset = 173;
    private const int _rocketAmmoOffset = 174;
    private const int _grenadeAmmoOffset = 175;
    private const int _smallMedOffset = 176;
    private const int _largeMedOffset = 177;
    private const int _flaresOffset = 178;

    public WeaponRecipe? GetWeaponRecipe(long apItemId, int itemBaseId)
    {
        int offset = (int)apItemId - itemBaseId;
        return offset switch
        {
            _shotgunOffset => new WeaponRecipe
            {
                WeaponObjId = TR3RMemoryMap.InvObjId.Shotgun, WeaponFlag = TR3RMemoryMap.Weapon_Shotgun,
                AmmoObjId = TR3RMemoryMap.InvObjId.ShotgunAmmo,
                LaraAmmoOffset = TR3RMemoryMap.Lara_ShotgunAmmo,
                StartingAmmo = 2 * TR3RMemoryMap.ShotgunAmmoMultiplier, Name = "Shotgun"
            },
            _deagleOffset => new WeaponRecipe
            {
                WeaponObjId = TR3RMemoryMap.InvObjId.DesertEagle, WeaponFlag = TR3RMemoryMap.Weapon_DesertEagle,
                AmmoObjId = TR3RMemoryMap.InvObjId.DeagleAmmo,
                LaraAmmoOffset = TR3RMemoryMap.Lara_DeagleAmmo,
                StartingAmmo = 50, Name = "Desert Eagle"
            },
            _uzisOffset => new WeaponRecipe
            {
                WeaponObjId = TR3RMemoryMap.InvObjId.Uzis, WeaponFlag = TR3RMemoryMap.Weapon_Uzis,
                AmmoObjId = TR3RMemoryMap.InvObjId.UziAmmo,
                LaraAmmoOffset = TR3RMemoryMap.Lara_UziAmmo,
                StartingAmmo = 100, Name = "Uzis"
            },
            _harpoonGunOffset => new WeaponRecipe
            {
                WeaponObjId = TR3RMemoryMap.InvObjId.HarpoonGun, WeaponFlag = 0, // Harpoon tracked separately
                AmmoObjId = TR3RMemoryMap.InvObjId.Harpoons,
                LaraAmmoOffset = TR3RMemoryMap.Lara_HarpoonAmmo,
                StartingAmmo = 3, Name = "Harpoon Gun"
            },
            _mp5Offset => new WeaponRecipe
            {
                WeaponObjId = TR3RMemoryMap.InvObjId.MP5, WeaponFlag = TR3RMemoryMap.Weapon_MP5,
                AmmoObjId = TR3RMemoryMap.InvObjId.MP5Ammo,
                LaraAmmoOffset = TR3RMemoryMap.Lara_MP5Ammo,
                StartingAmmo = 40, Name = "MP5"
            },
            _rocketLauncherOffset => new WeaponRecipe
            {
                WeaponObjId = TR3RMemoryMap.InvObjId.RocketLauncher, WeaponFlag = TR3RMemoryMap.Weapon_RocketLauncher,
                AmmoObjId = TR3RMemoryMap.InvObjId.Rockets,
                LaraAmmoOffset = TR3RMemoryMap.Lara_RocketAmmo,
                StartingAmmo = 2, Name = "Rocket Launcher"
            },
            _grenadeLauncherOffset => new WeaponRecipe
            {
                WeaponObjId = TR3RMemoryMap.InvObjId.GrenadeLauncher, WeaponFlag = TR3RMemoryMap.Weapon_GrenadeLauncher,
                AmmoObjId = TR3RMemoryMap.InvObjId.Grenades,
                LaraAmmoOffset = TR3RMemoryMap.Lara_GrenadeAmmo,
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
                WeaponObjId = TR3RMemoryMap.InvObjId.Shotgun, AmmoObjId = TR3RMemoryMap.InvObjId.ShotgunAmmo,
                LaraAmmoOffset = TR3RMemoryMap.Lara_ShotgunAmmo, Amount = 2 * TR3RMemoryMap.ShotgunAmmoMultiplier, Name = "Shotgun Shells"
            },
            _deagleAmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR3RMemoryMap.InvObjId.DesertEagle, AmmoObjId = TR3RMemoryMap.InvObjId.DeagleAmmo,
                LaraAmmoOffset = TR3RMemoryMap.Lara_DeagleAmmo, Amount = 50, Name = "Desert Eagle Clips"
            },
            _uziAmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR3RMemoryMap.InvObjId.Uzis, AmmoObjId = TR3RMemoryMap.InvObjId.UziAmmo,
                LaraAmmoOffset = TR3RMemoryMap.Lara_UziAmmo, Amount = 100, Name = "Uzi Clips"
            },
            _harpoonAmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR3RMemoryMap.InvObjId.HarpoonGun, AmmoObjId = TR3RMemoryMap.InvObjId.Harpoons,
                LaraAmmoOffset = TR3RMemoryMap.Lara_HarpoonAmmo, Amount = 3, Name = "Harpoons"
            },
            _mp5AmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR3RMemoryMap.InvObjId.MP5, AmmoObjId = TR3RMemoryMap.InvObjId.MP5Ammo,
                LaraAmmoOffset = TR3RMemoryMap.Lara_MP5Ammo, Amount = 40, Name = "MP5 Clips"
            },
            _rocketAmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR3RMemoryMap.InvObjId.RocketLauncher, AmmoObjId = TR3RMemoryMap.InvObjId.Rockets,
                LaraAmmoOffset = TR3RMemoryMap.Lara_RocketAmmo, Amount = 2, Name = "Rockets"
            },
            _grenadeAmmoOffset => new AmmoRecipe
            {
                WeaponObjId = TR3RMemoryMap.InvObjId.GrenadeLauncher, AmmoObjId = TR3RMemoryMap.InvObjId.Grenades,
                LaraAmmoOffset = TR3RMemoryMap.Lara_GrenadeAmmo, Amount = 2, Name = "Grenades"
            },
            _ => null,
        };
    }

    public int GetMedipackObjId(long apItemId, int itemBaseId)
    {
        int offset = (int)apItemId - itemBaseId;
        return offset switch
        {
            _smallMedOffset => TR3RMemoryMap.InvObjId.SmallMedipack,
            _largeMedOffset => TR3RMemoryMap.InvObjId.LargeMedipack,
            _flaresOffset => TR3RMemoryMap.InvObjId.Flares,
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
        TR3RMemoryMap.InvObjId.Statistiques => 0,
        TR3RMemoryMap.InvObjId.Pistols => -10,
        TR3RMemoryMap.InvObjId.Shotgun => -6,
        // DesertEagle -> NonStrideByteOffsets
        // Uzis -> NonStrideByteOffsets
        TR3RMemoryMap.InvObjId.HarpoonGun => -20,
        TR3RMemoryMap.InvObjId.MP5 => -13,
        TR3RMemoryMap.InvObjId.RocketLauncher => -26,
        // GrenadeLauncher -> NonStrideByteOffsets
        TR3RMemoryMap.InvObjId.ShotgunAmmo => -22,
        TR3RMemoryMap.InvObjId.DeagleAmmo => -2,
        // UziAmmo -> NonStrideByteOffsets
        TR3RMemoryMap.InvObjId.Harpoons => -8,
        TR3RMemoryMap.InvObjId.MP5Ammo => -15,
        TR3RMemoryMap.InvObjId.Rockets => -11,
        TR3RMemoryMap.InvObjId.Grenades => -4,
        TR3RMemoryMap.InvObjId.SmallMedipack => -19,
        TR3RMemoryMap.InvObjId.LargeMedipack => -14,
        // Flares -> NonStrideByteOffsets
        TR3RMemoryMap.InvObjId.Crystal => -9,
        // Puzzle1 -> NonStrideByteOffsets
        TR3RMemoryMap.InvObjId.Puzzle2 => -17,
        TR3RMemoryMap.InvObjId.Puzzle3 => -1,
        TR3RMemoryMap.InvObjId.Puzzle4 => -18,
        TR3RMemoryMap.InvObjId.Key1 => -12,
        TR3RMemoryMap.InvObjId.Key2 => -21,
        TR3RMemoryMap.InvObjId.Key3 => 1,
        TR3RMemoryMap.InvObjId.Key4 => -28,
        TR3RMemoryMap.InvObjId.Pickup1 => -29,  // Infada Stone
        TR3RMemoryMap.InvObjId.Pickup2 => -7,   // Eye of Isis
        TR3RMemoryMap.InvObjId.Pickup3 => -25,  // Element 115
        _ => null,
    };
}
