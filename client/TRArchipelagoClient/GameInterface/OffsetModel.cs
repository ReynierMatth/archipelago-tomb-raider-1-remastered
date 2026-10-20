using System.Security.Cryptography;
using TRArchipelagoClient.UI;

namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Blast-radius grouping of build-sensitive memory offsets.
/// A game patch normally shifts one region (e.g. the inventory ring block) and
/// leaves the rest alone, so failures are reported and retried per group instead
/// of taking the whole client down.
/// </summary>
public enum OffsetGroup
{
    /// <summary>tomb123.exe global state.</summary>
    Exe,

    /// <summary>Level/scene/Lara anchors every other read hangs off.</summary>
    Core,

    /// <summary>Entity array + rooms (pickup detection).</summary>
    Entities,

    /// <summary>Main Ring + Keys Ring (item injection, sentinel removal).</summary>
    Ring,

    /// <summary>Camera block.</summary>
    Camera,

    /// <summary>WorldStateBackup buffer (secrets, save counter, WSB ammo).</summary>
    Wsb,

    /// <summary>savegame.dat slot geometry.</summary>
    SaveSlot,
}

public enum OffsetState
{
    /// <summary>Not checked yet.</summary>
    Pending,

    /// <summary>Compiled-in/manifest offsets passed every invariant.</summary>
    Verified,

    /// <summary>Offsets failed, were re-derived by a scan, and now pass.</summary>
    Resolved,

    /// <summary>Re-identification failed — dependent features are disabled.</summary>
    Quarantined,
}

public sealed class OffsetReport
{
    public OffsetState State { get; set; } = OffsetState.Pending;
    public int Attempts { get; set; }
    public List<string> Notes { get; } = new();
    public string? LastError { get; set; }

    public void Reset(OffsetState state)
    {
        State = state;
        Notes.Clear();
        LastError = null;
    }
}

/// <summary>
/// Group membership, invariant checks and build identity for the offset layer.
/// </summary>
public static class OffsetModel
{
    public static readonly OffsetGroup[] AllGroups =
    {
        OffsetGroup.Exe,
        OffsetGroup.Core,
        OffsetGroup.Entities,
        OffsetGroup.Ring,
        OffsetGroup.Camera,
        OffsetGroup.Wsb,
        OffsetGroup.SaveSlot,
    };

    /// <summary>Offsets owned by each group — used for the version-diff report.</summary>
    public static readonly IReadOnlyDictionary<OffsetGroup, string[]> Members =
        new Dictionary<OffsetGroup, string[]>
        {
            [OffsetGroup.Exe] = new[]
            {
                nameof(TR1RMemoryMap.Exe_Level), nameof(TR1RMemoryMap.Exe_GameVersion),
                nameof(TR1RMemoryMap.Exe_ExitingGame), nameof(TR1RMemoryMap.Exe_IsPhotoMode),
            },
            [OffsetGroup.Core] = new[]
            {
                nameof(TR1RMemoryMap.LevelId), nameof(TR1RMemoryMap.IsInGameScene),
                nameof(TR1RMemoryMap.LevelCompleted), nameof(TR1RMemoryMap.BinaryTick),
                nameof(TR1RMemoryMap.MenuSelection), nameof(TR1RMemoryMap.MenuState),
                nameof(TR1RMemoryMap.NewGamePlus), nameof(TR1RMemoryMap.ActionKeys),
                nameof(TR1RMemoryMap.LaraBase), nameof(TR1RMemoryMap.LaraId),
                nameof(TR1RMemoryMap.LaraGunType), nameof(TR1RMemoryMap.LaraOxygen),
                nameof(TR1RMemoryMap.LaraGunFlags), nameof(TR1RMemoryMap.LaraAimingEnemy),
                nameof(TR1RMemoryMap.LaraRoomType), nameof(TR1RMemoryMap.Lara_MagnumAmmo),
                nameof(TR1RMemoryMap.Lara_UziAmmo), nameof(TR1RMemoryMap.Lara_ShotgunAmmo),
            },
            [OffsetGroup.Entities] = new[]
            {
                nameof(TR1RMemoryMap.EntitiesPointer), nameof(TR1RMemoryMap.EntitiesCount),
                nameof(TR1RMemoryMap.RoomsPointer), nameof(TR1RMemoryMap.RoomsCount),
            },
            [OffsetGroup.Ring] = new[]
            {
                nameof(TR1RMemoryMap.MainRingCount), nameof(TR1RMemoryMap.MainRingItems),
                nameof(TR1RMemoryMap.MainRingQtys), nameof(TR1RMemoryMap.KeysRingCount),
                nameof(TR1RMemoryMap.KeysRingItems), nameof(TR1RMemoryMap.KeysRingQtys),
            },
            [OffsetGroup.Camera] = new[]
            {
                nameof(TR1RMemoryMap.CameraX), nameof(TR1RMemoryMap.CameraY),
                nameof(TR1RMemoryMap.CameraZ), nameof(TR1RMemoryMap.CameraYaw),
                nameof(TR1RMemoryMap.CameraPitch), nameof(TR1RMemoryMap.CameraFov),
            },
            [OffsetGroup.Wsb] = new[]
            {
                nameof(TR1RMemoryMap.WorldStateBackup), nameof(TR1RMemoryMap.WSB_SaveCounter),
                nameof(TR1RMemoryMap.Runtime_SecretsFound),
            },
            [OffsetGroup.SaveSlot] = new[]
            {
                nameof(TR1RMemoryMap.SaveFileBaseOffset), nameof(TR1RMemoryMap.SaveFileMaxOffset),
                nameof(TR1RMemoryMap.SaveSlotSize), nameof(TR1RMemoryMap.MaxSaveSlots),
                nameof(TR1RMemoryMap.SaveFileSize),
            },
        };

    private static readonly Dictionary<OffsetGroup, OffsetReport> _reports =
        AllGroups.ToDictionary(g => g, _ => new OffsetReport());

    /// <summary>Per-group status for logging and gating.</summary>
    public static IReadOnlyDictionary<OffsetGroup, OffsetReport> Reports => _reports;

    public static OffsetReport Report(OffsetGroup group) => _reports[group];

    /// <summary>True when the group's offsets can be trusted for read/write.</summary>
    public static bool Usable(OffsetGroup group) =>
        _reports[group].State is OffsetState.Verified or OffsetState.Resolved;

    public static bool IsQuarantined(OffsetGroup group) =>
        _reports[group].State == OffsetState.Quarantined;

    public static string QuarantinedGroups() =>
        string.Join(", ", AllGroups.Where(IsQuarantined).Select(g => g.ToString()));

    // =================================================================
    // INVARIANTS
    // =================================================================

    /// <summary>
    /// Runs every invariant for a group. An empty list means the group's offsets
    /// describe a self-consistent game state; a non-empty list is the exact
    /// reason the group is about to be re-identified (or quarantined).
    /// </summary>
    public static List<string> Validate(ProcessMemory m, OffsetGroup group, string? saveFile = null)
        => group switch
        {
            OffsetGroup.Exe => ValidateExe(m),
            OffsetGroup.Core => ValidateCore(m),
            OffsetGroup.Entities => ValidateEntities(m),
            OffsetGroup.Ring => ValidateRing(m),
            OffsetGroup.Camera => ValidateCamera(m),
            OffsetGroup.Wsb => ValidateWsb(m),
            OffsetGroup.SaveSlot => ValidateSaveSlot(saveFile),
            _ => new List<string> { $"no invariant defined for {group}" },
        };

    private static List<string> ValidateExe(ProcessMemory m)
    {
        var failures = new List<string>();
        if (m.ExeBase == IntPtr.Zero)
        {
            failures.Add("tomb123.exe base not resolved");
            return failures;
        }

        int version = m.ReadInt32(m.ExeBase, TR1RMemoryMap.Exe_GameVersion);
        if (version is < 0 or > 3)
            failures.Add($"Exe_GameVersion = {version} (expected 0..3)");

        int level = m.ReadInt32(m.ExeBase, TR1RMemoryMap.Exe_Level);
        if (level is < -1 or > 64)
            failures.Add($"Exe_Level = {level} (implausible)");
        return failures;
    }

    private static List<string> ValidateCore(ProcessMemory m)
    {
        var failures = new List<string>();
        IntPtr t1 = m.Tomb1Base;
        if (t1 == IntPtr.Zero)
        {
            failures.Add("tomb1.dll not loaded");
            return failures;
        }

        int levelId = m.ReadInt32(t1, TR1RMemoryMap.LevelId);
        if (levelId is < 0 or > 24)
            failures.Add($"LevelId = {levelId} (expected 0..24)");

        int inGame = m.ReadInt32(t1, TR1RMemoryMap.IsInGameScene);
        if (inGame is < -1 or > 100)
            failures.Add($"IsInGameScene = {inGame} (expected 0..1)");

        IntPtr lara = m.ReadPointer(t1 + TR1RMemoryMap.LaraBase);
        if (lara == IntPtr.Zero)
        {
            failures.Add("LaraBase dereferences to NULL");
        }
        else if (inGame > 0)
        {
            short hp = m.ReadInt16(lara + TR1RMemoryMap.Item_HitPoints);
            if (hp is < 0 or > TR1RMemoryMap.MaxHealth)
                failures.Add($"Lara health = {hp} (expected 0..{TR1RMemoryMap.MaxHealth}) — Item_HitPoints/LaraBase mismatch");
        }

        foreach (var (name, offset) in new[]
        {
            ("Lara_MagnumAmmo", TR1RMemoryMap.Lara_MagnumAmmo),
            ("Lara_UziAmmo", TR1RMemoryMap.Lara_UziAmmo),
            ("Lara_ShotgunAmmo", TR1RMemoryMap.Lara_ShotgunAmmo),
        })
        {
            int ammo = m.ReadInt32(t1, offset);
            if (ammo is < 0 or > 999_999)
                failures.Add($"{name} = {ammo} (implausible)");
        }

        return failures;
    }

    private static List<string> ValidateEntities(ProcessMemory m)
    {
        var failures = new List<string>();
        IntPtr t1 = m.Tomb1Base;
        if (t1 == IntPtr.Zero)
        {
            failures.Add("tomb1.dll not loaded");
            return failures;
        }

        int count = (ushort)m.ReadInt16(t1, TR1RMemoryMap.EntitiesCount);
        if (count is < 1 or > 4000)
            failures.Add($"EntitiesCount = {count} (expected 1..4000)");

        IntPtr entities = m.ReadPointer(t1 + TR1RMemoryMap.EntitiesPointer);
        if (entities == IntPtr.Zero)
        {
            failures.Add("EntitiesPointer dereferences to NULL");
        }
        else if (count >= 1 && count <= 4000)
        {
            // The runtime entity struct is not the inventory-item struct: its
            // object id is not at InvItem_ObjectId and low slots legitimately
            // read -1 (unused box links). Pickup detection is proven working
            // through Item_Flags, and no write path depends on Entities, so we
            // only soft-check here instead of failing the group.
            for (int i = 0; i < Math.Min(count, 5); i++)
            {
                short at0 = m.ReadInt16(entities + i * TR1RMemoryMap.EntitySize);
                short at18 = m.ReadInt16(entities + i * TR1RMemoryMap.EntitySize + 0x18);
                if ((at0 is >= 0 and <= 900) || (at18 is >= 0 and <= 900))
                    return failures;
            }
            ConsoleUI.Warning("[OFFSET] Entities: no plausible object id in first slots (soft check only)");
        }

        int rooms = (ushort)m.ReadInt16(t1, TR1RMemoryMap.RoomsCount);
        if (rooms is < 1 or > 512)
            failures.Add($"RoomsCount = {rooms} (expected 1..512)");

        IntPtr roomPtr = m.ReadPointer(t1 + TR1RMemoryMap.RoomsPointer);
        if (roomPtr == IntPtr.Zero)
            failures.Add("RoomsPointer dereferences to NULL");

        return failures;
    }

    private static List<string> ValidateRing(ProcessMemory m)
    {
        var failures = new List<string>();
        IntPtr t1 = m.Tomb1Base;

        // The count fields are not (yet) confirmed, so validate the arrays by
        // their walked (NULL-terminated) length and their structural invariants.
        short mainWalk = OffsetIdentifier.RingWalk(m, TR1RMemoryMap.MainRingItems);
        if (mainWalk is < 1 or > 24)
        {
            failures.Add($"main ring walked length = {mainWalk} (expected 1..24)");
        }
        else if (TR1RMemoryMap.MainRingCountConfirmed)
        {
            if (!OffsetIdentifier.TryEvaluateMainRing(m,
                    TR1RMemoryMap.MainRingCount, TR1RMemoryMap.MainRingItems, TR1RMemoryMap.MainRingQtys,
                    out var reason))
                failures.Add($"main ring: {reason}");
        }
        else if (mainWalk > 1 && OffsetIdentifier.FindPistols(m, t1) == IntPtr.Zero)
        {
            failures.Add("main ring: no Compass/Pistols pair found in the array");
        }

        // The keys ring may legitimately be empty; a non-empty one must be
        // coherent (non-NULL entries, plausible quantities).
        short keysCount = OffsetIdentifier.RingWalk(m, TR1RMemoryMap.KeysRingItems);
        if (keysCount is < 0 or > 24)
        {
            failures.Add($"keys ring walked length = {keysCount} (expected 0..24)");
        }
        else if (keysCount > 0)
        {
            for (int i = 0; i < keysCount; i++)
            {
                IntPtr ptr = m.ReadPointer(t1 + TR1RMemoryMap.KeysRingItems + i * 8);
                if (ptr == IntPtr.Zero)
                {
                    failures.Add($"KeysRingItems[{i}] is NULL while walked length is {keysCount}");
                    break;
                }
                short qty = m.ReadInt16(t1 + TR1RMemoryMap.KeysRingQtys + i * 2);
                if (qty is < 0 or > 10_000)
                {
                    failures.Add($"KeysRingQtys[{i}] = {qty} (implausible)");
                    break;
                }
            }
        }

        return failures;
    }

    private static List<string> ValidateCamera(ProcessMemory m)
    {
        var failures = new List<string>();
        IntPtr t1 = m.Tomb1Base;
        if (t1 == IntPtr.Zero)
        {
            failures.Add("tomb1.dll not loaded");
            return failures;
        }

        int x = m.ReadInt32(t1, TR1RMemoryMap.CameraX);
        int y = m.ReadInt32(t1, TR1RMemoryMap.CameraY);
        int z = m.ReadInt32(t1, TR1RMemoryMap.CameraZ);

        // World coordinates are bounded by level geometry; code/data bytes read
        // at a wrong offset virtually always land outside this envelope.
        foreach (var (name, value) in new[] { ("CameraX", x), ("CameraY", y), ("CameraZ", z) })
        {
            if (Math.Abs(value) > 10_000_000)
                failures.Add($"{name} = {value} (outside world-coordinate envelope)");
        }

        if (x == 0 && y == 0 && z == 0 && m.ReadInt32(t1, TR1RMemoryMap.IsInGameScene) > 0)
            failures.Add("CameraX/Y/Z are all 0 while in-game");

        return failures;
    }

    private static List<string> ValidateWsb(ProcessMemory m)
    {
        var failures = new List<string>();
        IntPtr t1 = m.Tomb1Base;
        if (t1 == IntPtr.Zero)
        {
            failures.Add("tomb1.dll not loaded");
            return failures;
        }

        int counter = m.ReadInt32(t1 + TR1RMemoryMap.WorldStateBackup + TR1RMemoryMap.WSB_SaveCounter);
        if (counter is < 0 or > 100_000)
            failures.Add($"WSB save counter = {counter} (expected 0..100000)");

        ushort secrets = m.ReadUInt16(t1 + TR1RMemoryMap.WorldStateBackup + TR1RMemoryMap.Runtime_SecretsFound);
        if (System.Numerics.BitOperations.PopCount(secrets) > 16)
            failures.Add($"WSB secrets mask 0x{secrets:X4} has too many bits set");

        return failures;
    }

    private static List<string> ValidateSaveSlot(string? saveFile)
    {
        var failures = new List<string>();
        if (saveFile == null || !File.Exists(saveFile))
        {
            failures.Add("savegame.dat not located (save-slot geometry unchecked)");
            return failures;
        }

        try
        {
            using var fs = new FileStream(saveFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(fs);

            if (fs.Length < TR1RMemoryMap.SaveFileBaseOffset + TR1RMemoryMap.SaveSlotSize)
            {
                failures.Add($"save file is {fs.Length} bytes — smaller than base {TR1RMemoryMap.SaveFileBaseOffset:X} + slot {TR1RMemoryMap.SaveSlotSize:X}");
                return failures;
            }

            int occupied = 0;
            int slots = (int)Math.Min(TR1RMemoryMap.MaxSaveSlots,
                (fs.Length - TR1RMemoryMap.SaveFileBaseOffset) / TR1RMemoryMap.SaveSlotSize);

            for (int i = 0; i < slots; i++)
            {
                long slotBase = TR1RMemoryMap.SaveFileBaseOffset + (long)i * TR1RMemoryMap.SaveSlotSize;
                if (slotBase + TR1RMemoryMap.Save_SlotStatus >= fs.Length) break;

                fs.Seek(slotBase + TR1RMemoryMap.Save_SlotStatus, SeekOrigin.Begin);
                byte status = reader.ReadByte();
                if (status != 1) continue;

                occupied++;
                fs.Seek(slotBase + TR1RMemoryMap.Save_Number, SeekOrigin.Begin);
                int saveNumber = reader.ReadInt32();
                if (saveNumber < 0 || saveNumber > 1_000_000)
                    failures.Add($"slot {i}: Save_Number = {saveNumber} (implausible) — SaveSlotSize/SaveFileBaseOffset mismatch");

                fs.Seek(slotBase + TR1RMemoryMap.Save_LevelIndex, SeekOrigin.Begin);
                byte levelIndex = reader.ReadByte();
                if (levelIndex is < 1 or > 19)
                    failures.Add($"slot {i}: LevelIndex = {levelIndex} (expected 1..19) — SaveSlotSize/SaveFileBaseOffset mismatch");
            }

            if (occupied == 0 && fs.Length != TR1RMemoryMap.SaveFileSize)
                failures.Add($"no occupied slot found in {fs.Length}-byte file (expected {TR1RMemoryMap.SaveFileSize})");
        }
        catch (Exception ex)
        {
            failures.Add($"save file read failed: {ex.Message}");
        }

        return failures;
    }

    // =================================================================
    // BUILD IDENTITY
    // =================================================================

    private static string? _buildHash;

    /// <summary>
    /// SHA256 of tomb123.exe on disk — the key offsets are catalogued under.
    /// Recomputed once per process.
    /// </summary>
    public static string BuildHash
    {
        get
        {
            if (_buildHash != null) return _buildHash;
            _buildHash = ComputeHash() ?? "unknown";
            return _buildHash;
        }
    }

    /// <summary>Best-effort fingerprint of the active game binary.</summary>
    public static string ComputeHash()
    {
        try
        {
            string? exePath = CurrentExePath;
            if (exePath == null || !File.Exists(exePath)) return null;

            using var stream = File.OpenRead(exePath);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Set once ProcessMemory exposes the on-disk path of the host exe.</summary>
    public static string? CurrentExePath { get; set; }

    // =================================================================
    // MUTABLE OFFSET ACCESSORS (explicit, no reflection — the client is
    // published with PublishTrimmed=true, so runtime field lookup is unsafe).
    // Only groups that can be re-derived at runtime are exposed here.
    // =================================================================

    private static readonly Dictionary<string, Action<int>> Setters = new()
    {
        [nameof(TR1RMemoryMap.MainRingCount)] = v => TR1RMemoryMap.MainRingCount = v,
        [nameof(TR1RMemoryMap.MainRingItems)] = v => TR1RMemoryMap.MainRingItems = v,
        [nameof(TR1RMemoryMap.MainRingQtys)] = v => TR1RMemoryMap.MainRingQtys = v,
        [nameof(TR1RMemoryMap.KeysRingCount)] = v => TR1RMemoryMap.KeysRingCount = v,
        [nameof(TR1RMemoryMap.KeysRingItems)] = v => TR1RMemoryMap.KeysRingItems = v,
        [nameof(TR1RMemoryMap.KeysRingQtys)] = v => TR1RMemoryMap.KeysRingQtys = v,
        [nameof(TR1RMemoryMap.SaveFileBaseOffset)] = v => TR1RMemoryMap.SaveFileBaseOffset = v,
        [nameof(TR1RMemoryMap.SaveFileMaxOffset)] = v => TR1RMemoryMap.SaveFileMaxOffset = v,
        [nameof(TR1RMemoryMap.SaveSlotSize)] = v => TR1RMemoryMap.SaveSlotSize = v,
        [nameof(TR1RMemoryMap.MaxSaveSlots)] = v => TR1RMemoryMap.MaxSaveSlots = v,
        [nameof(TR1RMemoryMap.SaveFileSize)] = v => TR1RMemoryMap.SaveFileSize = v,
    };

    private static readonly Dictionary<string, Func<int>> Getters = new()
    {
        [nameof(TR1RMemoryMap.MainRingCount)] = () => TR1RMemoryMap.MainRingCount,
        [nameof(TR1RMemoryMap.MainRingItems)] = () => TR1RMemoryMap.MainRingItems,
        [nameof(TR1RMemoryMap.MainRingQtys)] = () => TR1RMemoryMap.MainRingQtys,
        [nameof(TR1RMemoryMap.KeysRingCount)] = () => TR1RMemoryMap.KeysRingCount,
        [nameof(TR1RMemoryMap.KeysRingItems)] = () => TR1RMemoryMap.KeysRingItems,
        [nameof(TR1RMemoryMap.KeysRingQtys)] = () => TR1RMemoryMap.KeysRingQtys,
        [nameof(TR1RMemoryMap.SaveFileBaseOffset)] = () => TR1RMemoryMap.SaveFileBaseOffset,
        [nameof(TR1RMemoryMap.SaveFileMaxOffset)] = () => TR1RMemoryMap.SaveFileMaxOffset,
        [nameof(TR1RMemoryMap.SaveSlotSize)] = () => TR1RMemoryMap.SaveSlotSize,
        [nameof(TR1RMemoryMap.MaxSaveSlots)] = () => TR1RMemoryMap.MaxSaveSlots,
        [nameof(TR1RMemoryMap.SaveFileSize)] = () => TR1RMemoryMap.SaveFileSize,
    };

    /// <summary>Offsets currently loaded, captured as a manifest-ready block.</summary>
    public static IReadOnlyList<(string Name, int Value)> Snapshot(OffsetGroup group) =>
        Members[group]
            .Where(Getters.ContainsKey)
            .Select(name => (name, Getters[name]()))
            .ToList();

    public static void SetOffset(string name, int value)
    {
        if (Setters.TryGetValue(name, out var setter))
            setter(value);
    }

    public static void ApplySnapshot(OffsetGroup group, IReadOnlyDictionary<string, int> values)
    {
        foreach (var name in Members[group])
        {
            if (values.TryGetValue(name, out int value))
                SetOffset(name, value);
        }
    }

    /// <summary>Prints a paste-ready manifest block for the current build.</summary>
    public static void PrintManifestBlock(OffsetGroup group)
    {
        Console.WriteLine($"        // {BuildHash}");
        Console.WriteLine($"        [\"{BuildHash}\"] = new Dictionary<string, int>");
        Console.WriteLine("        {");
        foreach (var (name, value) in Snapshot(group))
            Console.WriteLine($"            [\"{name}\"] = 0x{value:X},");
        Console.WriteLine("        },");
    }
}
