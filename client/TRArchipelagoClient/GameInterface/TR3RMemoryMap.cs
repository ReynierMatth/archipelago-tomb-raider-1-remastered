namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Memory map for TR3 Remastered (Patch 4.1 "Golden Pistols").
///
/// Architecture: tomb123.exe loads tomb3.dll for TR3 gameplay.
/// All TR3 game state lives inside tomb3.dll.
///
/// Sources:
///   - Burns Multiplayer Mod (patch4.1/tr3.js)
///   - TRR-SaveMaster (save file offsets)
///   - FearLess Revolution CE tables
///   - tomb3 decompilation (struct layout reference)
///
/// WARNING: Offsets are specific to Patch 4.1. They change between patches.
/// </summary>
public static class TR3RMemoryMap
{
    // =================================================================
    // TR3 RUNTIME OFFSETS (tomb3.dll, Patch 4.1)
    // All offsets relative to tomb3.dll base address.
    // =================================================================

    // ----- Lara Pointer Chain -----

    /// <summary>Pointer to Lara's ITEM struct. Read as Int64, then dereference.</summary>
    public const int LaraBase = 0x3a2070;

    /// <summary>Lara's entity index in the entities array. Int16.</summary>
    public const int LaraId = 0x3a1ec0;

    // ----- ITEM Struct Offsets (shared with TR1/TR2 remastered) -----
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

    // ----- LARA_INFO Static Variables (direct offsets from tomb3.dll) -----

    /// <summary>Equipped weapon type. Int32.</summary>
    public const int LaraGunType = 0x3a1ec4;

    /// <summary>Oxygen / air timer. Int16.</summary>
    public const int LaraOxygen = 0x3a1ed6;

    /// <summary>Gun state flags. UInt16.</summary>
    public const int LaraGunFlags = 0x3a1f00;

    /// <summary>Pointer to currently aimed enemy. UInt64.</summary>
    public const int LaraAimingEnemy = 0x3a1fb0;

    /// <summary>Water/climb state of Lara. Int16.</summary>
    public const int LaraClimbState = 0x3a1ece;

    /// <summary>Water state of current room. Int16.</summary>
    public const int LaraRoomType = 0x3a1ecc;

    /// <summary>Vehicle entity ID (-1 = none). Int16.</summary>
    public const int LaraVehicleId = 0x3a1ee8;

    // ----- LARA_INFO Ammo (live runtime) -----
    // TODO: Find via Cheat Engine. TR3 has 8 weapon types:
    //   Pistols, Desert Eagle, Uzis, Shotgun, MP5, Rocket Launcher, Grenade Launcher, Harpoon Gun
    // Expected: Int32 fields with 8-byte stride (AMMO_INFO structs)
    // Starting point: LaraId + ~0x148 (by analogy with TR1)

    // public const int Lara_DeagleAmmo = ???;
    // public const int Lara_UziAmmo = ???;
    // public const int Lara_ShotgunAmmo = ???;
    // public const int Lara_MP5Ammo = ???;
    // public const int Lara_RocketAmmo = ???;
    // public const int Lara_GrenadeAmmo = ???;
    // public const int Lara_HarpoonAmmo = ???;

    public const int ShotgunAmmoMultiplier = 6;

    // ----- Level & Game State -----

    /// <summary>Current TR3 level ID (0=Home, 1=Jungle, ..., 63=Menu). Int32.</summary>
    public const int LevelId = 0x18e16c;

    /// <summary>Set to 1 when current level is completed. Int32.</summary>
    public const int LevelCompleted = 0x18e690;

    /// <summary>Frame tick. Int8.</summary>
    public const int BinaryTick = 0x18e69c;

    /// <summary>Greater than 0 = in gameplay, 0 or less = in menu/loading. Int32.</summary>
    public const int IsInGameScene = 0x1682dc;

    /// <summary>Menu cursor position. UInt16.</summary>
    public const int MenuSelection = 0x16ad88;

    /// <summary>Menu state machine. UInt16.</summary>
    public const int MenuState = 0x562022;

    /// <summary>1 = New Game+ mode. UInt8.</summary>
    public const int NewGamePlus = 0x55e6d4;

    /// <summary>Input bitfield (action keys). UInt32.</summary>
    public const int ActionKeys = 0x461090;

    // ----- Entity Array -----

    /// <summary>Pointer to the entity array in heap memory. Read as Int64.</summary>
    public const int EntitiesPointer = 0x48b168;

    /// <summary>Number of entities in the current level. Int16.</summary>
    public const int EntitiesCount = 0x460310;

    // ----- Rooms -----

    public const int RoomsPointer = 0x461140;
    public const int RoomsCount = 0x460290;

    // ----- Inventory Rings -----
    // TODO: Find via Cheat Engine. Same ring system as TR1/TR2.

    // public const int MainRingCount = ???;
    // public const int MainRingItems = ???;
    // public const int MainRingQtys = ???;
    // public const int KeysRingCount = ???;
    // public const int KeysRingItems = ???;
    // public const int KeysRingQtys = ???;
    // public const int MaxRingItems = 24;
    // public const int InventoryItemStride = ???;

    // ----- WorldState Backup Buffer -----

    /// <summary>Save-format buffer in memory. Same 0x3800-byte layout as a save slot.</summary>
    public const int WorldStateBackup = 0x55de00;

    public const int WorldStateBackupSize = 0x3800;

    /// <summary>Save counter in WSB (Save_Number minus SlotStatus offset).</summary>
    public const int WSB_SaveCounter = 0x008;

    // ----- Level IDs (runtime, 0-based) -----

    public const int Level_Home = 0;
    // India
    public const int Level_Jungle = 1;
    public const int Level_TempleRuins = 2;
    public const int Level_RiverGanges = 3;
    public const int Level_CavesOfKaliya = 4;
    // South Pacific
    public const int Level_CoastalVillage = 5;
    public const int Level_CrashSite = 6;
    public const int Level_MadubuGorge = 7;
    public const int Level_TempleOfPuna = 8;
    // London
    public const int Level_ThamesWharf = 9;
    public const int Level_Aldwych = 10;
    public const int Level_LudsGate = 11;
    public const int Level_City = 12;
    // Nevada
    public const int Level_NevadaDesert = 13;
    public const int Level_HighSecurityCompound = 14;
    public const int Level_Area51 = 15;
    // Antarctica
    public const int Level_Antarctica = 16;
    public const int Level_RXTechMines = 17;
    public const int Level_LostCityOfTinnos = 18;
    public const int Level_MeteoriteCavern = 19;
    // Bonus
    public const int Level_AllHallows = 20;
    // The Lost Artifact DLC
    public const int Level_HighlandFling = 21;
    public const int Level_WillardsLair = 22;
    public const int Level_ShakespeareCliff = 23;
    public const int Level_SleepingWithTheFishes = 24;
    public const int Level_ItsAMadhouse = 25;
    public const int Level_Reunion = 26;
    public const int Level_MainMenu = 63;

    public static readonly Dictionary<int, string> LevelNames = new()
    {
        [0]  = "Lara's Home",
        // India
        [1]  = "Jungle",
        [2]  = "Temple Ruins",
        [3]  = "The River Ganges",
        [4]  = "Caves of Kaliya",
        // South Pacific
        [5]  = "Coastal Village",
        [6]  = "Crash Site",
        [7]  = "Madubu Gorge",
        [8]  = "Temple of Puna",
        // London
        [9]  = "Thames Wharf",
        [10] = "Aldwych",
        [11] = "Lud's Gate",
        [12] = "City",
        // Nevada
        [13] = "Nevada Desert",
        [14] = "High Security Compound",
        [15] = "Area 51",
        // Antarctica
        [16] = "Antarctica",
        [17] = "RX-Tech Mines",
        [18] = "Lost City of Tinnos",
        [19] = "Meteorite Cavern",
        // Bonus
        [20] = "All Hallows",
        // The Lost Artifact
        [21] = "Highland Fling",
        [22] = "Willard's Lair",
        [23] = "Shakespeare Cliff",
        [24] = "Sleeping with the Fishes",
        [25] = "It's a Madhouse!",
        [26] = "Reunion",
        [63] = "Main Menu",
    };

    /// <summary>
    /// Converts runtime level ID (0-based, 0=Home) to LocationMapper index (0-based, 0=Jungle).
    /// Returns -1 for non-game levels (Home, Menu).
    /// Main game: 1-20 -> 0-19, Lost Artifact: 21-26 -> 20-25.
    /// </summary>
    public static int ToLocationMapperIndex(int runtimeLevelId) => runtimeLevelId switch
    {
        >= 1 and <= 26 => runtimeLevelId - 1,
        _ => -1,
    };

    // =================================================================
    // HEALTH CONSTANTS
    // =================================================================
    public const short MaxHealth = 1000;
    public const short MinHealth = 1;

    // =================================================================
    // SAVEGAME FILE OFFSETS (within savegame.dat)
    // TR3 save slots start at 0xE2000 in the shared savegame.dat file.
    // =================================================================

    public const int SaveFileBaseOffset = 0xE2000;
    public const int SaveFileMaxOffset = 0x152000;
    public const int SaveSlotSize = 0x3800;
    public const int MaxSaveSlots = 32;

    public const int Save_SlotStatus = 0x004;
    public const int Save_GameMode = 0x008;
    public const int Save_Number = 0x00C;

    // Per-level ammo: base + (levelIndex * 0x40)
    // levelIndex is 1-based (1..26)
    public const int Save_AmmoStride = 0x40;
    public const int Save_DeagleAmmo_Base = 0x66;
    public const int Save_UziAmmo_Base = 0x68;
    public const int Save_ShotgunAmmo_Base = 0x6A;
    public const int Save_MP5Ammo_Base = 0x6C;
    public const int Save_RocketAmmo_Base = 0x6E;
    public const int Save_HarpoonAmmo_Base = 0x70;
    public const int Save_GrenadeAmmo_Base = 0x72;
    public const int Save_SmallMedipacks_Base = 0x74;
    public const int Save_LargeMedipacks_Base = 0x75;
    public const int Save_Flares_Base = 0x77;
    public const int Save_Crystals_Base = 0x78;
    public const int Save_WeaponsConfig_Base = 0xA0;
    public const int Save_HarpoonPresent_Base = 0xA1;

    // Statistics (fixed offsets in slot)
    public const int Save_CrystalsFound = 0x8A4;
    public const int Save_CrystalsUsed = 0x8A8;
    public const int Save_TimeTaken = 0x8AC;
    public const int Save_AmmoUsed = 0x8B0;
    public const int Save_Hits = 0x8B4;
    public const int Save_Kills = 0x8B8;
    public const int Save_Distance = 0x8BC;
    public const int Save_SecretsFound = 0x8C0;
    public const int Save_Pickups = 0x8C2;
    public const int Save_MedipacksUsed = 0x8C3;
    public const int Save_LevelIndex = 0x8D6;

    /// <summary>Runtime secrets bitmask in WSB (save offset - 4 for missing SlotStatus).</summary>
    public const int Runtime_SecretsFound = 0x8BC;

    // Weapon flags (OR'd into weapon config byte)
    public const byte Weapon_None = 1;
    public const byte Weapon_Pistols = 2;
    public const byte Weapon_DesertEagle = 4;
    public const byte Weapon_Uzis = 8;
    public const byte Weapon_Shotgun = 16;
    public const byte Weapon_MP5 = 32;
    public const byte Weapon_RocketLauncher = 64;
    public const byte Weapon_GrenadeLauncher = 128;
    // Harpoon Gun tracked separately via Save_HarpoonPresent_Base
}
