using TRArchipelagoClient.Core;
using TRLevelControl;
using TRLevelControl.Helpers;
using TRLevelControl.Model;

namespace TRArchipelagoClient.Patching;

/// <summary>
/// Patches TR1 level files for Archipelago multiworld.
/// Replaces randomizable pickups with SmallMed_S_P (universal sentinel)
/// and records entity-to-AP-location mappings.
/// </summary>
public class LevelPatcher
{
    private readonly string _gameDir;
    private readonly APSession _session;
    private readonly BackupManager _backupManager;

    // Mapping of (levelFile, entityIndex) -> AP location ID
    private readonly Dictionary<string, Dictionary<int, long>> _locationMappings = new();

    // Items that are AP locations (pickups + key items)
    private static readonly HashSet<TR1Type> _trackableTypes = new(
        TR1TypeUtilities.GetStandardPickupTypes()
            .Concat(TR1TypeUtilities.GetKeyItemTypes())
    );

    // Sentinel type: SmallMed_S_P exists in every level (has mesh data).
    // Player picks up a "small medipack" visually, but the real AP item is determined by the server.
    private const TR1Type SentinelType = TR1Type.SmallMed_S_P;

    public LevelPatcher(string gameDir, APSession session)
    {
        _gameDir = gameDir;
        _session = session;
        _backupManager = new BackupManager(gameDir);
    }

    public BackupManager BackupManager => _backupManager;

    /// <summary>
    /// Backup original files, then scan and patch all level files.
    /// </summary>
    public void PatchAll()
    {
        _backupManager.BackupAll();

        var levels = TR1LevelNames.AsList;

        for (int levelIdx = 0; levelIdx < levels.Count; levelIdx++)
        {
            string levelFile = levels[levelIdx];
            string levelPath = _backupManager.GetLevelPath(levelFile);

            if (levelPath == null)
            {
                Console.WriteLine($"[Patcher] Skipping {levelFile}: file not found");
                continue;
            }

            PatchLevel(levelFile, levelPath, levelIdx);
        }
    }

    /// <summary>
    /// Scan and patch a single level file.
    /// Replaces all pickup/key item entities with SmallMed_S_P sentinel.
    /// </summary>
    private void PatchLevel(string levelFile, string levelPath, int levelIndex)
    {
        var control = new TR1LevelControl();
        TR1Level level;

        try
        {
            level = control.Read(levelPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Patcher] Failed to read {levelFile}: {ex.Message}");
            return;
        }

        var entityMapping = new Dictionary<int, long>();
        int patchedCount = 0;

        for (int i = 0; i < level.Entities.Count; i++)
        {
            var entity = level.Entities[i];

            if (!_trackableTypes.Contains(entity.TypeID))
                continue;

            // Calculate the AP location ID for this entity
            long locationId = LocationMapper.GetPickupLocationId(levelIndex, i);

            // Record the mapping
            entityMapping[i] = locationId;

            // Replace with sentinel — SmallMed_S_P mesh exists in all levels
            entity.TypeID = SentinelType;
            patchedCount++;
        }

        if (patchedCount > 0)
        {
            try
            {
                control.Write(level, levelPath);
                Console.WriteLine($"[Patcher] {levelFile}: patched {patchedCount} pickups");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Patcher] Failed to write {levelFile}: {ex.Message}");
            }
        }

        _locationMappings[levelFile] = entityMapping;
    }

    /// <summary>
    /// Get the AP location ID for a specific entity in a level.
    /// </summary>
    public long? GetLocationId(string levelFile, int entityIndex)
    {
        if (_locationMappings.TryGetValue(levelFile, out var mapping) &&
            mapping.TryGetValue(entityIndex, out var locationId))
        {
            return locationId;
        }
        return null;
    }

    /// <summary>
    /// Get all location mappings for a specific level.
    /// </summary>
    public Dictionary<int, long> GetLevelMappings(string levelFile)
    {
        return _locationMappings.GetValueOrDefault(levelFile) ?? new();
    }

    /// <summary>
    /// Get all entity-to-location mappings indexed by LocationMapper level index (0-based).
    /// Used by GameStateWatcher for real-time entity pickup detection.
    /// </summary>
    public Dictionary<int, Dictionary<int, long>> GetAllMappingsByLevelIndex()
    {
        var result = new Dictionary<int, Dictionary<int, long>>();
        var levels = TR1LevelNames.AsList;

        for (int i = 0; i < levels.Count; i++)
        {
            if (_locationMappings.TryGetValue(levels[i], out var mapping) && mapping.Count > 0)
                result[i] = mapping;
        }

        return result;
    }
}
