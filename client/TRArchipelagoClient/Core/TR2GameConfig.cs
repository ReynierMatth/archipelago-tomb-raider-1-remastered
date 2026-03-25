namespace TRArchipelagoClient.Core;

/// <summary>
/// TR2 Remastered game configuration.
/// </summary>
public static class TR2GameConfig
{
    public static GameConfig Create() => new()
    {
        ApGameName = "Tomb Raider Remastered",
        GameKey = "tr2",
        ModuleName = "tomb2.dll",
        DataSubDir = "2",
        LevelFiles = new[]
        {
            "WALL.TR2",      // 0  Great Wall
            "BOAT.TR2",      // 1  Venice
            "VENICE.TR2",    // 2  Bartoli's Hideout
            "OPERA.TR2",     // 3  Opera House
            "RIG.TR2",       // 4  Oil Rig
            "PLATFORM.TR2",  // 5  Diving Area
            "UNWATER.TR2",   // 6  The Fathoms
            "KEEL.TR2",      // 7  Maria Doria
            "LIVING.TR2",    // 8  Living Quarters
            "DECK.TR2",      // 9  The Deck
            "SKIDOO.TR2",    // 10 Tibet
            "MONASTRY.TR2",  // 11 Barkhang Monastery
            "CATACOMB.TR2",  // 12 Catacombs of the Talion
            "ICECAVE.TR2",   // 13 Ice Palace
            "EMPRTOMB.TR2",  // 14 Xian Caves
            "FLOATING.TR2",  // 15 Dragon's Lair
            "XIAN.TR2",      // 16 House of the Spirit
            "HOUSE.TR2",     // 17 Home Sweet Home
        },
        LevelBaseNames = new[]
        {
            "WALL", "BOAT", "VENICE", "OPERA",
            "RIG", "PLATFORM", "UNWATER", "KEEL", "LIVING",
            "DECK", "SKIDOO", "MONASTRY", "CATACOMB",
            "ICECAVE", "EMPRTOMB", "FLOATING", "XIAN", "HOUSE",
        },
        LevelExtensions = new[] { ".TR2" },
        SentinelFile = "WALL.TR2",
        ItemBaseId = 870_000,
        TrapBaseId = 869_000,
        LocationBaseId = 880_000,
        SecretBaseId = 890_000,
        LevelCompleteBaseId = 895_000,
        ApTags = new[] { "TR2R", "DeathLink" },
        GenericItems = new Dictionary<int, GenericItemInfo>
        {
            [136] = new() { Name = "Shotgun", Category = ItemCategory.Weapon },
            [137] = new() { Name = "Auto Pistols", Category = ItemCategory.Weapon },
            [138] = new() { Name = "Uzis", Category = ItemCategory.Weapon },
            [139] = new() { Name = "Harpoon Gun", Category = ItemCategory.Weapon },
            [140] = new() { Name = "M16", Category = ItemCategory.Weapon },
            [141] = new() { Name = "Grenade Launcher", Category = ItemCategory.Weapon },
            [143] = new() { Name = "Shotgun Shells", Category = ItemCategory.Ammo },
            [144] = new() { Name = "Auto Clips", Category = ItemCategory.Ammo },
            [145] = new() { Name = "Uzi Clips", Category = ItemCategory.Ammo },
            [146] = new() { Name = "Harpoons", Category = ItemCategory.Ammo },
            [147] = new() { Name = "M16 Clips", Category = ItemCategory.Ammo },
            [148] = new() { Name = "Grenades", Category = ItemCategory.Ammo },
            [149] = new() { Name = "Small Medipack", Category = ItemCategory.Medipack },
            [150] = new() { Name = "Large Medipack", Category = ItemCategory.Medipack },
            [151] = new() { Name = "Flares", Category = ItemCategory.Medipack },
        },
        KeyItemLevelBases = new Dictionary<int, string>
        {
            [10000] = "WALL.TR2",
            [11000] = "BOAT.TR2",
            [12000] = "VENICE.TR2",
            [13000] = "OPERA.TR2",
            [14000] = "RIG.TR2",
            [15000] = "PLATFORM.TR2",
            [16000] = "UNWATER.TR2",
            [17000] = "KEEL.TR2",
            [18000] = "LIVING.TR2",
            [19000] = "DECK.TR2",
            [20000] = "SKIDOO.TR2",
            [21000] = "MONASTRY.TR2",
            [22000] = "CATACOMB.TR2",
            [23000] = "ICECAVE.TR2",
            [24000] = "EMPRTOMB.TR2",
            [25000] = "FLOATING.TR2",
            [26000] = "XIAN.TR2",
            [27000] = "HOUSE.TR2",
        },
    };
}
