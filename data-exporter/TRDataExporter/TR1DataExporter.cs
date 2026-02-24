using Newtonsoft.Json;
using TRLevelControl.Helpers;
using TRLevelControl.Model;

namespace TRDataExporter;

public class TR1DataExporter
{
    private readonly string _resourceBase;

    private static readonly Dictionary<string, string> _levelDisplayNames = new()
    {
        [TR1LevelNames.CAVES]      = "Caves",
        [TR1LevelNames.VILCABAMBA] = "City of Vilcabamba",
        [TR1LevelNames.VALLEY]     = "Lost Valley",
        [TR1LevelNames.QUALOPEC]   = "Tomb of Qualopec",
        [TR1LevelNames.FOLLY]      = "St. Francis' Folly",
        [TR1LevelNames.COLOSSEUM]  = "Colosseum",
        [TR1LevelNames.MIDAS]      = "Palace Midas",
        [TR1LevelNames.CISTERN]    = "The Cistern",
        [TR1LevelNames.TIHOCAN]    = "Tomb of Tihocan",
        [TR1LevelNames.KHAMOON]    = "City of Khamoon",
        [TR1LevelNames.OBELISK]    = "Obelisk of Khamoon",
        [TR1LevelNames.SANCTUARY]  = "Sanctuary of the Scion",
        [TR1LevelNames.MINES]      = "Natla's Mines",
        [TR1LevelNames.ATLANTIS]   = "Atlantis",
        [TR1LevelNames.PYRAMID]    = "The Great Pyramid",
    };

    private static readonly Dictionary<string, string> _levelRegions = new()
    {
        [TR1LevelNames.CAVES]      = "Peru",
        [TR1LevelNames.VILCABAMBA] = "Peru",
        [TR1LevelNames.VALLEY]     = "Peru",
        [TR1LevelNames.QUALOPEC]   = "Peru",
        [TR1LevelNames.FOLLY]      = "Greece",
        [TR1LevelNames.COLOSSEUM]  = "Greece",
        [TR1LevelNames.MIDAS]      = "Greece",
        [TR1LevelNames.CISTERN]    = "Greece",
        [TR1LevelNames.TIHOCAN]    = "Greece",
        [TR1LevelNames.KHAMOON]    = "Egypt",
        [TR1LevelNames.OBELISK]    = "Egypt",
        [TR1LevelNames.SANCTUARY]  = "Egypt",
        [TR1LevelNames.MINES]      = "Atlantis",
        [TR1LevelNames.ATLANTIS]   = "Atlantis",
        [TR1LevelNames.PYRAMID]    = "Atlantis",
    };

    // Map key item alias enum values to their string names.
    // The alias value encodes: level_base + entity_type_value
    // e.g. Vilcabamba_K1_SilverKey = 11183 = 11000 (Vilcabamba base) + 183 (not exact, it's the entity index)
    private static readonly Dictionary<TR1Type, string> _keyItemAliasNames = new()
    {
        [TR1Type.Vilcabamba_K1_SilverKey]       = "Vilcabamba Silver Key",
        [TR1Type.Vilcabamba_P1_GoldIdol]        = "Vilcabamba Gold Idol",
        [TR1Type.Valley_P1_CogAbovePool]        = "Lost Valley Cog (Above Pool)",
        [TR1Type.Valley_P1_CogBridge]           = "Lost Valley Cog (Bridge)",
        [TR1Type.Valley_P1_CogTemple]           = "Lost Valley Cog (Temple)",
        [TR1Type.Folly_K1_NeptuneKey]           = "Folly Neptune Key",
        [TR1Type.Folly_K2_AtlasKey]             = "Folly Atlas Key",
        [TR1Type.Folly_K3_DamoclesKey]          = "Folly Damocles Key",
        [TR1Type.Folly_K4_ThorKey]              = "Folly Thor Key",
        [TR1Type.Colosseum_K1_RustyKey]         = "Colosseum Rusty Key",
        [TR1Type.Midas_LeadBar_FireRoom]        = "Midas Lead Bar (Fire Room)",
        [TR1Type.Midas_LeadBar_SpikeRoom]       = "Midas Lead Bar (Spike Room)",
        [TR1Type.Midas_LeadBar_TempleRoof]      = "Midas Lead Bar (Temple Roof)",
        [TR1Type.Cistern_K1_GoldKey]            = "Cistern Gold Key",
        [TR1Type.Cistern_K2_SilverBehindDoor]   = "Cistern Silver Key (Behind Door)",
        [TR1Type.Cistern_K2_SilverBetweenDoors] = "Cistern Silver Key (Between Doors)",
        [TR1Type.Cistern_K3_RustyKeyMainRoom]   = "Cistern Rusty Key (Main Room)",
        [TR1Type.Cistern_K3_RustyKeyNearPierre] = "Cistern Rusty Key (Near Pierre)",
        [TR1Type.Tihocan_K1_GoldKeyFlipMap]     = "Tihocan Gold Key (Flip Map)",
        [TR1Type.Tihocan_K1_GoldKeyPierre]      = "Tihocan Gold Key (Pierre)",
        [TR1Type.Tihocan_K2_RustyKeyBoulders]   = "Tihocan Rusty Key (Boulders)",
        [TR1Type.Tihocan_K2_RustyKeyClangClang] = "Tihocan Rusty Key (Clang Clang)",
        [TR1Type.Tihocan_Scion_EndRoom]         = "Tihocan Scion",
        [TR1Type.Khamoon_K1_SapphireKeyEnd]     = "Khamoon Sapphire Key (End)",
        [TR1Type.Khamoon_K1_SapphireKeyStart]   = "Khamoon Sapphire Key (Start)",
        [TR1Type.Obelisk_K1_SapphireKeyEnd]     = "Obelisk Sapphire Key (End)",
        [TR1Type.Obelisk_K1_SapphireKeyStart]   = "Obelisk Sapphire Key (Start)",
        [TR1Type.Obelisk_P1_EyeOfHorus]         = "Obelisk Eye of Horus",
        [TR1Type.Obelisk_P2_Scarab]             = "Obelisk Scarab",
        [TR1Type.Obelisk_P3_SealOfAnubis]       = "Obelisk Seal of Anubis",
        [TR1Type.Obelisk_P4_Ankh]               = "Obelisk Ankh",
        [TR1Type.Sanctuary_K1_GoldKey]          = "Sanctuary Gold Key",
        [TR1Type.Sanctuary_P1_AnkhAfterKey]     = "Sanctuary Ankh (After Key)",
        [TR1Type.Sanctuary_P1_AnkhBehindSphinx] = "Sanctuary Ankh (Behind Sphinx)",
        [TR1Type.Sanctuary_P2_Scarab]           = "Sanctuary Scarab",
        [TR1Type.Mines_K1_RustyKey]             = "Mines Rusty Key",
        [TR1Type.Mines_P1_BoulderFuse]          = "Mines Fuse (Boulder)",
        [TR1Type.Mines_P1_ConveyorFuse]         = "Mines Fuse (Conveyor)",
        [TR1Type.Mines_P1_CowboyFuse]           = "Mines Fuse (Cowboy)",
        [TR1Type.Mines_P1_CowboyAltFuse]        = "Mines Fuse (Cowboy Alt)",
        [TR1Type.Mines_P2_PyramidKey]           = "Mines Pyramid Key",
    };

    // Map key item alias bases to level file names
    private static readonly Dictionary<uint, string> _keyItemBaseLevels = new()
    {
        [10000] = TR1LevelNames.CAVES,
        [11000] = TR1LevelNames.VILCABAMBA,
        [12000] = TR1LevelNames.VALLEY,
        [13000] = TR1LevelNames.QUALOPEC,
        [14000] = TR1LevelNames.FOLLY,
        [15000] = TR1LevelNames.COLOSSEUM,
        [16000] = TR1LevelNames.MIDAS,
        [17000] = TR1LevelNames.CISTERN,
        [18000] = TR1LevelNames.TIHOCAN,
        [19000] = TR1LevelNames.KHAMOON,
        [20000] = TR1LevelNames.OBELISK,
        [21000] = TR1LevelNames.SANCTUARY,
        [22000] = TR1LevelNames.MINES,
        [23000] = TR1LevelNames.ATLANTIS,
        [24000] = TR1LevelNames.PYRAMID,
    };

    public TR1DataExporter(string resourceBase)
    {
        _resourceBase = resourceBase;
    }

    public TR1ArchipelagoData Export()
    {
        var data = new TR1ArchipelagoData();
        var levels = TR1LevelNames.AsList;
        var routes = LoadRoutes();
        var secretMappings = LoadSecretMappings(levels);

        var keyItemTypes = TR1TypeUtilities.GetKeyItemTypes();
        var standardPickupTypes = TR1TypeUtilities.GetStandardPickupTypes();
        var weaponTypes = TR1TypeUtilities.GetWeaponPickups();

        // Build item definitions
        BuildItemDefinitions(data);

        // Build key dependencies from alias enum
        BuildKeyDependencies(data);

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
                Region = region
            };

            // Export pickups and key items from known TR1Type entities
            ExportEntitiesFromEnum(levelData, levelFile, keyItemTypes, standardPickupTypes, weaponTypes);

            // Export secrets from secret mapping files
            if (secretMappings.TryGetValue(levelFile, out var secretMapping))
            {
                ExportSecrets(levelData, secretMapping);
            }

            // Export routes
            if (routes.TryGetValue(levelFile, out var levelRoutes))
            {
                ExportRoutes(levelData, levelRoutes);
            }

            data.Levels.Add(levelData);
        }

        data.LevelSequence = levels;

        return data;
    }

    private void ExportEntitiesFromEnum(
        LevelData levelData,
        string levelFile,
        List<TR1Type> keyItemTypes,
        List<TR1Type> standardPickupTypes,
        List<TR1Type> weaponTypes)
    {
        // We can't read the actual level files without the game data.
        // Instead, we build the data from the enum aliases and type utilities.
        // The actual entity indices will be filled in when we can read level files.

        // Key items: find all aliases that belong to this level
        uint levelBase = GetKeyItemBase(levelFile);
        if (levelBase > 0)
        {
            foreach (var kvp in _keyItemAliasNames)
            {
                uint aliasValue = (uint)kvp.Key;
                uint aliasBase = (aliasValue / 1000) * 1000;
                if (aliasBase == levelBase)
                {
                    levelData.KeyItems.Add(new KeyItemData
                    {
                        EntityIndex = -1, // Will be resolved from level data
                        Type = kvp.Key.ToString(),
                        Alias = kvp.Key.ToString(),
                        Name = kvp.Value,
                    });
                }
            }
        }

        // Standard pickups: we know the types but not entity indices without level files.
        // We'll list the available types for this level.
        foreach (var pickupType in standardPickupTypes)
        {
            string category;
            if (weaponTypes.Contains(pickupType))
                category = "weapon";
            else if (TR1TypeUtilities.IsAmmoPickup(pickupType))
                category = "ammo";
            else if (TR1TypeUtilities.IsMediType(pickupType))
                category = pickupType == TR1Type.LargeMed_S_P ? "large_medipack" : "small_medipack";
            else
                category = "pickup";

            // Add as a template - actual instances come from level files
            levelData.Pickups.Add(new PickupData
            {
                EntityIndex = -1,
                Type = TR1TypeUtilities.GetName(pickupType),
                Category = category,
            });
        }
    }

    private void ExportSecrets(LevelData levelData, SecretMappingData secretMapping)
    {
        for (int i = 0; i < secretMapping.Rooms.Count; i++)
        {
            levelData.Secrets.Add(new SecretData
            {
                Index = i,
                RewardEntities = secretMapping.RewardEntities ?? new List<int>(),
            });
        }
    }

    private void ExportRoutes(LevelData levelData, List<RouteEntry> levelRoutes)
    {
        foreach (var route in levelRoutes)
        {
            levelData.Routes.Add(new RouteData
            {
                X = route.X,
                Y = route.Y,
                Z = route.Z,
                Room = route.Room,
                KeyItemsLow = route.KeyItemsLow,
                KeyItemsHigh = route.KeyItemsHigh,
                Range = route.Range,
                RequiresReturnPath = route.RequiresReturnPath,
            });
        }
    }

    private void BuildItemDefinitions(TR1ArchipelagoData data)
    {
        int baseId = 770000;

        // Key items (progression)
        foreach (var kvp in _keyItemAliasNames)
        {
            data.ItemDefinitions[kvp.Key.ToString()] = new ItemDefinition
            {
                Id = baseId + (int)(uint)kvp.Key,
                Name = kvp.Value,
                Category = "key_item",
                ApClassification = "progression",
            };
        }

        // Weapons (useful)
        var weapons = new Dictionary<TR1Type, string>
        {
            [TR1Type.Shotgun_S_P] = "Shotgun",
            [TR1Type.Magnums_S_P] = "Magnums",
            [TR1Type.Uzis_S_P] = "Uzis",
        };
        foreach (var kvp in weapons)
        {
            data.ItemDefinitions[kvp.Key.ToString()] = new ItemDefinition
            {
                Id = baseId + (int)(uint)kvp.Key,
                Name = kvp.Value,
                Category = "weapon",
                ApClassification = "useful",
            };
        }

        // Ammo (filler)
        var ammo = new Dictionary<TR1Type, string>
        {
            [TR1Type.ShotgunAmmo_S_P] = "Shotgun Shells",
            [TR1Type.MagnumAmmo_S_P] = "Magnum Clips",
            [TR1Type.UziAmmo_S_P] = "Uzi Clips",
        };
        foreach (var kvp in ammo)
        {
            data.ItemDefinitions[kvp.Key.ToString()] = new ItemDefinition
            {
                Id = baseId + (int)(uint)kvp.Key,
                Name = kvp.Value,
                Category = "ammo",
                ApClassification = "filler",
            };
        }

        // Medipacks
        data.ItemDefinitions[TR1Type.LargeMed_S_P.ToString()] = new ItemDefinition
        {
            Id = baseId + (int)(uint)TR1Type.LargeMed_S_P,
            Name = "Large Medipack",
            Category = "large_medipack",
            ApClassification = "useful",
        };
        data.ItemDefinitions[TR1Type.SmallMed_S_P.ToString()] = new ItemDefinition
        {
            Id = baseId + (int)(uint)TR1Type.SmallMed_S_P,
            Name = "Small Medipack",
            Category = "small_medipack",
            ApClassification = "filler",
        };
    }

    private void BuildKeyDependencies(TR1ArchipelagoData data)
    {
        foreach (var kvp in _keyItemAliasNames)
        {
            uint aliasValue = (uint)kvp.Key;
            uint aliasBase = (aliasValue / 1000) * 1000;

            if (_keyItemBaseLevels.TryGetValue(aliasBase, out string levelFile))
            {
                data.KeyDependencies[kvp.Key.ToString()] = new KeyDependency
                {
                    Level = levelFile,
                    BaseType = GetBaseKeyItemType(kvp.Key),
                };
            }
        }
    }

    private static string GetBaseKeyItemType(TR1Type aliasType)
    {
        string name = aliasType.ToString();
        if (name.Contains("_K1") || name.Contains("_K2") || name.Contains("_K3") || name.Contains("_K4"))
            return "Key";
        if (name.Contains("_P1") || name.Contains("_P2") || name.Contains("_P3") || name.Contains("_P4"))
            return "Puzzle";
        if (name.Contains("_Scion"))
            return "Scion";
        if (name.Contains("_LeadBar"))
            return "LeadBar";
        return "Unknown";
    }

    private static uint GetKeyItemBase(string levelFile)
    {
        foreach (var kvp in _keyItemBaseLevels)
        {
            if (kvp.Value == levelFile)
                return kvp.Key;
        }
        return 0;
    }

    private Dictionary<string, List<RouteEntry>> LoadRoutes()
    {
        string routesPath = Path.Combine(_resourceBase, "Locations", "routes.json");
        if (!File.Exists(routesPath))
        {
            Console.WriteLine($"Warning: routes.json not found at {routesPath}");
            return new();
        }

        string json = File.ReadAllText(routesPath);
        return JsonConvert.DeserializeObject<Dictionary<string, List<RouteEntry>>>(json) ?? new();
    }

    private Dictionary<string, SecretMappingData> LoadSecretMappings(List<string> levels)
    {
        var mappings = new Dictionary<string, SecretMappingData>();
        string secretDir = Path.Combine(_resourceBase, "SecretMapping");

        foreach (string level in levels)
        {
            string path = Path.Combine(secretDir, $"{level}-SecretMapping.json");
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var mapping = JsonConvert.DeserializeObject<SecretMappingData>(json);
                if (mapping != null)
                {
                    mappings[level] = mapping;
                }
            }
        }

        return mappings;
    }
}

// JSON deserialization models for routes.json
public class RouteEntry
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Room { get; set; }
    public string KeyItemsLow { get; set; }
    public string KeyItemsHigh { get; set; }
    public string Range { get; set; }
    public bool RequiresReturnPath { get; set; }
    public bool Validated { get; set; }
}

// JSON deserialization models for secret mappings
public class SecretMappingData
{
    public List<int> RewardEntities { get; set; }
    public List<SecretRoom> Rooms { get; set; } = new();
}

public class SecretRoom
{
    public List<SecretPosition> RewardPositions { get; set; } = new();
}

public class SecretPosition
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}
