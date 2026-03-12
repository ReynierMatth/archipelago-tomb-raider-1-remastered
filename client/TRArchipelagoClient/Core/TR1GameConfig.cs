namespace TRArchipelagoClient.Core;

/// <summary>
/// TR1 Remastered game configuration.
/// </summary>
public static class TR1GameConfig
{
    public static GameConfig Create() => new()
    {
        ApGameName = "Tomb Raider 1 Remastered",
        GameKey = "tr1",
        ModuleName = "tomb1.dll",
        DataSubDir = "1",
        LevelFiles = new[]
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
        },
        LevelBaseNames = new[]
        {
            "LEVEL1", "LEVEL2", "LEVEL3A", "LEVEL3B",
            "LEVEL4", "LEVEL5", "LEVEL6", "LEVEL7A", "LEVEL7B",
            "LEVEL8A", "LEVEL8B", "LEVEL8C",
            "LEVEL10A", "LEVEL10B", "LEVEL10C",
        },
        LevelExtensions = new[] { ".PHD", ".PDP", ".MAP" },
        SentinelFile = "LEVEL1.PHD",
        ItemBaseId = 770_000,
        TrapBaseId = 769_000,
        LocationBaseId = 780_000,
        SecretBaseId = 790_000,
        LevelCompleteBaseId = 795_000,
        ApTags = new[] { "TR1R", "DeathLink" },
        GenericItems = new Dictionary<int, GenericItemInfo>
        {
            [85] = new() { Name = "Shotgun", Category = ItemCategory.Weapon },
            [86] = new() { Name = "Magnums", Category = ItemCategory.Weapon },
            [87] = new() { Name = "Uzis", Category = ItemCategory.Weapon },
            [89] = new() { Name = "Shotgun Shells", Category = ItemCategory.Ammo },
            [90] = new() { Name = "Magnum Clips", Category = ItemCategory.Ammo },
            [91] = new() { Name = "Uzi Clips", Category = ItemCategory.Ammo },
            [93] = new() { Name = "Small Medipack", Category = ItemCategory.Medipack },
            [94] = new() { Name = "Large Medipack", Category = ItemCategory.Medipack },
        },
        KeyItemLevelBases = new Dictionary<int, string>
        {
            [10000] = "LEVEL1.PHD",
            [11000] = "LEVEL2.PHD",
            [12000] = "LEVEL3A.PHD",
            [13000] = "LEVEL3B.PHD",
            [14000] = "LEVEL4.PHD",
            [15000] = "LEVEL5.PHD",
            [16000] = "LEVEL6.PHD",
            [17000] = "LEVEL7A.PHD",
            [18000] = "LEVEL7B.PHD",
            [19000] = "LEVEL8A.PHD",
            [20000] = "LEVEL8B.PHD",
            [21000] = "LEVEL8C.PHD",
            [22000] = "LEVEL10A.PHD",
            [23000] = "LEVEL10B.PHD",
            [24000] = "LEVEL10C.PHD",
        },
    };
}
