namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Memory map for TR3 Remastered.
///
/// Architecture: tomb123.exe loads tomb3.dll for TR3 gameplay.
/// All TR3 game state lives inside tomb3.dll.
///
/// Sources:
///   - Cheat Engine scanning (CE-verified post game update)
///   - Burns Multiplayer Mod (patch4.1/tr3.js)
///   - TRR-SaveMaster (save file offsets)
///   - tomb3 decompilation (struct layout reference)
/// </summary>
public static class TR3RMemoryMap
{
    // =================================================================
    // TR3 RUNTIME OFFSETS (tomb3.dll, CE-verified)
    // =================================================================

    // ----- Lara HP -----
    // Like TR2, TR3 stores HP at a static address (not pointer chain).

    /// <summary>Lara's HP. Int32. Static address, no dereference needed.
    /// Range 0-1000.</summary>
    public const int LaraHP = 0x1B11D8;

    /// <summary>Pointer to Lara's ITEM struct. WARNING: not re-verified post update.</summary>
    public const int LaraBase = 0x3a2070;

    /// <summary>Lara's entity index in the entities array. Int16.</summary>
    public const int LaraId = 0x3a1ec0;

    // ----- ITEM Struct Offsets (shared with TR1/TR2 remastered) -----
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

    // ----- LARA_INFO Static Variables -----

    public const int LaraGunType = 0x3a1ec4;
    public const int LaraOxygen = 0x3a1ed6;
    public const int LaraGunFlags = 0x3a1f00;
    public const int LaraAimingEnemy = 0x3a1fb0;
    public const int LaraClimbState = 0x3a1ece;
    public const int LaraRoomType = 0x3a1ecc;
    public const int LaraVehicleId = 0x3a1ee8;

    // ----- LARA_INFO Ammo (live runtime, stride=8, all CE-verified) -----

    public const int Lara_DeagleAmmo = 0x3D09E8;
    public const int Lara_UziAmmo = 0x3D09F0;
    public const int Lara_ShotgunAmmo = 0x3D09F8;
    public const int Lara_HarpoonAmmo = 0x3D0A00;
    public const int Lara_RocketAmmo = 0x3D0A08;
    public const int Lara_GrenadeAmmo = 0x3D0A10;
    public const int Lara_MP5Ammo = 0x3D0A18;

    public const int ShotgunAmmoMultiplier = 6;

    // ----- Level & Game State (CE-verified) -----

    public const int LevelId = 0x1B376C;
    public const int LevelCompleted = 0x1B915C;
    public const int BinaryTick = 0x18e69c;
    public const int IsInGameScene = 0x1682dc; // WARNING: not re-verified post update
    public const int MenuSelection = 0x16ad88;
    public const int MenuState = 0x562022;
    public const int NewGamePlus = 0x55e6d4;
    public const int ActionKeys = 0x461090;

    // ----- Entity Array (CE-verified) -----

    public const int EntitiesPointer = 0x4BA7A8;
    public const int EntitiesCount = 0x480A88;

    // ----- Rooms -----

    public const int RoomsPointer = 0x461140;
    public const int RoomsCount = 0x460290;

    // ----- Inventory Rings (CE-verified) -----

    public const int MainRingCount = 0x18E3F8;
    public const int MainRingItems = 0x1AE930;
    public const int MainRingQtys = 0x1AE9E8;
    public const int KeysRingCount = 0x1B37CC;
    public const int KeysRingItems = 0x1AEC30;
    public const int KeysRingQtys = 0x1AEC00;
    public const int MaxRingItems = 24;
    public const int InventoryItemStride = 0xCD0;
    public const int InvItem_ObjectId = 0x08;

    /// <summary>
    /// TR3 inventory object IDs (CE-verified via MemoryTest mode 7).
    /// </summary>
    public static class InvObjId
    {
        // Main Ring — Weapons
        public const int Statistiques = 0x92;
        public const int Pistols = 0xB9;
        public const int Shotgun = 0xBA;
        public const int DesertEagle = 0xBB;   // NOT stride-aligned
        public const int Uzis = 0xBC;           // NOT stride-aligned
        public const int HarpoonGun = 0xBD;
        public const int MP5 = 0xBE;
        public const int RocketLauncher = 0xBF;
        public const int GrenadeLauncher = 0xC0; // NOT stride-aligned

        // Main Ring — Ammo
        public const int ShotgunAmmo = 0xC2;
        public const int DeagleAmmo = 0xC3;
        public const int UziAmmo = 0xC4;         // NOT stride-aligned
        public const int Harpoons = 0xC5;
        public const int MP5Ammo = 0xC6;
        public const int Rockets = 0xC7;
        public const int Grenades = 0xC8;

        // Main Ring — Other
        public const int SmallMedipack = 0xC9;
        public const int LargeMedipack = 0xCA;
        public const int Flares = 0xCB;          // NOT stride-aligned

        // Keys Ring — Puzzles (sequential: 0xD1-0xD4)
        public const int Puzzle1 = 0xD1;          // NOT stride-aligned, +0x4450
        public const int Puzzle2 = 0xD2;
        public const int Puzzle3 = 0xD3;
        public const int Puzzle4 = 0xD4;

        // Keys Ring — Keys (sequential: 0xE4-0xE7)
        public const int Key1 = 0xE4;             // Smuggler's Key etc.
        public const int Key2 = 0xE5;
        public const int Key3 = 0xE6;
        public const int Key4 = 0xE7;             // Indra Key confirmed

        // Keys Ring — Special
        public const int Crystal = 0xCC;
        // Pickup/artifact slots (Infada Stone, Element 115, Eye of Isis)
        public const int Pickup1 = 0xF4;
        public const int Pickup2 = 0xF5;
        public const int Pickup3 = 0xF6;
    }

    /// <summary>
    /// Maps TR3 inventory object_id values to human-readable names.
    /// </summary>
    public static readonly Dictionary<int, string> InvObjIdNames = new()
    {
        [InvObjId.Statistiques] = "Statistiques",
        [InvObjId.Pistols] = "Pistols",
        [InvObjId.Shotgun] = "Shotgun",
        [InvObjId.DesertEagle] = "Desert Eagle",
        [InvObjId.Uzis] = "Uzis",
        [InvObjId.HarpoonGun] = "Harpoon Gun",
        [InvObjId.MP5] = "MP5",
        [InvObjId.RocketLauncher] = "Rocket Launcher",
        [InvObjId.GrenadeLauncher] = "Grenade Launcher",
        [InvObjId.ShotgunAmmo] = "Shotgun Shells",
        [InvObjId.DeagleAmmo] = "Desert Eagle Clips",
        [InvObjId.UziAmmo] = "Uzi Clips",
        [InvObjId.Harpoons] = "Harpoons",
        [InvObjId.MP5Ammo] = "MP5 Clips",
        [InvObjId.Rockets] = "Rockets",
        [InvObjId.Grenades] = "Grenades",
        [InvObjId.SmallMedipack] = "Small Medipack",
        [InvObjId.LargeMedipack] = "Large Medipack",
        [InvObjId.Flares] = "Flares",
        [InvObjId.Crystal] = "Save Crystal",
        [InvObjId.Puzzle1] = "Puzzle 1",
        [InvObjId.Puzzle2] = "Puzzle 2",
        [InvObjId.Puzzle3] = "Puzzle 3",
        [InvObjId.Puzzle4] = "Puzzle 4",
        [InvObjId.Key1] = "Key 1",
        [InvObjId.Key2] = "Key 2",
        [InvObjId.Key3] = "Key 3",
        [InvObjId.Key4] = "Key 4 (Indra Key etc.)",
        [InvObjId.Pickup1] = "Pickup 1 (Infada Stone)",
        [InvObjId.Pickup2] = "Pickup 2 (Eye of Isis)",
        [InvObjId.Pickup3] = "Pickup 3 (Element 115)",
    };

    /// <summary>
    /// Byte offsets from Statistiques INVENTORY_ITEM for items NOT stride-aligned.
    /// Same offsets as TR2 for equivalent items!
    /// </summary>
    public static readonly Dictionary<int, int> NonStrideByteOffsets = new()
    {
        [InvObjId.DesertEagle] = 0x6AC0,
        [InvObjId.Uzis] = 0x2AB0,
        [InvObjId.GrenadeLauncher] = 0x5120,
        [InvObjId.UziAmmo] = 0x3780,
        [InvObjId.Flares] = 0x5DF0,
        [InvObjId.Puzzle1] = 0x4450,
    };

    // ----- WorldState Backup Buffer -----

    public const int WorldStateBackup = 0x58D440;
    public const int WorldStateBackupSize = 0x3800;
    public const int WSB_SaveCounter = 0x008;

    /// <summary>
    /// Runtime secrets counter. Byte. Increments 0→1→2→3 per level.
    /// Direct offset from tomb3.dll, NOT in WSB.
    /// </summary>
    public const int Runtime_SecretsFound = 0x58DD20;

    // ----- Level IDs (runtime, 0-based) -----

    public const int Level_Home = 0;
    public const int Level_Jungle = 1;
    public const int Level_TempleRuins = 2;
    public const int Level_RiverGanges = 3;
    public const int Level_CavesOfKaliya = 4;
    public const int Level_CoastalVillage = 5;
    public const int Level_CrashSite = 6;
    public const int Level_MadubuGorge = 7;
    public const int Level_TempleOfPuna = 8;
    public const int Level_ThamesWharf = 9;
    public const int Level_Aldwych = 10;
    public const int Level_LudsGate = 11;
    public const int Level_City = 12;
    public const int Level_NevadaDesert = 13;
    public const int Level_HighSecurityCompound = 14;
    public const int Level_Area51 = 15;
    public const int Level_Antarctica = 16;
    public const int Level_RXTechMines = 17;
    public const int Level_LostCityOfTinnos = 18;
    public const int Level_MeteoriteCavern = 19;
    public const int Level_AllHallows = 20;
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
        [1]  = "Jungle",
        [2]  = "Temple Ruins",
        [3]  = "The River Ganges",
        [4]  = "Caves of Kaliya",
        [5]  = "Coastal Village",
        [6]  = "Crash Site",
        [7]  = "Madubu Gorge",
        [8]  = "Temple of Puna",
        [9]  = "Thames Wharf",
        [10] = "Aldwych",
        [11] = "Lud's Gate",
        [12] = "City",
        [13] = "Nevada Desert",
        [14] = "High Security Compound",
        [15] = "Area 51",
        [16] = "Antarctica",
        [17] = "RX-Tech Mines",
        [18] = "Lost City of Tinnos",
        [19] = "Meteorite Cavern",
        [20] = "All Hallows",
        [21] = "Highland Fling",
        [22] = "Willard's Lair",
        [23] = "Shakespeare Cliff",
        [24] = "Sleeping with the Fishes",
        [25] = "It's a Madhouse!",
        [26] = "Reunion",
        [63] = "Main Menu",
    };

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
    // SAVEGAME FILE OFFSETS
    // =================================================================

    public const int SaveFileBaseOffset = 0xE2000;
    public const int SaveFileMaxOffset = 0x152000;
    public const int SaveSlotSize = 0x3800;
    public const int MaxSaveSlots = 32;

    public const int Save_SlotStatus = 0x004;
    public const int Save_GameMode = 0x008;
    public const int Save_Number = 0x00C;

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

    // Weapon flags
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
