namespace TRArchipelagoClient.Core;

/// <summary>
/// Maps between Archipelago location IDs and TR1 level/entity coordinates.
///
/// ID Schema:
///   Pickup locations:    780000 + level_index * 1000 + entity_index
///   Secret locations:    790000 + level_index * 10 + secret_index
///   Level completion:    795000 + level_index
/// </summary>
public static class LocationMapper
{
    private const int PickupBaseId = 780_000;
    private const int SecretBaseId = 790_000;
    private const int LevelCompleteBaseId = 795_000;

    private static readonly string[] LevelFiles =
    {
        "LEVEL1.PHD",   // 0  Caves
        "LEVEL2.PHD",   // 1  Vilcabamba
        "LEVEL3A.PHD",  // 2  Lost Valley
        "LEVEL3B.PHD",  // 3  Qualopec
        "LEVEL4.PHD",   // 4  Folly
        "LEVEL5.PHD",   // 5  Colosseum
        "LEVEL6.PHD",   // 6  Midas
        "LEVEL7A.PHD",  // 7  Cistern
        "LEVEL7B.PHD",  // 8  Tihocan
        "LEVEL8A.PHD",  // 9  Khamoon
        "LEVEL8B.PHD",  // 10 Obelisk
        "LEVEL8C.PHD",  // 11 Sanctuary
        "LEVEL10A.PHD", // 12 Mines
        "LEVEL10B.PHD", // 13 Atlantis
        "LEVEL10C.PHD", // 14 Pyramid
    };

    public enum LocationType
    {
        Pickup,
        Secret,
        LevelComplete,
        Unknown,
    }

    public static long GetPickupLocationId(int levelIndex, int entityIndex)
        => PickupBaseId + levelIndex * 1000 + entityIndex;

    public static long GetSecretLocationId(int levelIndex, int secretIndex)
        => SecretBaseId + levelIndex * 10 + secretIndex;

    public static long GetLevelCompleteId(int levelIndex)
        => LevelCompleteBaseId + levelIndex;

    public static LocationType GetLocationType(long locationId)
    {
        if (locationId >= LevelCompleteBaseId)
            return LocationType.LevelComplete;
        if (locationId >= SecretBaseId)
            return LocationType.Secret;
        if (locationId >= PickupBaseId)
            return LocationType.Pickup;
        return LocationType.Unknown;
    }

    public static (int levelIndex, int entityIndex) ParsePickupLocation(long locationId)
    {
        int offset = (int)(locationId - PickupBaseId);
        return (offset / 1000, offset % 1000);
    }

    public static (int levelIndex, int secretIndex) ParseSecretLocation(long locationId)
    {
        int offset = (int)(locationId - SecretBaseId);
        return (offset / 10, offset % 10);
    }

    public static int ParseLevelComplete(long locationId)
    {
        return (int)(locationId - LevelCompleteBaseId);
    }

    public static int GetLevelIndex(string levelFile)
    {
        return Array.IndexOf(LevelFiles, levelFile.ToUpperInvariant());
    }

    public static string GetLevelFile(int index)
    {
        return index >= 0 && index < LevelFiles.Length ? LevelFiles[index] : null;
    }
}
