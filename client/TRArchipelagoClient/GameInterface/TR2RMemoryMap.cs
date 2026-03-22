namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Memory map for TR2 Remastered (Patch 4.1 "Golden Pistols").
///
/// Architecture: tomb123.exe loads tomb2.dll for TR2 gameplay.
/// All TR2 game state lives inside tomb2.dll.
///
/// Sources:
///   - Burns Multiplayer Mod (patch4.1/tr2.js)
///   - TRR-SaveMaster (save file offsets)
///   - FearLess Revolution CE tables
///   - TR2Main decompilation (struct layout reference)
///
/// WARNING: Offsets are specific to Patch 4.1. They change between patches.
/// </summary>
public static class TR2RMemoryMap
{
    // =================================================================
    // TR2 RUNTIME OFFSETS (tomb2.dll, Patch 4.1)
    // All offsets relative to tomb2.dll base address.
    // =================================================================

    // ----- Lara HP -----
    // Unlike TR1 (pointer chain: LaraBase → deref → +0x26), TR2 stores HP at a
    // static address in tomb2.dll. Found via CE unknown-value scan.

    /// <summary>Lara's HP. Int32 (not Int16 like TR1). Static address, no dereference needed.
    /// Range 0-1000. Read directly: memory.ReadInt32(tomb2Base + LaraHP).</summary>
    public const int LaraHP = 0x154E58;

    /// <summary>Pointer to Lara's ITEM struct. Read as Int64, then dereference.
    /// WARNING: not yet re-verified after game update.</summary>
    public const int LaraBase = 0x346170;

    /// <summary>Lara's entity index in the entities array. Int16.</summary>
    public const int LaraId = 0x345fc0;

    // ----- ITEM Struct Offsets (shared with TR1/TR3 remastered) -----
    // Entity size = 0xE50 bytes (identical across all 3 games).

    public const int EntitySize = 0xE50;
    public const int Item_ObjectId = 0x0E;
    public const int Item_AnimNum = 0x18;
    public const int Item_FrameNum = 0x1A;
    public const int Item_RoomNum = 0x1C;
    public const int Item_Speed = 0x22;
    public const int Item_FallSpeed = 0x24;
    public const int Item_HitPoints = 0x26;
    public const int Item_PosX = 0x58;
    public const int Item_PosY = 0x5C;
    public const int Item_PosZ = 0x60;
    public const int Item_Flags = 0x1E4;

    // ----- LARA_INFO Static Variables (direct offsets from tomb2.dll) -----

    /// <summary>Equipped weapon type. Int32.</summary>
    public const int LaraGunType = 0x345fc4;

    /// <summary>Oxygen / air timer. Int16.</summary>
    public const int LaraOxygen = 0x345fd6;

    /// <summary>Gun state flags. UInt16.</summary>
    public const int LaraGunFlags = 0x346000;

    /// <summary>Pointer to currently aimed enemy. UInt64.</summary>
    public const int LaraAimingEnemy = 0x3460b0;

    /// <summary>Water/climb state of Lara. Int16.</summary>
    public const int LaraClimbState = 0x345fce;

    /// <summary>Water state of current room. Int16.</summary>
    public const int LaraRoomType = 0x345fcc;

    /// <summary>Vehicle entity ID (-1 = none). Int16.</summary>
    public const int LaraVehicleId = 0x345fe8;

    // ----- LARA_INFO Ammo (live runtime) -----
    // TODO: Find via Cheat Engine. TR2 has 7 weapon types:
    //   Pistols, Auto Pistols, Uzis, Shotgun, M16, Grenade Launcher, Harpoon Gun
    // Expected: Int32 fields with 8-byte stride (AMMO_INFO structs)
    // Starting point: LaraId + ~0x148 (by analogy with TR1)

    // public const int Lara_AutoPistolAmmo = ???;
    // public const int Lara_UziAmmo = ???;
    // public const int Lara_ShotgunAmmo = ???;
    // public const int Lara_M16Ammo = ???;
    // public const int Lara_GrenadeAmmo = ???;
    // public const int Lara_HarpoonAmmo = ???;

    public const int ShotgunAmmoMultiplier = 6;

    // ----- Level & Game State -----

    /// <summary>Current TR2 level ID (0=Home, 1=Great Wall, ..., 63=Menu). Int32.</summary>
    public const int LevelId = 0x157168;

    /// <summary>Set to 1 when current level is completed. Int32.</summary>
    public const int LevelCompleted = 0x15CB74;

    /// <summary>Frame tick. Int8.</summary>
    public const int BinaryTick = 0x1330c8;

    /// <summary>Greater than 0 = in gameplay, 0 or less = in menu/loading. Int32.</summary>
    /// <summary>Greater than 0 = in gameplay, 0 = menu/inventory/loading.
    /// NOTE: also returns 1 when passport is open from main menu.
    /// Not a perfect "Lara controllable" flag — use with save counter + settle period.</summary>
    public const int IsInGameScene = 0x157210;

    /// <summary>Menu cursor position. UInt16.</summary>
    public const int MenuSelection = 0x113f14;

    /// <summary>Menu state machine. UInt16.</summary>
    public const int MenuState = 0x4fdf02;

    /// <summary>1 = New Game+ mode. UInt8.</summary>
    public const int NewGamePlus = 0x4fa606;

    /// <summary>Input bitfield (action keys). UInt32.</summary>
    public const int ActionKeys = 0x3fd320;

    // ----- Entity Array -----

    /// <summary>Pointer to the entity array in heap memory. Read as Int64.</summary>
    public const int EntitiesPointer = 0x5285E0;

    /// <summary>Number of entities in the current level. Int16.</summary>
    public const int EntitiesCount = 0x3FD1B4;

    // ----- Rooms -----

    public const int RoomsPointer = 0x427360;
    public const int RoomsCount = 0x3fd1b0;

    // ----- Inventory Rings -----
    // Same ring system as TR1: count (Int16) + items[] (Int64 pointers) + qtys[] (Int16)
    // Found via Cheat Engine (Patch 4.1).

    public const int MainRingCount = 0x137EDC;
    public const int MainRingItems = 0x1525F0;
    public const int MainRingQtys = 0x1526A8;
    public const int KeysRingCount = 0x1571B0;
    public const int KeysRingItems = 0x1528F0;
    public const int KeysRingQtys = 0x1528C0;
    public const int MaxRingItems = 24;
    public const int InventoryItemStride = 0xCD0;
    public const int InvItem_ObjectId = 0x08;

    /// <summary>
    /// TR2 inventory object IDs (different from TR1).
    /// Discovered via MainRingItems pointer dereference + 0x08.
    /// </summary>
    public static class InvObjId
    {
        public const int Statistiques = 0x79;
        public const int Pistols = 0x9D;
        public const int Shotgun = 0x9E;
        public const int AutoPistols = 0x9F;
        public const int Uzis = 0xA0;
        public const int HarpoonGun = 0xA1;
        public const int M16 = 0xA2;
        public const int GrenadeLauncher = 0xA3;
        public const int ShotgunAmmo = 0xA5;     // relIdx=-18
        public const int AutoPistolAmmo = 0xA6;  // relIdx=-2
        public const int UziAmmo = 0xA7;         // NOT stride-aligned
        // public const int ShotgunAmmo was guessed 0xA8 but is actually 0xA5
        public const int HarpoonAmmo = 0xA8;      // relIdx=-5
        public const int M16Ammo = 0xA9;         // relIdx=-11
        public const int GrenadeAmmo = 0xAA;     // relIdx=-7

        // Keys Ring items — Key slots
        public const int Puzzle1 = 0xB2;         // confirmed, NOT stride-aligned +0x4450
        public const int Puzzle2 = 0xB3;         // confirmed, Prayer Wheel, N=-13
        // Puzzle3 (0xB4) NOT used in TR2 — displays "Select Level" glitch
        public const int Puzzle4 = 0xB5;         // confirmed, Seraph, N=-14
        public const int Key1 = 0xC5;            // confirmed, Guardhouse Key, N=-8
        public const int Key2 = 0xC6;            // confirmed, Rusty Key, N=-17
        public const int Key3 = 0xC7;            // confirmed, N=1
        public const int Key4 = 0xC8;            // confirmed, N=-22
        public const int SmallMedipack = 0xAB;
        public const int LargeMedipack = 0xAC;
        public const int Flares = 0xAD;
    }

    /// <summary>
    /// Maps TR2 inventory object_id values to human-readable names.
    /// </summary>
    public static readonly Dictionary<int, string> InvObjIdNames = new()
    {
        [InvObjId.Statistiques] = "Statistiques",
        [InvObjId.Pistols] = "Pistols",
        [InvObjId.Shotgun] = "Shotgun",
        [InvObjId.AutoPistols] = "Auto Pistols",
        [InvObjId.Uzis] = "Uzis",
        [InvObjId.HarpoonGun] = "Harpoon Gun",
        [InvObjId.M16] = "M16",
        [InvObjId.GrenadeLauncher] = "Grenade Launcher",
        [InvObjId.ShotgunAmmo] = "Shotgun Ammo",
        [InvObjId.AutoPistolAmmo] = "Auto Pistol Ammo",
        [InvObjId.UziAmmo] = "Uzi Ammo",
        [InvObjId.HarpoonAmmo] = "Harpoon Ammo",
        [InvObjId.M16Ammo] = "M16 Ammo",
        [InvObjId.GrenadeAmmo] = "Grenade Ammo",
        [InvObjId.Puzzle1] = "Puzzle 1",
        [InvObjId.Puzzle2] = "Puzzle 2",
        [InvObjId.Puzzle4] = "Puzzle 4 (Seraph)",
        [InvObjId.Key1] = "Key 1",
        [InvObjId.Key2] = "Key 2",
        [InvObjId.Key3] = "Key 3",
        [InvObjId.Key4] = "Key 4",
        [InvObjId.SmallMedipack] = "Small Medipack",
        [InvObjId.LargeMedipack] = "Large Medipack",
        [InvObjId.Flares] = "Flares",
    };

    /// <summary>
    /// Byte offsets from Statistiques INVENTORY_ITEM for items NOT stride-aligned.
    /// Usage: ptr = statsPtr + ByteOffset
    /// Discovered via CE ring scan.
    /// </summary>
    public static readonly Dictionary<int, int> NonStrideByteOffsets = new()
    {
        [InvObjId.AutoPistols] = 0x5DF0,
        [InvObjId.Uzis] = 0x2AB0,
        [InvObjId.UziAmmo] = 0x3780,
        [InvObjId.Flares] = 0x5120,
        [InvObjId.Puzzle1] = 0x4450,
    };

    // ----- WorldState Backup Buffer -----

    /// <summary>Save-format buffer in memory. Same 0x3800-byte layout as a save slot.</summary>
    public const int WorldStateBackup = 0x528600;

    public const int WorldStateBackupSize = 0x3800;

    /// <summary>Save counter in WSB (Save_Number minus SlotStatus offset).</summary>
    public const int WSB_SaveCounter = 0x008;

    // ----- Level IDs (runtime, 0-based) -----

    public const int Level_Home = 0;
    public const int Level_GreatWall = 1;
    public const int Level_Venice = 2;
    public const int Level_BartoliHideout = 3;
    public const int Level_OperaHouse = 4;
    public const int Level_OffshoreRig = 5;
    public const int Level_DivingArea = 6;
    public const int Level_40Fathoms = 7;
    public const int Level_MariaDoria = 8;
    public const int Level_LivingQuarters = 9;
    public const int Level_TheDeck = 10;
    public const int Level_TibetanFoothills = 11;
    public const int Level_BarkhangMonastery = 12;
    public const int Level_CatacombsOfTalion = 13;
    public const int Level_IcePalace = 14;
    public const int Level_TempleOfXian = 15;
    public const int Level_FloatingIslands = 16;
    public const int Level_DragonsLair = 17;
    public const int Level_HomeSweetHome = 18;
    // Golden Mask DLC
    public const int Level_TheColdWar = 19;
    public const int Level_FoolsGold = 20;
    public const int Level_FurnaceOfTheGods = 21;
    public const int Level_Kingdom = 22;
    public const int Level_NightmareInVegas = 23;
    public const int Level_MainMenu = 63;

    public static readonly Dictionary<int, string> LevelNames = new()
    {
        [0]  = "Lara's Home",
        [1]  = "The Great Wall",
        [2]  = "Venice",
        [3]  = "Bartoli's Hideout",
        [4]  = "Opera House",
        [5]  = "Offshore Rig",
        [6]  = "Diving Area",
        [7]  = "40 Fathoms",
        [8]  = "Wreck of the Maria Doria",
        [9]  = "Living Quarters",
        [10] = "The Deck",
        [11] = "Tibetan Foothills",
        [12] = "Barkhang Monastery",
        [13] = "Catacombs of the Talion",
        [14] = "Ice Palace",
        [15] = "Temple of Xian",
        [16] = "Floating Islands",
        [17] = "The Dragon's Lair",
        [18] = "Home Sweet Home",
        [19] = "The Cold War",
        [20] = "Fool's Gold",
        [21] = "Furnace of the Gods",
        [22] = "Kingdom",
        [23] = "Nightmare in Vegas",
        [63] = "Main Menu",
    };

    /// <summary>
    /// Converts runtime level ID (0-based, 0=Home) to LocationMapper index (0-based, 0=GreatWall).
    /// Returns -1 for non-game levels (Home, Menu).
    /// Main game: 1-18 -> 0-17, Golden Mask: 19-23 -> 18-22.
    /// </summary>
    public static int ToLocationMapperIndex(int runtimeLevelId) => runtimeLevelId switch
    {
        >= 1 and <= 23 => runtimeLevelId - 1,
        _ => -1,
    };

    // =================================================================
    // HEALTH CONSTANTS
    // =================================================================
    public const short MaxHealth = 1000;
    public const short MinHealth = 1;

    // =================================================================
    // SAVEGAME FILE OFFSETS (within savegame.dat)
    // TR2 save slots start at 0x72000 in the shared savegame.dat file.
    // =================================================================

    public const int SaveFileBaseOffset = 0x72000;
    public const int SaveFileMaxOffset = 0xE2000;
    public const int SaveSlotSize = 0x3800;
    public const int MaxSaveSlots = 32;

    public const int Save_SlotStatus = 0x004;
    public const int Save_GameMode = 0x008;
    public const int Save_Number = 0x00C;

    // Per-level ammo: base + (levelIndex * 0x30)
    // levelIndex is 1-based (1..23)
    public const int Save_AmmoStride = 0x30;
    public const int Save_AutoPistolAmmo_Base = 0x12;
    public const int Save_UziAmmo_Base = 0x14;
    public const int Save_ShotgunAmmo_Base = 0x16;
    public const int Save_M16Ammo_Base = 0x18;
    public const int Save_GrenadeAmmo_Base = 0x1A;
    public const int Save_HarpoonAmmo_Base = 0x1C;
    public const int Save_SmallMedipacks_Base = 0x1E;
    public const int Save_LargeMedipacks_Base = 0x1F;
    public const int Save_Flares_Base = 0x21;
    public const int Save_WeaponsConfig_Base = 0x3C;

    // Statistics (fixed offsets in slot)
    public const int Save_TimeTaken = 0x610;
    public const int Save_AmmoUsed = 0x614;
    public const int Save_Hits = 0x618;
    public const int Save_Kills = 0x61C;
    public const int Save_Distance = 0x620;
    public const int Save_SecretsFound = 0x624;
    public const int Save_Pickups = 0x626;
    public const int Save_MedipacksUsed = 0x627;
    public const int Save_LevelIndex = 0x628;

    /// <summary>
    /// Runtime secrets counter (NOT bitmask like TR1). Int16.
    /// Increments 0→1→2→3 as silver/jade/gold dragons are collected.
    /// Direct offset, not in WSB.
    /// </summary>
    public const int Runtime_SecretsFound = 0x528C34;

    // Weapon flags (OR'd into weapon config byte)
    public const byte Weapon_None = 1;
    public const byte Weapon_Pistols = 2;
    public const byte Weapon_AutoPistols = 4;
    public const byte Weapon_Uzis = 8;
    public const byte Weapon_Shotgun = 16;
    public const byte Weapon_M16 = 32;
    public const byte Weapon_GrenadeLauncher = 64;
    public const byte Weapon_HarpoonGun = 128;
}
