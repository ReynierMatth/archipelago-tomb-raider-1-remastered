namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Complete memory map for TR1 Remastered (Patch 5.4 "Steam 6 October 2026 - Challenge Mode Update 4").
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
/// WARNING: Offsets are specific to Patch 5.4. They change between patches.
/// Patch 5.4 tomb123.exe SHA256: 614f9578215a213a0950b0cb1533d7d416fb5eb1daac6e6375da0e08c8e2b729
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
    public static int  Exe_Level = 0x38f740;

    /// <summary>Active game: 0=TR1, 1=TR2, 2=TR3. Int32.</summary>
    public static int  Exe_GameVersion = 0xf0438;

    /// <summary>Non-zero when exiting the game. Int8.</summary>
    public static int  Exe_ExitingGame = 0x41f1d8;

    /// <summary>1 = photo mode active. Int32.</summary>
    public static int  Exe_IsPhotoMode = 0x38f774;

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
    public static int  LaraBase = 0x33ea90;

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
    public static int  LaraId = 0x33e8e0;

    /// <summary>Equipped weapon type. Int32. See GunType enum.</summary>
    public static int  LaraGunType = 0x33e8e2;

    /// <summary>Oxygen / air timer. Int16. Max ~1800.</summary>
    public static int  LaraOxygen = 0x33e8f6;

    /// <summary>Gun state flags. UInt32.</summary>
    public static int  LaraGunFlags = 0x33e920;

    /// <summary>Pointer to currently aimed enemy. UInt64.</summary>
    public static int  LaraAimingEnemy = 0x33e9d0;

    /// <summary>Water state of current room. Int16.</summary>
    public static int  LaraRoomType = 0x33e8ec;

    // ----- LARA_INFO Ammo (live runtime, Int32 each, 8-byte stride) -----
    // These are the AMMO_INFO fields in LARA_INFO. Writing here changes ammo instantly.
    // Shotgun ammo is stored internally as rounds*6 (6 pellets per shot).

    /// <summary>Magnum ammo count. Int32. Direct value (1 ammo = 1 displayed).</summary>
    public static int  Lara_MagnumAmmo = 0x33EA28;

    /// <summary>Uzi ammo count. Int32. Direct value (1 ammo = 1 displayed).</summary>
    public static int  Lara_UziAmmo = 0x33EA30;

    /// <summary>Shotgun ammo count. Int32. Internal = displayed * 6 (6 pellets per shell).</summary>
    public static int  Lara_ShotgunAmmo = 0x33EA38;

    /// <summary>Shotgun internal multiplier (each shell = 6 pellets).</summary>
    public const int ShotgunAmmoMultiplier = 6;

    // ----- Level & Game State -----

    /// <summary>Current TR1 level ID (0=Home, 1=Caves, ..., 24=Menu). Int32.</summary>
    public static int  LevelId = 0x105ab8;

    /// <summary>Set to 1 when current level is completed. Int32.</summary>
    public static int  LevelCompleted = 0x126284;

    /// <summary>Frame tick. Int8. Alternates each frame.</summary>
    public static int  BinaryTick = 0x126298;

    /// <summary>Greater than 0 = in gameplay, 0 or less = in menu/loading. Int32.</summary>
    public static int  IsInGameScene = 0x105e50;

    /// <summary>Menu cursor position. UInt16.</summary>
    public static int  MenuSelection = 0x105e54;

    /// <summary>Menu state machine. UInt16. 0xD = exit to title.</summary>
    public static int  MenuState = 0x4F8522;

    /// <summary>1 = New Game+ mode. UInt8.</summary>
    public static int  NewGamePlus = 0x4f4bea;

    /// <summary>Input bitfield (action keys). UInt32.</summary>
    public static int  ActionKeys = 0x420a74;

    // ----- Entity Array -----

    /// <summary>Pointer to the entity array in heap memory. Read as Int64.</summary>
    public static int  EntitiesPointer = 0x420d78;

    /// <summary>Number of entities in the current level. Int16.</summary>
    public static int  EntitiesCount = 0x3f06c8;

    // ----- Rooms -----

    /// <summary>Pointer to room array. Int64.</summary>
    public static int  RoomsPointer = 0x420d68;

    /// <summary>Number of rooms. Int16.</summary>
    public static int  RoomsCount = 0x420c30;

    // ----- Inventory Ring (Main Ring) -----
    // Proven by full disassembly of Inv_AddItem (tomb1.dll+0x1F7D0):
    //   insert 0x1FD10, remove 0x1FF80 — both load/store the count field
    //   and index items[]/qtys[] from these bases.
    // The main ring stores items sorted by inv_pos.

    /// <summary>
    /// Main ring item count. Int16. Immediately follows LevelId (Int32 @ 0x105ab8).
    /// Proven: insert (0x1FD10) does count = items[count] append + count++,
    /// remove (0x1FF80) does count-- + shift. Physical capacity 23 — insert has
    /// NO capacity check, so appending at count==23 would corrupt qtys[0].
    /// </summary>
    public static int  MainRingCount = 0x105ABC;

    /// <summary>
    /// Main ring items[] array. 23 × Int64 pointers to INVENTORY_ITEM structs.
    /// Ends exactly where MainRingQtys begins (0x11BD10 + 23*8 == 0x11BDC8).
    /// </summary>
    public static int  MainRingItems = 0x11BD10;

    /// <summary>Main ring qtys[] array. 23 × Int16 quantities. Stride 2.</summary>
    public static int  MainRingQtys = 0x11BDC8;

    /// <summary>
    /// Max items per ring (physical main-ring capacity; keys ring walks use the
    /// same bound as a safety guard).
    /// </summary>
    public const int MaxRingItems = 23;

    // ----- Inventory Ring (Keys Ring) -----
    // Same mechanic as Main Ring: count + items[] pointers + qtys[].
    // Stores key items (keys, puzzles, pickups that go to the key ring).
    // Proven by disassembly of the keys-ring paths of insert/remove.

    /// <summary>Keys ring item count. Int16.</summary>
    public static int  KeysRingCount = 0x120D44;

    /// <summary>Keys ring items[] array. 24 × Int64 pointers to INVENTORY_ITEM structs.</summary>
    public static int  KeysRingItems = 0x11C590;

    /// <summary>
    /// Keys ring qtys[] array. Int16 quantities. Stride 2.
    /// Note: qtys[] is BEFORE items[] in memory (not after).
    /// </summary>
    public static int  KeysRingQtys = 0x11BFE0;

    /// <summary>
    /// INVENTORY_ITEM struct stride in the global table (3280 bytes).
    /// All INVENTORY_ITEM structs are stored sequentially in tomb1.dll.
    /// Pointer for item X = Pistols_ptr + RelativeIndex * Stride.
    /// CAVEAT: holds for weapons/ammo/meds/compass (live-proven via the
    /// Compass invariant) but NOT for Key2/Key3/Key4 — those are off-grid.
    /// Never locate an item by relIdx pointer math; match on InvItem_ObjectId.
    /// </summary>
    public const int InventoryItemStride = 0xCD0;

    /// <summary>
    /// Whether the ring count field offsets are confirmed against the live game.
    /// TRUE: the count fields were proven by disassembly of insert/remove
    /// (load + store of the same addresses in both functions, plus the
    /// LevelId-adjacency cross-check). Reads use the count field directly;
    /// the NULL-walk is only a fallback for out-of-range values.
    /// </summary>
    public static bool MainRingCountConfirmed = true;

    /// <summary>Offset of object_id field within an INVENTORY_ITEM struct. Int16.</summary>
    public const int InvItem_ObjectId = 0x08;

    /// <summary>
    /// Relative table indices from Pistols' INVENTORY_ITEM.
    /// Usage: target_ptr = pistols_ptr + RelIndex * InventoryItemStride
    /// </summary>
    public static class InvItemRelIndex
    {
        // Main Ring items — weapons & gear
        public const int ShotgunAmmo = -8;
        public const int SmallMedipack = -6;
        public const int LargeMedipack = -2;
        public const int Pistols = 0;
        public const int Shotgun = 1;
        public const int MagnumAmmo = 4;
        public const int Compass = 6;
        public const int Uzis = 9;
        public const int UziAmmo = 10;
        public const int Magnums = 12;

        // Keys Ring items — proven by table parse, but Key2/Key3/Key4 are NOT
        // stride-aligned from Pistols (off-grid): use objId matching instead.
        public const int Key1 = -1;    // Neptune/Silver/Gold/Rusty/Sapphire Key
        public const int Key2 = -7;    // Atlas Key, Cistern Silver Key
        public const int Key3 = 7;     // Damocles Key, Cistern Rusty Key
        // Key4: NOT in the stride-aligned table. Use Key4ByteOffset instead.
        public const int Puzzle1 = 11; // Gold Bar, Fuse, Ankh, Scarab, etc.
        public const int Puzzle2 = -4; // Seal of Anubis, etc.
        public const int Puzzle3 = 5;
        public const int Puzzle4 = -5;
        public const int Scion = -10;  // Scion piece
    }

    /// <summary>
    /// Key4 (Thor Key) has its INVENTORY_ITEM dynamically allocated outside the
    /// stride-aligned global table. Its address is at a fixed BYTE offset from Pistols.
    /// Usage: key4_ptr = pistols_ptr - 0x9E00
    /// Only used in St. Francis' Folly. Confirmed via CE injection test.
    /// </summary>
    public const int Key4ByteOffset = -0x9E00;

    /// <summary>
    /// Inventory object IDs (from INVENTORY_ITEM+0x08 in the global table).
    /// NOTE: These are NOT the same as TR1Type entity IDs — they are internal
    /// inventory IDs discovered via the Mode 6 table scanner.
    /// </summary>
    public static class InvObjId
    {
        // Main Ring items
        public const int Pistols = 0x63;
        public const int Shotgun = 0x64;
        public const int Magnums = 0x65;
        public const int Uzis = 0x66;
        public const int ShotgunAmmo = 0x68;
        public const int MagnumAmmo = 0x69;
        public const int UziAmmo = 0x6A;
        public const int SmallMedipack = 0x6C;
        public const int LargeMedipack = 0x6D;
        public const int Compass = 0x48;
        // Keys Ring items
        public const int Key1 = 0x85;
        public const int Key2 = 0x86;
        public const int Key3 = 0x87;
        public const int Key4 = 0x88;  // Thor Key — dynamically allocated
        public const int Puzzle1 = 0x72;
        public const int Puzzle2 = 0x73;
        public const int Puzzle3 = 0x74;
        public const int Puzzle4 = 0x75;
        public const int Scion = 0x96;
    }

    /// <summary>
    /// Maps inventory object_id values to human-readable names.
    /// Used by the INVENTORY_ITEM table scanner (Mode 6).
    /// </summary>
    public static readonly Dictionary<int, string> InvObjIdNames = new()
    {
        [InvObjId.Pistols] = "Pistols",
        [InvObjId.Shotgun] = "Shotgun",
        [InvObjId.Magnums] = "Magnums",
        [InvObjId.Uzis] = "Uzis",
        [InvObjId.ShotgunAmmo] = "Shotgun Ammo",
        [InvObjId.MagnumAmmo] = "Magnum Ammo",
        [InvObjId.UziAmmo] = "Uzi Ammo",
        [InvObjId.SmallMedipack] = "Small Medipack",
        [InvObjId.LargeMedipack] = "Large Medipack",
        [InvObjId.Compass] = "Compass",
        [InvObjId.Key1] = "Key 1",
        [InvObjId.Key2] = "Key 2",
        [InvObjId.Key3] = "Key 3",
        [InvObjId.Key4] = "Key 4 (Thor Key)",
        [InvObjId.Puzzle1] = "Puzzle 1 (Gold Bar/Fuse)",
        [InvObjId.Puzzle2] = "Puzzle 2",
        [InvObjId.Puzzle3] = "Puzzle 3",
        [InvObjId.Puzzle4] = "Puzzle 4",
        [InvObjId.Scion] = "Scion",
    };

    // ----- Inv_AddItem (native inventory give, tomb1.dll) -----
    // Full flow proven by disassembly of 0x1F7D0..0x1FD03 (patch 5.4):
    //   1. flag gate: test byte [base + objId*0x900 + 0x420DF6], 1 —
    //      set when the object is loaded in the current level (BSS, zero on disk).
    //   2. if clear: call 0x1F590 (pickup-id <-> canonical-id alias map,
    //      e.g. 84<->99, 100<->104, 93<->108) and retry the flag once —
    //      passing either member of a pair works whenever either is loaded.
    //   3. canonicalize (0x200F0): stack into main/keys ring when already owned
    //      (medipack qty caps: small @255, large uncapped), else switch on objId
    //      -> static INVENTORY_ITEM struct + native insert (0x1FD10).
    // Return codes are AMBIGUOUS (weapon-add and refusal both return 0) —
    // always verify effects by ring read-back, never by exit code.

    /// <summary>Inv_AddItem(int objId) — tomb1.dll RVA (thread-proc arg arrives in RCX).</summary>
    public static int InvAddItem = 0x1F7D0;

    /// <summary>
    /// "Object loaded in current level" flag byte at
    /// base + objId*InvLoadedFlagStride + InvLoadedFlagTable.
    /// If neither member of an id pair has its flag set, Inv_AddItem refuses
    /// (returns 0) — e.g. keys in a level that does not use them.
    /// </summary>
    public static int InvLoadedFlagTable = 0x420DF6;
    public const int InvLoadedFlagStride = 0x900;

    /// <summary>
    /// Expected prologue of Inv_AddItem (21 bytes, patch 5.4):
    ///   48 89 5C 24 08  mov [rsp+08],rbx
    ///   48 89 74 24 10  mov [rsp+10],rsi
    ///   48 89 7C 24 18  mov [rsp+18],rdi
    ///   41 56           push r14
    ///   48 83 EC 20     sub rsp,0x20
    /// Refuse every remote call if live bytes differ (patch changed under us).
    /// </summary>
    public static readonly byte[] InvAddItemSignature =
    {
        0x48, 0x89, 0x5C, 0x24, 0x08,
        0x48, 0x89, 0x74, 0x24, 0x10,
        0x48, 0x89, 0x7C, 0x24, 0x18,
        0x41, 0x56,
        0x48, 0x83, 0xEC, 0x20,
    };

    /// <summary>
    /// Ring remove (qty--, full removal at 0; searches main then keys ring).
    /// RVA + expected prologue (patch 5.4, 10 bytes):
    ///   48 89 74 24 08  mov [rsp+08],rsi
    ///   57              push rdi
    ///   48 83 EC 20     sub rsp,0x20
    /// </summary>
    public static int InvRemoveItem = 0x1FF80;
    public static readonly byte[] InvRemoveItemSignature =
    {
        0x48, 0x89, 0x74, 0x24, 0x08,
        0x57,
        0x48, 0x83, 0xEC, 0x20,
    };

    // ----- Sound -----

    /// <summary>
    /// Sound_Effect(int32 sfx, const XYZ_32 *pos, uint32 flags) — tomb1.dll RVA.
    ///
    /// Proven by full disassembly: 84 call sites pass ecx = sample id,
    /// rdx = position pointer (or NULL for non-positional), r8d = flags; the
    /// body indexes the level sound map at 0x377920 (-1/-2 sentinels) and owns
    /// the MssSetPlayingLutIndex bound error (ids 0..0x171). Observed constant
    /// ids match the engine's own SFX_* catalog one for one (0x09 WOLF_HURT,
    /// 0x25 LION_HURT, 0x1E PUSHBLOCK_LAND, 0x0D LARA_INJURY, 0x30 MENU_COMPASS,
    /// 0x46 SECRET), including two call sites that pass SFX_SECRET.
    ///
    /// Takes three register arguments, so it cannot be driven by CallRemote
    /// (thread parameter only reaches RCX) — use ProcessMemory.CallRemote3.
    /// </summary>
    public static int SoundEffect = 0x88480;

    /// <summary>
    /// Expected prologue of Sound_Effect (21 bytes, current build):
    ///   48 89 5C 24 10  mov [rsp+10],rbx
    ///   44 89 44 24 18  mov [rsp+18],r8d   ; flags saved first — arg3 exists
    ///   55              push rbp
    ///   56              push rsi
    ///   57              push rdi
    ///   41 54           push r12
    ///   41 55           push r13
    ///   41 56           push r14
    ///   41 57           push r15
    /// Refuse every remote call if live bytes differ (patch changed under us).
    /// </summary>
    public static readonly byte[] SoundEffectSignature =
    {
        0x48, 0x89, 0x5C, 0x24, 0x10,
        0x44, 0x89, 0x44, 0x24, 0x18,
        0x55, 0x56, 0x57,
        0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57,
    };

    /// <summary>
    /// AP-check / secret feedback chime — catalog id 2 (SFX_LARA_NO).
    /// Proven to always be loaded (present in every level's sound map), so it is a
    /// reliable audible cue. The semantically-correct SFX_SECRET is id 70, but this
    /// build's per-level map does not load it everywhere (level 1 -> -1), so the
    /// engine is silent there.
    /// </summary>
    public const int SfxSecret = 2;

    // ----- Camera -----

    public static int  CameraX = 0x2cb92c;
    public static int  CameraY = 0x2cb93c;
    public static int  CameraZ = 0x2cb94c;
    public static int  CameraYaw = 0x2cb8ae;
    public static int  CameraPitch = 0x2cb8ac;
    public static int  CameraFov = 0x2cb950;

    // ----- WorldState Backup Buffer -----
    // This is a 0x3800-byte buffer that uses the same layout as a save slot.
    // Updated during save/load. Can be used to read inventory state.

    /// <summary>
    /// Save-format buffer in memory. Same layout as a save file slot.
    /// Ammo/weapons/inventory can be read/written here using Save_* offsets.
    /// </summary>
    public static int  WorldStateBackup = 0x4f45c0;

    /// <summary>Size of the WorldState backup buffer (same as save slot).</summary>
    public const int WorldStateBackupSize = 0x6800;

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

    public static int  SaveFileBaseOffset = 0x2000;
    public static int  SaveFileMaxOffset = 0x72000;
    public static int  SaveSlotSize = 0x3800;
    public static int  MaxSaveSlots = 32;
    public static int  SaveFileSize = 0x152004;

    public const int Save_SlotStatus = 0x004;
    public const int Save_GameMode = 0x008;
    public const int Save_Number = 0x00C;

    /// <summary>
    /// Save counter offset within the WSB (runtime). The WSB layout differs from
    /// the save file by -4 bytes (no SlotStatus field), so Save_Number (0x00C in
    /// the file) maps to 0x008 in the WSB. Confirmed via CE: tomb1.dll+0x4C4E08.
    /// </summary>
    public static int  WSB_SaveCounter = 0x008;
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
    public static int  Runtime_SecretsFound = 0x624;
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
