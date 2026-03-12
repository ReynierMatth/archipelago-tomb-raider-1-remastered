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

    // ----- Lara Pointer Chain -----

    /// <summary>Pointer to Lara's ITEM struct. Read as Int64, then dereference.</summary>
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
    public const int LevelId = 0x132b58;

    /// <summary>Set to 1 when current level is completed. Int32.</summary>
    public const int LevelCompleted = 0x1330b8;

    /// <summary>Frame tick. Int8.</summary>
    public const int BinaryTick = 0x1330c8;

    /// <summary>Greater than 0 = in gameplay, 0 or less = in menu/loading. Int32.</summary>
    public const int IsInGameScene = 0x1142ec;

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
    public const int EntitiesPointer = 0x4f9fc0;

    /// <summary>Number of entities in the current level. Int16.</summary>
    public const int EntitiesCount = 0x3FD1B4;

    // ----- Rooms -----

    public const int RoomsPointer = 0x427360;
    public const int RoomsCount = 0x3fd1b0;

    // ----- Inventory Rings -----
    // Same ring system as TR1: count (Int16) + items[] (Int64 pointers) + qtys[] (Int16)
    // Found via Cheat Engine (Patch 4.1).

    public const int MainRingCount = 0x113EDC;
    public const int MainRingItems = 0x12E5E0;
    public const int MainRingQtys = 0x12E698;
    // public const int KeysRingCount = ???;
    // public const int KeysRingItems = ???;
    // public const int KeysRingQtys = ???;
    public const int MaxRingItems = 24;
    public const int InventoryItemStride = 0xCD0;
    public const int InvItem_ObjectId = 0x08;

    // ----- WorldState Backup Buffer -----

    /// <summary>Save-format buffer in memory. Same 0x3800-byte layout as a save slot.</summary>
    public const int WorldStateBackup = 0x4f9fe0;

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

    /// <summary>Runtime secrets bitmask in WSB (save offset - 4 for missing SlotStatus).</summary>
    public const int Runtime_SecretsFound = 0x620;

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
