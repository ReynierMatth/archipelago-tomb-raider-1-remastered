namespace TRArchipelagoClient.Core;

/// <summary>
/// Holds all game-specific configuration for the AP client.
/// Each TR game (TR1, TR2, TR3) has its own GameConfig instance.
/// </summary>
public class GameConfig
{
    /// <summary>AP game name used for login (must match APWorld).</summary>
    public required string ApGameName { get; init; }

    /// <summary>Short key: "tr1", "tr2", "tr3".</summary>
    public required string GameKey { get; init; }

    /// <summary>DLL module name in tomb123.exe (e.g. "tomb1.dll").</summary>
    public required string ModuleName { get; init; }

    /// <summary>Subdirectory under the game install dir (e.g. "1" for TR1).</summary>
    public required string DataSubDir { get; init; }

    /// <summary>Level file names in sequence order (e.g. "LEVEL1.PHD").</summary>
    public required string[] LevelFiles { get; init; }

    /// <summary>Level base names without extension for backup (e.g. "LEVEL1").</summary>
    public required string[] LevelBaseNames { get; init; }

    /// <summary>File extensions to backup (e.g. ".PHD", ".PDP").</summary>
    public required string[] LevelExtensions { get; init; }

    /// <summary>Sentinel file used to detect data directory (e.g. "LEVEL1.PHD").</summary>
    public required string SentinelFile { get; init; }

    /// <summary>AP base ID for items (e.g. 770000 for TR1).</summary>
    public required int ItemBaseId { get; init; }

    /// <summary>AP base ID for trap items.</summary>
    public required int TrapBaseId { get; init; }

    /// <summary>AP base ID for pickup locations.</summary>
    public required int LocationBaseId { get; init; }

    /// <summary>AP base ID for secret locations.</summary>
    public required int SecretBaseId { get; init; }

    /// <summary>AP base ID for level completion events.</summary>
    public required int LevelCompleteBaseId { get; init; }

    /// <summary>AP tags for login (e.g. "TR1R", "DeathLink").</summary>
    public required string[] ApTags { get; init; }

    /// <summary>
    /// Generic pickup AP ID mapping: offset from ItemBaseId -> item category.
    /// Only standard (non-key) items. Key items use alias ranges.
    /// </summary>
    public required Dictionary<int, GenericItemInfo> GenericItems { get; init; }

    /// <summary>
    /// Key item alias level bases: offset from ItemBaseId base -> level file.
    /// E.g. 10000 -> "LEVEL1.PHD" for TR1.
    /// </summary>
    public required Dictionary<int, string> KeyItemLevelBases { get; init; }

    public int LevelCount => LevelFiles.Length;
}

public class GenericItemInfo
{
    public required string Name { get; init; }
    public required ItemCategory Category { get; init; }
}

public enum ItemCategory
{
    Weapon,
    Ammo,
    Medipack,
    KeyItem,
    Trap,
    Unknown,
}
