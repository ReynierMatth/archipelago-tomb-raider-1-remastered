namespace TRArchipelagoClient.Core;

/// <summary>
/// Maps between Archipelago location IDs and game level/entity coordinates.
///
/// ID Schema (per game):
///   Pickup locations:    locationBase + level_index * 1000 + entity_index
///   Secret locations:    secretBase + level_index * 10 + secret_index
///   Level completion:    levelCompleteBase + level_index
/// </summary>
public class LocationMapper
{
    private readonly GameConfig _config;

    public enum LocationType
    {
        Pickup,
        Secret,
        LevelComplete,
        Unknown,
    }

    public LocationMapper(GameConfig config)
    {
        _config = config;
    }

    public long GetPickupLocationId(int levelIndex, int entityIndex)
        => _config.LocationBaseId + levelIndex * 1000 + entityIndex;

    public long GetSecretLocationId(int levelIndex, int secretIndex)
        => _config.SecretBaseId + levelIndex * 10 + secretIndex;

    public long GetLevelCompleteId(int levelIndex)
        => _config.LevelCompleteBaseId + levelIndex;

    public LocationType GetLocationType(long locationId)
    {
        if (locationId >= _config.LevelCompleteBaseId)
            return LocationType.LevelComplete;
        if (locationId >= _config.SecretBaseId)
            return LocationType.Secret;
        if (locationId >= _config.LocationBaseId)
            return LocationType.Pickup;
        return LocationType.Unknown;
    }

    public (int levelIndex, int entityIndex) ParsePickupLocation(long locationId)
    {
        int offset = (int)(locationId - _config.LocationBaseId);
        return (offset / 1000, offset % 1000);
    }

    public (int levelIndex, int secretIndex) ParseSecretLocation(long locationId)
    {
        int offset = (int)(locationId - _config.SecretBaseId);
        return (offset / 10, offset % 10);
    }

    public int ParseLevelComplete(long locationId)
    {
        return (int)(locationId - _config.LevelCompleteBaseId);
    }

    public int GetLevelIndex(string levelFile)
    {
        return Array.IndexOf(_config.LevelFiles, levelFile.ToUpperInvariant());
    }

    public string GetLevelFile(int index)
    {
        return index >= 0 && index < _config.LevelFiles.Length ? _config.LevelFiles[index] : null;
    }
}
