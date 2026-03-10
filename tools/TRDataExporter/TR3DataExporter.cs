using TRLevelControl;
using TRLevelControl.Helpers;
using TRLevelControl.Model;

namespace TRDataExporter;

public class TR3DataExporter
{
    private readonly string _gameDataDir;

    private static readonly Dictionary<string, string> _levelDisplayNames = new()
    {
        [TR3LevelNames.JUNGLE]  = "Jungle",
        [TR3LevelNames.RUINS]   = "Temple Ruins",
        [TR3LevelNames.GANGES]  = "River Ganges",
        [TR3LevelNames.CAVES]   = "Caves of Kaliya",
        [TR3LevelNames.COASTAL] = "Coastal Village",
        [TR3LevelNames.CRASH]   = "Crash Site",
        [TR3LevelNames.MADUBU]  = "Madubu Gorge",
        [TR3LevelNames.PUNA]    = "Temple of Puna",
        [TR3LevelNames.THAMES]  = "Thames Wharf",
        [TR3LevelNames.ALDWYCH] = "Aldwych",
        [TR3LevelNames.LUDS]    = "Lud's Gate",
        [TR3LevelNames.CITY]    = "City",
        [TR3LevelNames.NEVADA]  = "Nevada Desert",
        [TR3LevelNames.HSC]     = "High Security Compound",
        [TR3LevelNames.AREA51]  = "Area 51",
        [TR3LevelNames.ANTARC]  = "Antarctica",
        [TR3LevelNames.RXTECH]  = "RX-Tech Mines",
        [TR3LevelNames.TINNOS]  = "Lost City of Tinnos",
        [TR3LevelNames.WILLIE]  = "Meteorite Cavern",
        [TR3LevelNames.HALLOWS] = "All Hallows",
        // Gold / The Lost Artifact
        [TR3LevelNames.FLING]    = "Highland Fling",
        [TR3LevelNames.LAIR]     = "Willard's Lair",
        [TR3LevelNames.CLIFF]    = "Shakespeare Cliff",
        [TR3LevelNames.FISHES]   = "Sleeping with the Fishes",
        [TR3LevelNames.MADHOUSE] = "It's a Madhouse!",
        [TR3LevelNames.REUNION]  = "Reunion",
    };

    private static readonly Dictionary<string, string> _levelRegions = new()
    {
        [TR3LevelNames.JUNGLE]  = "India",
        [TR3LevelNames.RUINS]   = "India",
        [TR3LevelNames.GANGES]  = "India",
        [TR3LevelNames.CAVES]   = "India",
        [TR3LevelNames.COASTAL] = "South Pacific",
        [TR3LevelNames.CRASH]   = "South Pacific",
        [TR3LevelNames.MADUBU]  = "South Pacific",
        [TR3LevelNames.PUNA]    = "South Pacific",
        [TR3LevelNames.THAMES]  = "London",
        [TR3LevelNames.ALDWYCH] = "London",
        [TR3LevelNames.LUDS]    = "London",
        [TR3LevelNames.CITY]    = "London",
        [TR3LevelNames.NEVADA]  = "Nevada",
        [TR3LevelNames.HSC]     = "Nevada",
        [TR3LevelNames.AREA51]  = "Nevada",
        [TR3LevelNames.ANTARC]  = "Antarctica",
        [TR3LevelNames.RXTECH]  = "Antarctica",
        [TR3LevelNames.TINNOS]  = "Antarctica",
        [TR3LevelNames.WILLIE]  = "Antarctica",
        [TR3LevelNames.HALLOWS] = "London",
        // Gold
        [TR3LevelNames.FLING]    = "Gold",
        [TR3LevelNames.LAIR]     = "Gold",
        [TR3LevelNames.CLIFF]    = "Gold",
        [TR3LevelNames.FISHES]   = "Gold",
        [TR3LevelNames.MADHOUSE] = "Gold",
        [TR3LevelNames.REUNION]  = "Gold",
    };

    private static readonly Dictionary<(string level, string baseType), string> _keyTypeNames = new()
    {
        { (TR3LevelNames.JUNGLE, "Key4_P"), "Indra Key" },
        { (TR3LevelNames.RUINS, "Key1_P"), "Ganesha Key" },
        { (TR3LevelNames.RUINS, "Puzzle1_P"), "Scimitar" },
        { (TR3LevelNames.RUINS, "Puzzle2_P"), "Scimitar" },
        { (TR3LevelNames.GANGES, "Key1_P"), "Gate Key" },
        { (TR3LevelNames.COASTAL, "Key1_P"), "Smuggler's Key" },
        { (TR3LevelNames.COASTAL, "Puzzle1_P"), "Serpent Stone" },
        { (TR3LevelNames.CRASH, "Key1_P"), "Bishop's Key" },
        { (TR3LevelNames.CRASH, "Key2_P"), "Tuckerman's Key" },
        { (TR3LevelNames.THAMES, "Key1_P"), "Flue Room Key" },
        { (TR3LevelNames.THAMES, "Key2_P"), "Cathedral Key" },
        { (TR3LevelNames.ALDWYCH, "Key1_P"), "Maintenance Key" },
        { (TR3LevelNames.ALDWYCH, "Key2_P"), "Solomon's Key" },
        { (TR3LevelNames.ALDWYCH, "Key3_P"), "Solomon's Key" },
        { (TR3LevelNames.ALDWYCH, "Puzzle1_P"), "Old Coin" },
        { (TR3LevelNames.ALDWYCH, "Puzzle2_P"), "Ticket" },
        { (TR3LevelNames.ALDWYCH, "Puzzle3_P"), "Hammer" },
        { (TR3LevelNames.ALDWYCH, "Puzzle4_P"), "Ornate Star" },
        { (TR3LevelNames.LUDS, "Key1_P"), "Boiler Room Key" },
        { (TR3LevelNames.LUDS, "Puzzle1_P"), "Embalming Fluid" },
        { (TR3LevelNames.NEVADA, "Key1_P"), "Generator Access Card" },
        { (TR3LevelNames.NEVADA, "Key2_P"), "Detonator Key" },
        { (TR3LevelNames.HSC, "Key1_P"), "Keycard Type A" },
        { (TR3LevelNames.HSC, "Key2_P"), "Keycard Type B" },
        { (TR3LevelNames.HSC, "Puzzle1_P"), "Blue Pass" },
        { (TR3LevelNames.HSC, "Puzzle2_P"), "Yellow Pass" },
        { (TR3LevelNames.AREA51, "Key1_P"), "Launch Code Card" },
        { (TR3LevelNames.AREA51, "Puzzle2_P"), "Code Clearance CD" },
        { (TR3LevelNames.AREA51, "Puzzle3_P"), "Code Clearance CD" },
        { (TR3LevelNames.AREA51, "Puzzle4_P"), "Hangar Access Pass" },
        { (TR3LevelNames.ANTARC, "Key1_P"), "Hut Key" },
        { (TR3LevelNames.ANTARC, "Puzzle1_P"), "Crowbar" },
        { (TR3LevelNames.ANTARC, "Puzzle2_P"), "Gate Control Key" },
        { (TR3LevelNames.RXTECH, "Puzzle1_P"), "Crowbar" },
        { (TR3LevelNames.RXTECH, "Puzzle2_P"), "Lead Acid Battery" },
        { (TR3LevelNames.RXTECH, "Puzzle3_P"), "Winch Starter" },
        { (TR3LevelNames.TINNOS, "Key1_P"), "Uli Key" },
        { (TR3LevelNames.TINNOS, "Puzzle1_P"), "Oceanic Mask" },
        { (TR3LevelNames.HALLOWS, "Key1_P"), "Vault Key" },
    };

    public TR3DataExporter(string gameDataDir)
    {
        _gameDataDir = gameDataDir;
    }

    public TRArchipelagoData Export(bool includeGold)
    {
        var data = new TRArchipelagoData { Game = "Tomb Raider 3 Remastered" };
        var levels = includeGold
            ? TR3LevelNames.AsList.Concat(TR3LevelNames.AsListGold).Distinct().ToList()
            : TR3LevelNames.AsList.Distinct().ToList();
        var reader = new TR3LevelControl();

        BuildItemDefinitions(data);

        for (int i = 0; i < levels.Count; i++)
        {
            string levelFile = levels[i];
            string displayName = _levelDisplayNames.GetValueOrDefault(levelFile, levelFile);
            string region = _levelRegions.GetValueOrDefault(levelFile, "Unknown");

            var levelData = new LevelData
            {
                Name = displayName,
                File = levelFile,
                Sequence = i + 1,
                Region = region,
            };

            string path = Path.Combine(_gameDataDir, levelFile);
            string backupPath = path + ".apbak";
            if (File.Exists(backupPath))
                path = backupPath;
            if (!File.Exists(path))
            {
                Console.WriteLine($"  WARNING: {path} not found, skipping");
                data.Levels.Add(levelData);
                continue;
            }

            TR3Level level;
            try { level = reader.Read(path); }
            catch (Exception ex)
            {
                Console.WriteLine($"  WARNING: Failed to read {levelFile}: {ex.Message}");
                data.Levels.Add(levelData);
                continue;
            }

            Console.WriteLine($"  {displayName}: {level.Entities.Count} entities");

            for (int ei = 0; ei < level.Entities.Count; ei++)
            {
                var entity = level.Entities[ei];

                if (TR3TypeUtilities.IsKeyItemType(entity.TypeID)
                    || TR3TypeUtilities.IsArtefactPickup(entity.TypeID))
                {
                    string baseName = entity.TypeID.ToString();
                    int sameTypeCount = levelData.KeyItems.Count(k => k.Type == baseName);
                    string friendlyName = BuildKeyItemName(levelFile, displayName, entity.TypeID, sameTypeCount);
                    string alias = BuildKeyItemAlias(displayName, entity.TypeID, sameTypeCount);

                    levelData.KeyItems.Add(new KeyItemData
                    {
                        EntityIndex = ei,
                        Type = baseName,
                        Alias = alias,
                        Name = friendlyName,
                        X = entity.X, Y = entity.Y, Z = entity.Z,
                        Room = entity.Room,
                    });
                }
                else if (TR3TypeUtilities.IsStandardPickupType(entity.TypeID))
                {
                    string category;
                    if (TR3TypeUtilities.IsWeaponPickup(entity.TypeID))
                        category = "weapon";
                    else if (TR3TypeUtilities.IsAmmoPickup(entity.TypeID))
                        category = "ammo";
                    else if (entity.TypeID == TR3Type.LargeMed_P)
                        category = "large_medipack";
                    else if (entity.TypeID == TR3Type.SmallMed_P)
                        category = "small_medipack";
                    else
                        category = "pickup";

                    levelData.Pickups.Add(new PickupData
                    {
                        EntityIndex = ei,
                        Type = entity.TypeID.ToString(),
                        Category = category,
                        X = entity.X, Y = entity.Y, Z = entity.Z,
                        Room = entity.Room,
                    });
                }
            }

            int secretCount = _secretCounts.GetValueOrDefault(levelFile, 0);
            for (int s = 0; s < secretCount; s++)
            {
                levelData.Secrets.Add(new SecretData { Index = s });
            }

            data.Levels.Add(levelData);
        }

        data.LevelSequence = levels;
        BuildKeyDependencies(data);
        return data;
    }

    private static readonly Dictionary<string, string> _levelShortNames = new()
    {
        ["Jungle"] = "Jungle",
        ["Temple Ruins"] = "Temple",
        ["River Ganges"] = "Ganges",
        ["Caves of Kaliya"] = "Kaliya",
        ["Coastal Village"] = "Coastal",
        ["Crash Site"] = "Crash",
        ["Madubu Gorge"] = "Madubu",
        ["Temple of Puna"] = "Puna",
        ["Thames Wharf"] = "Thames",
        ["Aldwych"] = "Aldwych",
        ["Lud's Gate"] = "Luds",
        ["City"] = "City",
        ["Nevada Desert"] = "Nevada",
        ["High Security Compound"] = "HSC",
        ["Area 51"] = "Area51",
        ["Antarctica"] = "Antarc",
        ["RX-Tech Mines"] = "RX",
        ["Lost City of Tinnos"] = "Tinnos",
        ["Meteorite Cavern"] = "Willie",
        ["All Hallows"] = "Hallows",
        ["Highland Fling"] = "Fling",
        ["Willard's Lair"] = "WLair",
        ["Shakespeare Cliff"] = "Cliff",
        ["Sleeping with the Fishes"] = "Fishes",
        ["It's a Madhouse!"] = "Madhouse",
        ["Reunion"] = "Reunion",
    };

    private static string GetKeyItemDisplayName(TR3Type alias, string baseName)
    {
        return alias switch
        {
            TR3Type.Jungle_K4_IndraKey             => "Jungle Indra Key",
            TR3Type.Temple_K1_GaneshaCurrentPool   => "Temple Ganesha Key (Current Pool)",
            TR3Type.Temple_K1_GaneshaFlipmapPool   => "Temple Ganesha Key (Flipmap Pool)",
            TR3Type.Temple_K1_GaneshaMudslide      => "Temple Ganesha Key (Mudslide)",
            TR3Type.Temple_K1_GaneshaRandyRory     => "Temple Ganesha Key (Randy Rory)",
            TR3Type.Temple_K1_GaneshaSpikeCeiling  => "Temple Ganesha Key (Spike Ceiling)",
            TR3Type.Temple_P1_ScimitarEast          => "Temple Scimitar (East)",
            TR3Type.Temple_P2_ScimitarWest          => "Temple Scimitar (West)",
            TR3Type.Ganges_K1_GateKeyMonkeyPit     => "Ganges Gate Key (Monkey Pit)",
            TR3Type.Ganges_K1_GateKeySnakePit      => "Ganges Gate Key (Snake Pit)",
            TR3Type.Coastal_K1_SmugglersKey        => "Coastal Smuggler's Key",
            TR3Type.Coastal_P1_StoneAbovePool      => "Coastal Serpent Stone (Above Pool)",
            TR3Type.Coastal_P1_StoneTreetops       => "Coastal Serpent Stone (Treetops)",
            TR3Type.Coastal_P1_StoneWaterfall      => "Coastal Serpent Stone (Waterfall)",
            TR3Type.Crash_K1_BishopsKey            => "Crash Bishop's Key",
            TR3Type.Crash_K2_TuckermansKey         => "Crash Tuckerman's Key",
            TR3Type.Thames_K1_FlueRoomKey          => "Thames Flue Room Key",
            TR3Type.Thames_K2_CathedralKey         => "Thames Cathedral Key",
            TR3Type.Aldwych_K1_MaintenanceKey      => "Aldwych Maintenance Key",
            TR3Type.Aldwych_K2_SolomonKey3Doors    => "Aldwych Solomon's Key (3 Doors)",
            TR3Type.Aldwych_K3_SolomonKeyDrill     => "Aldwych Solomon's Key (Drill)",
            TR3Type.Aldwych_P1_OldCoin             => "Aldwych Old Coin",
            TR3Type.Aldwych_P2_Ticket              => "Aldwych Ticket",
            TR3Type.Aldwych_P3_Hammer              => "Aldwych Hammer",
            TR3Type.Aldwych_P4_OrnateStar          => "Aldwych Ornate Star",
            TR3Type.Luds_K1_BoilerRoomKey          => "Luds Boiler Room Key",
            TR3Type.Luds_P1_EmbalmingFluid         => "Luds Embalming Fluid",
            TR3Type.Nevada_K1_GeneratorAccessCard  => "Nevada Generator Access Card",
            TR3Type.Nevada_K2_DetonatorKey         => "Nevada Detonator Key",
            TR3Type.Nevada_K2_DetonatorKeyUnused   => "Nevada Detonator Key (Unused)",
            TR3Type.HSC_K1_KeycardTypeA            => "HSC Keycard Type A",
            TR3Type.HSC_K2_KeycardTypeBSatellite   => "HSC Keycard Type B (Satellite)",
            TR3Type.HSC_K2_KeycardTypeBTurrets     => "HSC Keycard Type B (Turrets)",
            TR3Type.HSC_P1_BluePass                => "HSC Blue Pass",
            TR3Type.HSC_P2_YellowPassEnd           => "HSC Yellow Pass (End)",
            TR3Type.HSC_P2_YellowPassHangar        => "HSC Yellow Pass (Hangar)",
            TR3Type.HSC_P2_YellowPassSatellite     => "HSC Yellow Pass (Satellite)",
            TR3Type.Area51_K1_LaunchCodeCard       => "Area 51 Launch Code Card",
            TR3Type.Area51_P2_CodeCDSilo           => "Area 51 Code CD (Silo)",
            TR3Type.Area51_P3_CodeCDWatchTower     => "Area 51 Code CD (Watch Tower)",
            TR3Type.Area51_P4_HangarAccessPass     => "Area 51 Hangar Access Pass",
            TR3Type.Antarc_K1_HutKey               => "Antarctica Hut Key",
            TR3Type.Antarc_P1_CrowbarGateControl   => "Antarctica Crowbar (Gate Control)",
            TR3Type.Antarc_P1_CrowbarRegular       => "Antarctica Crowbar",
            TR3Type.Antarc_P1_CrowbarTower         => "Antarctica Crowbar (Tower)",
            TR3Type.Antarc_P2_GateControlKey       => "Antarctica Gate Control Key",
            TR3Type.RX_P1_Crowbar                  => "RX Crowbar",
            TR3Type.RX_P2_LeadAcidBattery          => "RX Lead Acid Battery",
            TR3Type.RX_P3_WinchStarter             => "RX Winch Starter",
            TR3Type.Tinnos_K1_UliKeyEnd            => "Tinnos Uli Key (End)",
            TR3Type.Tinnos_K1_UliKeyStart          => "Tinnos Uli Key (Start)",
            TR3Type.Tinnos_P1_OceanicMaskEarth     => "Tinnos Oceanic Mask (Earth)",
            TR3Type.Tinnos_P1_OceanicMaskFire      => "Tinnos Oceanic Mask (Fire)",
            TR3Type.Tinnos_P1_OceanicMaskWater     => "Tinnos Oceanic Mask (Water)",
            TR3Type.Tinnos_P1_OceanicMaskWind      => "Tinnos Oceanic Mask (Wind)",
            TR3Type.Hallows_K1_VaultKey            => "Hallows Vault Key",
            _ => baseName,
        };
    }

    private void BuildItemDefinitions(TRArchipelagoData data)
    {
        int baseId = 970_000;

        foreach (var alias in Enum.GetValues<TR3Type>())
        {
            string name = GetKeyItemDisplayName(alias, alias.ToString());
            if (name != alias.ToString())
            {
                data.ItemDefinitions[alias.ToString()] = new ItemDefinition
                {
                    Id = baseId + (int)(uint)alias,
                    Name = name,
                    Category = "key_item",
                    ApClassification = "progression",
                };
            }
        }

        AddItemDef(data, baseId, TR3Type.Shotgun_P, "Shotgun", "weapon", "useful");
        AddItemDef(data, baseId, TR3Type.Deagle_P, "Desert Eagle", "weapon", "useful");
        AddItemDef(data, baseId, TR3Type.Uzis_P, "Uzis", "weapon", "useful");
        AddItemDef(data, baseId, TR3Type.Harpoon_P, "Harpoon Gun", "weapon", "useful");
        AddItemDef(data, baseId, TR3Type.MP5_P, "MP5", "weapon", "useful");
        AddItemDef(data, baseId, TR3Type.RocketLauncher_P, "Rocket Launcher", "weapon", "useful");
        AddItemDef(data, baseId, TR3Type.GrenadeLauncher_P, "Grenade Launcher", "weapon", "useful");

        AddItemDef(data, baseId, TR3Type.ShotgunAmmo_P, "Shotgun Shells", "ammo", "filler");
        AddItemDef(data, baseId, TR3Type.DeagleAmmo_P, "Desert Eagle Clips", "ammo", "filler");
        AddItemDef(data, baseId, TR3Type.UziAmmo_P, "Uzi Clips", "ammo", "filler");
        AddItemDef(data, baseId, TR3Type.Harpoons_P, "Harpoons", "ammo", "filler");
        AddItemDef(data, baseId, TR3Type.MP5Ammo_P, "MP5 Clips", "ammo", "filler");
        AddItemDef(data, baseId, TR3Type.Rockets_P, "Rockets", "ammo", "filler");
        AddItemDef(data, baseId, TR3Type.Grenades_P, "Grenades", "ammo", "filler");

        AddItemDef(data, baseId, TR3Type.LargeMed_P, "Large Medipack", "large_medipack", "useful");
        AddItemDef(data, baseId, TR3Type.SmallMed_P, "Small Medipack", "small_medipack", "filler");
        AddItemDef(data, baseId, TR3Type.Flares_P, "Flares", "pickup", "filler");
    }

    private static void AddItemDef(TRArchipelagoData data, int baseId, TR3Type type, string name, string category, string classification)
    {
        data.ItemDefinitions[type.ToString()] = new ItemDefinition
        {
            Id = baseId + (int)(uint)type,
            Name = name,
            Category = category,
            ApClassification = classification,
        };
    }

    private static string BuildKeyItemName(string levelFile, string displayName, TR3Type type, int sameTypeCount)
    {
        string baseName = type.ToString();
        string suffix = baseName.EndsWith("_P") ? "_P" : "";
        string typeName = _keyTypeNames.GetValueOrDefault((levelFile, baseName), baseName.Replace(suffix, ""));
        if (sameTypeCount > 0)
            return $"{displayName} - {typeName} #{sameTypeCount + 1}";
        return $"{displayName} - {typeName}";
    }

    private static string BuildKeyItemAlias(string displayName, TR3Type type, int sameTypeCount)
    {
        string shortLevel = _levelShortNames.GetValueOrDefault(displayName, displayName.Replace(" ", ""));
        string shortType = type.ToString().Replace("_P", "");
        if (sameTypeCount > 0)
            return $"{shortLevel}_{shortType}_{sameTypeCount + 1}";
        return $"{shortLevel}_{shortType}";
    }

    private void BuildKeyDependencies(TRArchipelagoData data)
    {
        foreach (var level in data.Levels)
            foreach (var keyItem in level.KeyItems)
                data.KeyDependencies[keyItem.Alias] = new KeyDependency
                {
                    Level = level.File,
                    BaseType = keyItem.Type,
                };
    }

    private static readonly Dictionary<string, int> _secretCounts = new()
    {
        [TR3LevelNames.JUNGLE]  = 3,
        [TR3LevelNames.RUINS]   = 2,
        [TR3LevelNames.GANGES]  = 3,
        [TR3LevelNames.CAVES]   = 3,
        [TR3LevelNames.COASTAL] = 3,
        [TR3LevelNames.CRASH]   = 1,
        [TR3LevelNames.MADUBU]  = 2,
        [TR3LevelNames.PUNA]    = 1,
        [TR3LevelNames.THAMES]  = 2,
        [TR3LevelNames.ALDWYCH] = 2,
        [TR3LevelNames.LUDS]    = 2,
        [TR3LevelNames.CITY]    = 2,
        [TR3LevelNames.NEVADA]  = 2,
        [TR3LevelNames.HSC]     = 3,
        [TR3LevelNames.AREA51]  = 2,
        [TR3LevelNames.ANTARC]  = 2,
        [TR3LevelNames.RXTECH]  = 2,
        [TR3LevelNames.TINNOS]  = 2,
        [TR3LevelNames.WILLIE]  = 1,
        [TR3LevelNames.HALLOWS] = 2,
        // Gold
        [TR3LevelNames.FLING]    = 3,
        [TR3LevelNames.LAIR]     = 3,
        [TR3LevelNames.CLIFF]    = 3,
        [TR3LevelNames.FISHES]   = 3,
        [TR3LevelNames.MADHOUSE] = 3,
        [TR3LevelNames.REUNION]  = 3,
    };
}
