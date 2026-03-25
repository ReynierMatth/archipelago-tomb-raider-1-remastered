using TRArchipelagoClient.Core;
using TRLevelControl;
using TRLevelControl.Helpers;
using TRLevelControl.Model;

namespace TRArchipelagoClient.Patching;

/// <summary>
/// Patches TR level files for Archipelago multiworld.
/// Replaces randomizable pickups with a sentinel entity type
/// and records entity-to-AP-location mappings.
/// Supports TR1, TR2, and TR3 level formats.
/// </summary>
public class LevelPatcher
{
    private readonly string _gameDir;
    private readonly GameConfig _config;
    private readonly LocationMapper _locationMapper;
    private readonly BackupManager _backupManager;

    // Mapping of (levelFile, entityIndex) -> AP location ID
    private readonly Dictionary<string, Dictionary<int, long>> _locationMappings = new();

    // TR1 trackable types
    private static readonly HashSet<TR1Type> _tr1TrackableTypes = new(
        TR1TypeUtilities.GetStandardPickupTypes()
            .Concat(TR1TypeUtilities.GetKeyItemTypes())
    );

    // TR2 trackable types (GetStandardPickupTypes only has guns+ammo, not medipacks)
    private static readonly HashSet<TR2Type> _tr2TrackableTypes = new(
        TR2TypeUtilities.GetStandardPickupTypes()
            .Concat(TR2TypeUtilities.GetKeyItemTypes())
            .Append(TR2Type.SmallMed_S_P)
            .Append(TR2Type.LargeMed_S_P)
            .Append(TR2Type.Flares_S_P)
    );

    // TR3 trackable types (same issue — medipacks not in GetStandardPickupTypes)
    private static readonly HashSet<TR3Type> _tr3TrackableTypes = new(
        TR3TypeUtilities.GetStandardPickupTypes()
            .Concat(TR3TypeUtilities.GetKeyItemTypes())
            .Append(TR3Type.SmallMed_P)
            .Append(TR3Type.LargeMed_P)
            .Append(TR3Type.Flares_P)
    );

    public LevelPatcher(string gameDir, GameConfig config, LocationMapper locationMapper)
    {
        _gameDir = gameDir;
        _config = config;
        _locationMapper = locationMapper;
        _backupManager = new BackupManager(gameDir, config);
    }

    public BackupManager BackupManager => _backupManager;

    /// <summary>
    /// Backup original files, then scan and patch all level files.
    /// </summary>
    public void PatchAll()
    {
        _backupManager.BackupAll();

        var levels = _config.LevelFiles;

        for (int levelIdx = 0; levelIdx < levels.Length; levelIdx++)
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
    /// Dispatches to the correct level control based on game key.
    /// </summary>
    private void PatchLevel(string levelFile, string levelPath, int levelIndex)
    {
        switch (_config.GameKey)
        {
            case "tr1":
                PatchTR1Level(levelFile, levelPath, levelIndex);
                break;
            case "tr2":
                PatchTR2Level(levelFile, levelPath, levelIndex);
                break;
            case "tr3":
                PatchTR3Level(levelFile, levelPath, levelIndex);
                break;
            default:
                Console.WriteLine($"[Patcher] Unknown game key: {_config.GameKey}");
                break;
        }
    }

    private void PatchTR1Level(string levelFile, string levelPath, int levelIndex)
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
            if (!_tr1TrackableTypes.Contains(entity.TypeID))
                continue;

            long locationId = _locationMapper.GetPickupLocationId(levelIndex, i);
            entityMapping[i] = locationId;
            entity.TypeID = TR1Type.SmallMed_S_P;
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

    private void PatchTR2Level(string levelFile, string levelPath, int levelIndex)
    {
        var control = new TR2LevelControl();
        TR2Level level;

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
            if (!_tr2TrackableTypes.Contains(entity.TypeID))
                continue;

            long locationId = _locationMapper.GetPickupLocationId(levelIndex, i);
            entityMapping[i] = locationId;
            entity.TypeID = TR2Type.SmallMed_S_P;
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

    private void PatchTR3Level(string levelFile, string levelPath, int levelIndex)
    {
        var control = new TR3LevelControl();
        TR3Level level;

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
            if (!_tr3TrackableTypes.Contains(entity.TypeID))
                continue;

            long locationId = _locationMapper.GetPickupLocationId(levelIndex, i);
            entityMapping[i] = locationId;
            entity.TypeID = TR3Type.SmallMed_P;
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
    /// Get all entity-to-location mappings indexed by level index (0-based).
    /// Used by GameStateWatcher for real-time entity pickup detection.
    /// </summary>
    public Dictionary<int, Dictionary<int, long>> GetAllMappingsByLevelIndex()
    {
        var result = new Dictionary<int, Dictionary<int, long>>();

        for (int i = 0; i < _config.LevelFiles.Length; i++)
        {
            if (_locationMappings.TryGetValue(_config.LevelFiles[i], out var mapping) && mapping.Count > 0)
                result[i] = mapping;
        }

        return result;
    }
}
