namespace TRArchipelagoClient.Core;

/// <summary>
/// TR3 Remastered game configuration.
/// </summary>
public static class TR3GameConfig
{
    public static GameConfig Create() => new()
    {
        ApGameName = "Tomb Raider Remastered",
        GameKey = "tr3",
        ModuleName = "tomb3.dll",
        DataSubDir = "3",
        LevelFiles = new[]
        {
            "JUNGLE.TR2",    // 0  Jungle
            "TEMPLE.TR2",    // 1  Temple Ruins
            "QUADCHAS.TR2",  // 2  River Ganges
            "TONYBOSS.TR2",  // 3  Caves of Kaliya
            "SHORE.TR2",     // 4  Coastal Village
            "CRASH.TR2",     // 5  Crash Site
            "RAPIDS.TR2",    // 6  Madubu Gorge
            "TRIBOSS.TR2",   // 7  Temple of Puna
            "ROOFS.TR2",     // 8  Thames Wharf
            "SEWER.TR2",     // 9  Aldwych
            "TOWER.TR2",     // 10 Lud's Gate
            "OFFICE.TR2",    // 11 City
            "STPAUL.TR2",    // 12 All Hallows
            "NEVADA.TR2",    // 13 Nevada Desert
            "COMPOUND.TR2",  // 14 High Security Compound
            "AREA51.TR2",    // 15 Area 51
            "ANTARC.TR2",    // 16 Antarctica
            "MINES.TR2",     // 17 RX-Tech Mines
            "CITY.TR2",      // 18 Lost City of Tinnos
            "CHAMBER.TR2",   // 19 Meteorite Cavern
        },
        LevelBaseNames = new[]
        {
            "JUNGLE", "TEMPLE", "QUADCHAS", "TONYBOSS",
            "SHORE", "CRASH", "RAPIDS", "TRIBOSS",
            "ROOFS", "SEWER", "TOWER", "OFFICE",
            "STPAUL", "NEVADA", "COMPOUND", "AREA51",
            "ANTARC", "MINES", "CITY", "CHAMBER",
        },
        LevelExtensions = new[] { ".TR2" },
        SentinelFile = "JUNGLE.TR2",
        ItemBaseId = 970_000,
        TrapBaseId = 969_000,
        LocationBaseId = 980_000,
        SecretBaseId = 990_000,
        LevelCompleteBaseId = 995_000,
        ApTags = new[] { "TR3R", "DeathLink" },
        GenericItems = new Dictionary<int, GenericItemInfo>
        {
            [161] = new() { Name = "Shotgun", Category = ItemCategory.Weapon },
            [162] = new() { Name = "Desert Eagle", Category = ItemCategory.Weapon },
            [163] = new() { Name = "Uzis", Category = ItemCategory.Weapon },
            [164] = new() { Name = "Harpoon Gun", Category = ItemCategory.Weapon },
            [165] = new() { Name = "MP5", Category = ItemCategory.Weapon },
            [166] = new() { Name = "Rocket Launcher", Category = ItemCategory.Weapon },
            [167] = new() { Name = "Grenade Launcher", Category = ItemCategory.Weapon },
            [169] = new() { Name = "Shotgun Shells", Category = ItemCategory.Ammo },
            [170] = new() { Name = "Desert Eagle Clips", Category = ItemCategory.Ammo },
            [171] = new() { Name = "Uzi Clips", Category = ItemCategory.Ammo },
            [172] = new() { Name = "Harpoons", Category = ItemCategory.Ammo },
            [173] = new() { Name = "MP5 Clips", Category = ItemCategory.Ammo },
            [174] = new() { Name = "Rockets", Category = ItemCategory.Ammo },
            [175] = new() { Name = "Grenades", Category = ItemCategory.Ammo },
            [176] = new() { Name = "Small Medipack", Category = ItemCategory.Medipack },
            [177] = new() { Name = "Large Medipack", Category = ItemCategory.Medipack },
            [178] = new() { Name = "Flares", Category = ItemCategory.Medipack },
        },
        KeyItemLevelBases = new Dictionary<int, string>
        {
            [10000] = "JUNGLE.TR2",
            [11000] = "TEMPLE.TR2",
            [12000] = "QUADCHAS.TR2",
            [13000] = "TONYBOSS.TR2",
            [14000] = "SHORE.TR2",
            [15000] = "CRASH.TR2",
            [16000] = "RAPIDS.TR2",
            [17000] = "TRIBOSS.TR2",
            [18000] = "ROOFS.TR2",
            [19000] = "SEWER.TR2",
            [20000] = "TOWER.TR2",
            [21000] = "OFFICE.TR2",
            [22000] = "STPAUL.TR2",
            [23000] = "NEVADA.TR2",
            [24000] = "COMPOUND.TR2",
            [25000] = "AREA51.TR2",
            [26000] = "ANTARC.TR2",
            [27000] = "MINES.TR2",
            [28000] = "CITY.TR2",
            [29000] = "CHAMBER.TR2",
        },
    };
}
