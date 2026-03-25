using TRArchipelagoClient.Core;

namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Holds the runtime state for the currently active game.
/// Passed to GameStateWatcher, InventoryManager, KeyItemMonitor, etc.
/// Swapped when the player switches between TR1/TR2/TR3.
///
/// Also holds per-game registries (config, mappers, entity locations) that
/// are switched automatically when the active game changes.
/// </summary>
public class GameContext
{
    public IGameMemoryMap Map { get; set; } = null!;
    public IntPtr DllBase { get; set; }
    public int GameVersion { get; set; } // 0=TR1, 1=TR2, 2=TR3

    // --- Active per-game data (switched on game change) ---
    public GameConfig? ActiveConfig { get; private set; }
    public ItemMapper? ActiveItemMapper { get; private set; }
    public LocationMapper? ActiveLocationMapper { get; private set; }
    public Dictionary<int, Dictionary<int, long>> ActiveEntityLocations { get; private set; } = new();

    // --- Per-game registries ---
    private readonly Dictionary<int, GameConfig> _configs = new();
    private readonly Dictionary<int, ItemMapper> _itemMappers = new();
    private readonly Dictionary<int, LocationMapper> _locationMappers = new();
    private readonly Dictionary<int, Dictionary<int, Dictionary<int, long>>> _entityLocations = new();

    /// <summary>
    /// Register a game's config, mappers, and entity locations.
    /// gameVersion: 0=TR1, 1=TR2, 2=TR3.
    /// </summary>
    public void RegisterGame(
        int gameVersion,
        GameConfig config,
        ItemMapper itemMapper,
        LocationMapper locationMapper,
        Dictionary<int, Dictionary<int, long>> entityLocations)
    {
        _configs[gameVersion] = config;
        _itemMappers[gameVersion] = itemMapper;
        _locationMappers[gameVersion] = locationMapper;
        _entityLocations[gameVersion] = entityLocations;
    }

    /// <summary>
    /// Updates the context for a new active game, including per-game data.
    /// </summary>
    public void SwitchGame(int gameVersion, IGameMemoryMap map, IntPtr dllBase)
    {
        GameVersion = gameVersion;
        Map = map;
        DllBase = dllBase;

        if (_configs.TryGetValue(gameVersion, out var config))
            ActiveConfig = config;
        if (_itemMappers.TryGetValue(gameVersion, out var itemMapper))
            ActiveItemMapper = itemMapper;
        if (_locationMappers.TryGetValue(gameVersion, out var locationMapper))
            ActiveLocationMapper = locationMapper;
        if (_entityLocations.TryGetValue(gameVersion, out var entityLocs))
            ActiveEntityLocations = entityLocs;
        else
            ActiveEntityLocations = new();
    }

    /// <summary>
    /// Get an ItemMapper that handles a specific AP item ID (checks all registered games).
    /// Falls back to the active game's mapper.
    /// </summary>
    public ItemMapper? GetItemMapperForId(long apItemId)
    {
        foreach (var (_, mapper) in _itemMappers)
        {
            var cat = mapper.GetCategory(apItemId);
            if (cat != ItemCategory.Unknown)
                return mapper;
        }
        return ActiveItemMapper;
    }
}
