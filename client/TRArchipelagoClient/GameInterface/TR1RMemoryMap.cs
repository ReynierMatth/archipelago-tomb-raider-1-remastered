namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Complete memory map for TR1 Remastered (Patch 4.1 "Golden Pistols").
///
/// Architecture: tomb123.exe loads per-game DLLs (tomb1.dll, tomb2.dll, tomb3.dll).
/// All TR1 game state lives inside tomb1.dll. Only one DLL is active at a time.
///
/// Sources:
///   - Burns Multiplayer Mod (https://github.com/burn-sours/tomb-raider-remastered-multiplayer)
///   - TRR-SaveMaster (https://github.com/JulianOzelRose/TRR-SaveMaster)
///   - LostArtefacts/TRX (https://github.com/LostArtefacts/TRX) - ITEM struct definitions
///   - FearLess Revolution CE tables
///
/// WARNING: Offsets are specific to Patch 4.1. They change between patches.
/// Patch 4.1 tomb123.exe SHA256: d732834ad9f092968167e1a4c71f8c6bdb59809cee617b213f4f31e788504858
/// </summary>
public static class TR1RMemoryMap
{
    // =================================================================
    // PROCESS & MODULE NAMES
    // =================================================================
    public const string HostProcessName = "tomb123";
    public const string TR1ModuleName = "tomb1.dll";
    public const string TR2ModuleName = "tomb2.dll";
    public const string TR3ModuleName = "tomb3.dll";

    // =================================================================
    // GLOBAL STATE (tomb123.exe offsets, relative to tomb123.exe base)
    // =================================================================

    /// <summary>Current level ID (global, across all games). Int32.</summary>
    public const int Exe_Level = 0x263CD0;

    /// <summary>Active game: 0=TR1, 1=TR2, 2=TR3. Int32.</summary>
    public const int Exe_GameVersion = 0xe4bd8;

    /// <summary>Non-zero when exiting the game. Int8.</summary>
    public const int Exe_ExitingGame = 0x2f35ec;

    /// <summary>1 = photo mode active. Int32.</summary>
    public const int Exe_IsPhotoMode = 0x263D04;

    // =================================================================
    // TR1 RUNTIME OFFSETS (tomb1.dll, Patch 4.1)
    // All offsets relative to tomb1.dll base address.
    // =================================================================

    // ----- Lara Pointer Chain -----
    // tomb1.dll+LaraBase contains a POINTER (Int64) to Lara's ITEM struct.
    // Dereference it first, then apply ITEM struct offsets.

    /// <summary>
    /// Pointer to Lara's ITEM struct. Read as Int64 (8 bytes), then dereference.
    /// Usage: laraPtr = ReadPointer(tomb1Base + LaraBase)
    /// </summary>
    public const int LaraBase = 0x311030;

    // ----- ITEM Struct Offsets (from dereferenced LaraBase) -----
    // Each entity in the game uses this same struct layout.
    // Entity size = 0xE50 bytes.

    public const int EntitySize = 0xE50;

    /// <summary>Object type ID. Int16.</summary>
    public const int Item_ObjectId = 0x0E;

    /// <summary>Current animation state. Int16.</summary>
    public const int Item_AnimState = 0x10;

    /// <summary>Current animation number. Int16.</summary>
    public const int Item_AnimNum = 0x18;

    /// <summary>Current frame number. Int16.</summary>
    public const int Item_FrameNum = 0x1A;

    /// <summary>Room index the entity is in. Int16.</summary>
    public const int Item_RoomNum = 0x1C;

    /// <summary>XZ movement speed. Int16.</summary>
    public const int Item_Speed = 0x22;

    /// <summary>Y velocity (fall speed). Int16.</summary>
    public const int Item_FallSpeed = 0x24;

    /// <summary>Hit points (health). Int16, range 0-1000 for Lara.</summary>
    public const int Item_HitPoints = 0x26;

    /// <summary>Position X. Int32.</summary>
    public const int Item_PosX = 0x58;

    /// <summary>Position Y (height, negative = up). Int32.</summary>
    public const int Item_PosY = 0x5C;

    /// <summary>Position Z. Int32.</summary>
    public const int Item_PosZ = 0x60;

    /// <summary>
    /// Entity flags. Int16. Contains activation/visibility state.
    /// When a pickup is collected, its flags change (deactivated/invisible).
    /// </summary>
    public const int Item_Flags = 0x1E4;

    // ----- LARA_INFO Static Variables (direct offsets from tomb1.dll) -----

    /// <summary>Lara's entity index in the entities array. Int16.</summary>
    public const int LaraId = 0x310e80;

    /// <summary>Equipped weapon type. Int32. See GunType enum.</summary>
    public const int LaraGunType = 0x310e82;

    /// <summary>Oxygen / air timer. Int16. Max ~1800.</summary>
    public const int LaraOxygen = 0x310E96;

    /// <summary>Gun state flags. UInt32.</summary>
    public const int LaraGunFlags = 0x310ec0;

    /// <summary>Pointer to currently aimed enemy. UInt64.</summary>
    public const int LaraAimingEnemy = 0x310f70;

    /// <summary>Water state of current room. Int16.</summary>
    public const int LaraRoomType = 0x310e8c;

    // ----- Level & Game State -----

    /// <summary>Current TR1 level ID (0=Home, 1=Caves, ..., 24=Menu). Int32.</summary>
    public const int LevelId = 0xe2ab8;

    /// <summary>Set to 1 when current level is completed. Int32.</summary>
    public const int LevelCompleted = 0xfd750;

    /// <summary>Frame tick. Int8. Alternates each frame.</summary>
    public const int BinaryTick = 0xfd760;

    /// <summary>Greater than 0 = in gameplay, 0 or less = in menu/loading. Int32.</summary>
    public const int IsInGameScene = 0xe2e50;

    /// <summary>Menu cursor position. UInt16.</summary>
    public const int MenuSelection = 0xe2e54;

    /// <summary>Menu state machine. UInt16. 0xD = exit to title.</summary>
    public const int MenuState = 0x4c8d62;

    /// <summary>1 = New Game+ mode. UInt8.</summary>
    public const int NewGamePlus = 0x4c542a;

    /// <summary>Input bitfield (action keys). UInt32.</summary>
    public const int ActionKeys = 0x3f1e74;

    // ----- Entity Array -----

    /// <summary>Pointer to the entity array in heap memory. Read as Int64.</summary>
    public const int EntitiesPointer = 0x3f2178;

    /// <summary>Number of entities in the current level. Int16.</summary>
    public const int EntitiesCount = 0x3c1b20;

    // ----- Rooms -----

    /// <summary>Pointer to room array. Int64.</summary>
    public const int RoomsPointer = 0x3f2168;

    /// <summary>Number of rooms. Int16.</summary>
    public const int RoomsCount = 0x3f2030;

    // ----- Camera -----

    public const int CameraX = 0x29e2ec;
    public const int CameraY = 0x29e2fc;
    public const int CameraZ = 0x29E30C;
    public const int CameraYaw = 0x29E26E;
    public const int CameraPitch = 0x29e26c;
    public const int CameraFov = 0x29e310;

    // ----- WorldState Backup Buffer -----
    // This is a 0x3800-byte buffer that uses the same layout as a save slot.
    // Updated during save/load. Can be used to read inventory state.

    /// <summary>
    /// Save-format buffer in memory. Same layout as a save file slot.
    /// Ammo/weapons/inventory can be read/written here using Save_* offsets.
    /// </summary>
    public const int WorldStateBackup = 0x4c4e00;

    /// <summary>Size of the WorldState backup buffer (same as save slot).</summary>
    public const int WorldStateBackupSize = 0x3800;

    // ----- Gun Type Enum -----

    public enum GunType
    {
        Unarmed = 0,
        Pistols = 1,
        Magnums = 2,
        Uzis = 3,
        Shotgun = 4,
    }

    // ----- Level IDs (runtime, 0-based unlike save file which is 1-based) -----

    public const int Level_Home = 0;
    public const int Level_Caves = 1;
    public const int Level_Vilcabamba = 2;
    public const int Level_LostValley = 3;
    public const int Level_Qualopec = 4;
    public const int Level_Folly = 5;
    public const int Level_Colosseum = 6;
    public const int Level_Midas = 7;
    public const int Level_Cistern = 8;
    public const int Level_Tihocan = 9;
    public const int Level_Khamoon = 10;
    public const int Level_Obelisk = 11;
    public const int Level_Sanctuary = 12;
    public const int Level_Mines = 13;
    public const int Level_Atlantis = 14;
    public const int Level_Pyramid = 15;
    public const int Level_ReturnToEgypt = 16;
    public const int Level_TempleOfCat = 17;
    public const int Level_Stronghold = 18;
    public const int Level_Hive = 19;
    public const int Level_MainMenu = 24;

    /// <summary>Level names indexed by runtime level ID (0-based).</summary>
    public static readonly Dictionary<int, string> LevelNames = new()
    {
        [0]  = "Lara's Home",
        [1]  = "Caves",
        [2]  = "City of Vilcabamba",
        [3]  = "Lost Valley",
        [4]  = "Tomb of Qualopec",
        [5]  = "St. Francis' Folly",
        [6]  = "Colosseum",
        [7]  = "Palace Midas",
        [8]  = "The Cistern",
        [9]  = "Tomb of Tihocan",
        [10] = "City of Khamoon",
        [11] = "Obelisk of Khamoon",
        [12] = "Sanctuary of the Scion",
        [13] = "Natla's Mines",
        [14] = "Atlantis",
        [15] = "The Great Pyramid",
        [16] = "Return to Egypt",
        [17] = "Temple of the Cat",
        [18] = "Atlantean Stronghold",
        [19] = "The Hive",
        [24] = "Main Menu",
    };

    /// <summary>
    /// Converts runtime level ID (0-based, 0=Home) to LocationMapper index (0-based, 0=Caves).
    /// Returns -1 for non-game levels (Home, Menu, UB levels).
    /// </summary>
    public static int ToLocationMapperIndex(int runtimeLevelId) => runtimeLevelId switch
    {
        >= 1 and <= 15 => runtimeLevelId - 1, // Caves(1)->0, ..., Pyramid(15)->14
        _ => -1,
    };

    // =================================================================
    // HEALTH CONSTANTS
    // =================================================================
    public const short MaxHealth = 1000;
    public const short MinHealth = 1;

    // =================================================================
    // SAVEGAME FILE OFFSETS (savegame.dat) - for SaveFileReader fallback
    // All offsets are relative to the start of a save slot.
    // Slot address = SaveFileBaseOffset + (slot_index * SaveSlotSize)
    // =================================================================

    public const int SaveFileBaseOffset = 0x2000;
    public const int SaveFileMaxOffset = 0x72000;
    public const int SaveSlotSize = 0x3800;
    public const int MaxSaveSlots = 32;
    public const int SaveFileSize = 0x152004;

    public const int Save_SlotStatus = 0x004;
    public const int Save_GameMode = 0x008;
    public const int Save_Number = 0x00C;
    public const int Save_MagnumAmmo = 0x4C2;
    public const int Save_UziAmmo = 0x4C4;
    public const int Save_ShotgunAmmo = 0x4C6;
    public const int Save_SmallMedipacks = 0x4C8;
    public const int Save_LargeMedipacks = 0x4C9;
    public const int Save_WeaponsConfig = 0x4EC;
    public const int Save_CrystalsUsed = 0x610;
    public const int Save_TimeTaken = 0x614;
    public const int Save_AmmoUsed = 0x618;
    public const int Save_Hits = 0x61C;
    public const int Save_Kills = 0x620;
    public const int Save_Distance = 0x624;
    public const int Save_SecretsFound = 0x628;

    /// <summary>
    /// Runtime secrets bitmask offset within WorldStateBackup.
    /// NOT the same as Save_SecretsFound (0x628) — the live value is at 0x624.
    /// Updates in real-time when a secret is found during gameplay.
    /// </summary>
    public const int Runtime_SecretsFound = 0x624;
    public const int Save_Pickups = 0x62A;
    public const int Save_MedipacksUsed = 0x62B;
    public const int Save_LevelIndex = 0x62C;

    // Save file weapon flag constants
    public const byte Weapon_None = 1;
    public const byte Weapon_Pistols = 2;
    public const byte Weapon_Magnums = 4;
    public const byte Weapon_Uzis = 8;
    public const byte Weapon_Shotgun = 16;

    /// <summary>
    /// Per-level health offset in save file (varies because of entity data size).
    /// Level index is 1-based (save file format).
    /// </summary>
    public static int GetSaveHealthOffset(int saveFileLevelIndex) => saveFileLevelIndex switch
    {
        1  => 0x825,
        2  => 0x181D,
        3  => 0x82D,
        4  => 0xC41,
        5  => 0x1A39,
        6  => 0xF4F,
        7  => 0x82F,
        8  => 0x197B,
        9  => 0xA29,
        10 => 0x827,
        11 => 0xA8F,
        12 => 0x114F,
        13 => 0x12D3,
        14 => 0xD0F,
        15 => 0x10FD,
        16 => 0x8F3,
        17 => 0xE1D,
        18 => 0xE35,
        19 => 0x10DF,
        _  => -1,
    };
}
