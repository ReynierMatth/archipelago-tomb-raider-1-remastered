namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Abstracts game-specific memory offsets and behavioral differences between TR1, TR2, and TR3.
/// Each game implements this interface, delegating to its static MemoryMap class for raw offsets
/// and providing game-specific logic for health reading, secrets, and inventory resolution.
/// </summary>
public interface IGameMemoryMap
{
    // --- Identity ---
    string GameKey { get; }       // "tr1", "tr2", "tr3"
    string ModuleName { get; }    // "tomb1.dll", "tomb2.dll", "tomb3.dll"

    // --- DLL Base ---
    IntPtr GetDllBase(ProcessMemory memory);

    // --- Level & Game State ---
    int LevelId { get; }
    int LevelCompleted { get; }
    int IsInGameScene { get; }
    int BinaryTick { get; }
    int ActionKeys { get; }
    int Level_Home { get; }
    int Level_MainMenu { get; }

    // --- Entity Array ---
    int EntitiesPointer { get; }
    int EntitiesCount { get; }
    int EntitySize { get; }
    int Item_ObjectId { get; }
    int Item_Flags { get; }
    int Item_HitPoints { get; }
    int Item_PosX { get; }
    int Item_PosY { get; }
    int Item_PosZ { get; }
    int Item_RoomNum { get; }

    // --- Inventory Rings ---
    int MainRingCount { get; }
    int MainRingItems { get; }
    int MainRingQtys { get; }
    int KeysRingCount { get; }
    int KeysRingItems { get; }
    int KeysRingQtys { get; }
    int MaxRingItems { get; }
    int InventoryItemStride { get; }
    int InvItem_ObjectId { get; }

    // --- Anchor Item (Compass TR1 / Statistiques TR2) ---
    int AnchorObjId { get; }

    // --- WorldState Backup ---
    int WorldStateBackup { get; }
    int WorldStateBackupSize { get; }
    int WSB_SaveCounter { get; }

    // --- Health Constants ---
    short MaxHealth { get; }
    short MinHealth { get; }

    // --- Dictionaries ---
    Dictionary<int, string> LevelNames { get; }
    Dictionary<int, string> InvObjIdNames { get; }

    // =============== BEHAVIORAL METHODS ===============

    /// <summary>
    /// Reads Lara's health. TR1/TR3: deref LaraBase → +0x26 (Int16).
    /// TR2: static address (Int32, clamped to short).
    /// </summary>
    short ReadHealth(ProcessMemory memory, IntPtr dllBase);

    /// <summary>
    /// Reads secrets state. TR1/TR3: bitmask from WSB. TR2: counter from direct offset.
    /// </summary>
    ushort ReadSecrets(ProcessMemory memory, IntPtr dllBase);

    /// <summary>
    /// Detects new secrets by comparing previous and current values.
    /// TR1/TR3: returns new bit indices from bitmask XOR.
    /// TR2: returns sequential indices for counter increment.
    /// </summary>
    List<int> DetectNewSecrets(ushort previous, ushort current);

    /// <summary>
    /// Converts runtime level ID to LocationMapper index (0-based from first playable level).
    /// Returns -1 for non-game levels.
    /// </summary>
    int ToLocationMapperIndex(int runtimeLevelId);

    /// <summary>
    /// Finds the anchor inventory item pointer (ring[0] = Compass/Statistiques).
    /// Verifies the object_id matches AnchorObjId.
    /// </summary>
    IntPtr FindAnchorPointer(ProcessMemory memory, IntPtr dllBase);

    /// <summary>
    /// Resolves an inventory item pointer given the anchor and an InvObjId.
    /// Handles stride-aligned and non-stride items (TR2).
    /// Returns IntPtr.Zero if not resolvable.
    /// </summary>
    IntPtr ResolveInventoryItemPointer(IntPtr anchorPtr, int invObjId);

    /// <summary>
    /// Writes Lara's health. TR1/TR3: pointer chain. TR2: static address.
    /// </summary>
    void WriteHealth(ProcessMemory memory, IntPtr dllBase, short health);

    /// <summary>
    /// Checks if a given InvObjId is a key/puzzle item (belongs in Keys Ring, not Main Ring).
    /// </summary>
    bool IsKeyItem(int invObjId);

    // =============== ITEM INJECTION RECIPES ===============

    /// <summary>
    /// Gets the weapon recipe for a given AP item ID. Returns null if not a weapon.
    /// </summary>
    WeaponRecipe? GetWeaponRecipe(long apItemId, int itemBaseId);

    /// <summary>
    /// Gets the ammo recipe for a given AP item ID. Returns null if not ammo.
    /// </summary>
    AmmoRecipe? GetAmmoRecipe(long apItemId, int itemBaseId);

    /// <summary>
    /// Gets the medipack InvObjId for a given AP item ID. Returns -1 if not a medipack.
    /// </summary>
    int GetMedipackObjId(long apItemId, int itemBaseId);

    /// <summary>
    /// Gets the trap type for a given AP item ID. Returns null if not a trap.
    /// </summary>
    TrapRecipe? GetTrapRecipe(long apItemId, int trapBaseId);

    /// <summary>
    /// Maps a key item slot type string (e.g. "K1", "P2", "Scion") to an InvObjId.
    /// Returns -1 if unknown.
    /// </summary>
    int SlotTypeToInvObjId(string slotType);
}

/// <summary>Recipe for injecting a weapon: ring item + WSB flag + starting ammo.</summary>
public class WeaponRecipe
{
    public required int WeaponObjId { get; init; }
    public required byte WeaponFlag { get; init; }
    public required int AmmoObjId { get; init; }
    public required int LaraAmmoOffset { get; init; }
    public required int StartingAmmo { get; init; }
    public required string Name { get; init; }
}

/// <summary>Recipe for injecting ammo: check weapon ownership, write to LARA_INFO or ring.</summary>
public class AmmoRecipe
{
    public required int WeaponObjId { get; init; }
    public required int AmmoObjId { get; init; }
    public required int LaraAmmoOffset { get; init; }
    public required int Amount { get; init; }
    public required string Name { get; init; }
}

/// <summary>Recipe for applying a trap effect.</summary>
public class TrapRecipe
{
    public required TrapType Type { get; init; }
    public required string Name { get; init; }
}

public enum TrapType
{
    Damage,
    AmmoDrain,
    SmallDrain,
}
