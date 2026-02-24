using TRLevelControl;
using TRLevelControl.Helpers;
using TRLevelControl.Model;

namespace TRDataExporter;

public class TR1DataExporter
{
    private readonly string _gameDataDir;
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

    // In-game key item names per (level, base type)
    // These are the actual names shown in the TR1 inventory
    private static readonly Dictionary<(string level, string baseType), string> _keyTypeNames = new()
    {
        // Vilcabamba
        { (TR1LevelNames.VILCABAMBA, "Key1_S_P"), "Silver Key" },
        { (TR1LevelNames.VILCABAMBA, "Puzzle1_S_P"), "Gold Idol" },
        // Valley
        { (TR1LevelNames.VALLEY, "Puzzle1_S_P"), "Machine Cog" },
        // Folly
        { (TR1LevelNames.FOLLY, "Key1_S_P"), "Neptune Key" },
        { (TR1LevelNames.FOLLY, "Key2_S_P"), "Atlas Key" },
        { (TR1LevelNames.FOLLY, "Key3_S_P"), "Damocles Key" },
        { (TR1LevelNames.FOLLY, "Key4_S_P"), "Thor Key" },
        // Colosseum
        { (TR1LevelNames.COLOSSEUM, "Key1_S_P"), "Rusty Key" },
        // Midas
        { (TR1LevelNames.MIDAS, "LeadBar_S_P"), "Lead Bar" },
        // Cistern
        { (TR1LevelNames.CISTERN, "Key1_S_P"), "Gold Key" },
        { (TR1LevelNames.CISTERN, "Key2_S_P"), "Silver Key" },
        { (TR1LevelNames.CISTERN, "Key3_S_P"), "Rusty Key" },
        // Tihocan
        { (TR1LevelNames.TIHOCAN, "Key1_S_P"), "Gold Key" },
        { (TR1LevelNames.TIHOCAN, "Key3_S_P"), "Rusty Key" },
        { (TR1LevelNames.TIHOCAN, "ScionPiece2_S_P"), "Scion" },
        // Khamoon
        { (TR1LevelNames.KHAMOON, "Key1_S_P"), "Sapphire Key" },
        // Obelisk
        { (TR1LevelNames.OBELISK, "Key1_S_P"), "Sapphire Key" },
        { (TR1LevelNames.OBELISK, "Puzzle1_S_P"), "Eye of Horus" },
        { (TR1LevelNames.OBELISK, "Puzzle2_S_P"), "Scarab" },
        { (TR1LevelNames.OBELISK, "Puzzle3_S_P"), "Seal of Anubis" },
        { (TR1LevelNames.OBELISK, "Puzzle4_S_P"), "Ankh" },
        // Sanctuary
        { (TR1LevelNames.SANCTUARY, "Key1_S_P"), "Gold Key" },
        { (TR1LevelNames.SANCTUARY, "Puzzle1_S_P"), "Ankh" },
        { (TR1LevelNames.SANCTUARY, "Puzzle2_S_P"), "Scarab" },
        // Mines
        { (TR1LevelNames.MINES, "Key1_S_P"), "Rusty Key" },
        { (TR1LevelNames.MINES, "Puzzle1_S_P"), "Fuse" },
        { (TR1LevelNames.MINES, "Puzzle2_S_P"), "Pyramid Key" },
    };

    public TR1DataExporter(string gameDataDir, string resourceBase)
    {
        _gameDataDir = gameDataDir;
        _resourceBase = resourceBase;
    }

    public TR1ArchipelagoData Export()
    {
        var data = new TR1ArchipelagoData();
        var levels = TR1LevelNames.AsList;
        var reader = new TR1LevelControl();

        // Build item definitions (AP item types and IDs)
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

            // Read actual level file
            string phdPath = Path.Combine(_gameDataDir, levelFile);
            if (!File.Exists(phdPath))
            {
                Console.WriteLine($"  WARNING: {phdPath} not found, skipping");
                data.Levels.Add(levelData);
                continue;
            }

            TR1Level level;
            try
            {
                level = reader.Read(phdPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  WARNING: Failed to read {levelFile}: {ex.Message}");
                data.Levels.Add(levelData);
                continue;
            }

            Console.WriteLine($"  {displayName}: {level.Entities.Count} entities");

            // Extract pickups and key items from actual entities
            for (int ei = 0; ei < level.Entities.Count; ei++)
            {
                var entity = level.Entities[ei];

                if (TR1TypeUtilities.IsKeyItemType(entity.TypeID)
                    || (levelFile == TR1LevelNames.TIHOCAN && entity.TypeID == TR1Type.ScionPiece2_S_P))
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
                        X = entity.X,
                        Y = entity.Y,
                        Z = entity.Z,
                        Room = entity.Room,
                    });
                }
                else if (TR1TypeUtilities.IsStandardPickupType(entity.TypeID))
                {
                    string category;
                    if (TR1TypeUtilities.IsWeaponPickup(entity.TypeID))
                        category = "weapon";
                    else if (TR1TypeUtilities.IsAmmoPickup(entity.TypeID))
                        category = "ammo";
                    else if (TR1TypeUtilities.IsMediType(entity.TypeID))
                        category = entity.TypeID == TR1Type.LargeMed_S_P ? "large_medipack" : "small_medipack";
                    else
                        category = "pickup";

                    levelData.Pickups.Add(new PickupData
                    {
                        EntityIndex = ei,
                        Type = entity.TypeID.ToString(),
                        Category = category,
                        X = entity.X,
                        Y = entity.Y,
                        Z = entity.Z,
                        Room = entity.Room,
                    });
                }
            }

            // Secrets: use known counts per level
            int secretCount = _secretCounts.GetValueOrDefault(levelFile, 0);
            for (int s = 0; s < secretCount; s++)
            {
                levelData.Secrets.Add(new SecretData
                {
                    Index = s,
                });
            }

            data.Levels.Add(levelData);
        }

        data.LevelSequence = levels;

        // Build key dependencies
        BuildKeyDependencies(data);

        return data;
    }

    private static string GetKeyItemDisplayName(TR1Type alias, string baseName)
    {
        // Known alias display names
        return alias switch
        {
            TR1Type.Vilcabamba_K1_SilverKey       => "Vilcabamba Silver Key",
            TR1Type.Vilcabamba_P1_GoldIdol        => "Vilcabamba Gold Idol",
            TR1Type.Valley_P1_CogAbovePool        => "Lost Valley Cog (Above Pool)",
            TR1Type.Valley_P1_CogBridge           => "Lost Valley Cog (Bridge)",
            TR1Type.Valley_P1_CogTemple           => "Lost Valley Cog (Temple)",
            TR1Type.Folly_K1_NeptuneKey           => "Folly Neptune Key",
            TR1Type.Folly_K2_AtlasKey             => "Folly Atlas Key",
            TR1Type.Folly_K3_DamoclesKey          => "Folly Damocles Key",
            TR1Type.Folly_K4_ThorKey              => "Folly Thor Key",
            TR1Type.Colosseum_K1_RustyKey         => "Colosseum Rusty Key",
            TR1Type.Midas_LeadBar_FireRoom        => "Midas Lead Bar (Fire Room)",
            TR1Type.Midas_LeadBar_SpikeRoom       => "Midas Lead Bar (Spike Room)",
            TR1Type.Midas_LeadBar_TempleRoof      => "Midas Lead Bar (Temple Roof)",
            TR1Type.Cistern_K1_GoldKey            => "Cistern Gold Key",
            TR1Type.Cistern_K2_SilverBehindDoor   => "Cistern Silver Key (Behind Door)",
            TR1Type.Cistern_K2_SilverBetweenDoors => "Cistern Silver Key (Between Doors)",
            TR1Type.Cistern_K3_RustyKeyMainRoom   => "Cistern Rusty Key (Main Room)",
            TR1Type.Cistern_K3_RustyKeyNearPierre => "Cistern Rusty Key (Near Pierre)",
            TR1Type.Tihocan_K1_GoldKeyFlipMap     => "Tihocan Gold Key (Flip Map)",
            TR1Type.Tihocan_K1_GoldKeyPierre      => "Tihocan Gold Key (Pierre)",
            TR1Type.Tihocan_K2_RustyKeyBoulders   => "Tihocan Rusty Key (Boulders)",
            TR1Type.Tihocan_K2_RustyKeyClangClang => "Tihocan Rusty Key (Clang Clang)",
            TR1Type.Tihocan_Scion_EndRoom         => "Tihocan Scion",
            TR1Type.Khamoon_K1_SapphireKeyEnd     => "Khamoon Sapphire Key (End)",
            TR1Type.Khamoon_K1_SapphireKeyStart   => "Khamoon Sapphire Key (Start)",
            TR1Type.Obelisk_K1_SapphireKeyEnd     => "Obelisk Sapphire Key (End)",
            TR1Type.Obelisk_K1_SapphireKeyStart   => "Obelisk Sapphire Key (Start)",
            TR1Type.Obelisk_P1_EyeOfHorus         => "Obelisk Eye of Horus",
            TR1Type.Obelisk_P2_Scarab             => "Obelisk Scarab",
            TR1Type.Obelisk_P3_SealOfAnubis       => "Obelisk Seal of Anubis",
            TR1Type.Obelisk_P4_Ankh               => "Obelisk Ankh",
            TR1Type.Sanctuary_K1_GoldKey          => "Sanctuary Gold Key",
            TR1Type.Sanctuary_P1_AnkhAfterKey     => "Sanctuary Ankh (After Key)",
            TR1Type.Sanctuary_P1_AnkhBehindSphinx => "Sanctuary Ankh (Behind Sphinx)",
            TR1Type.Sanctuary_P2_Scarab           => "Sanctuary Scarab",
            TR1Type.Mines_K1_RustyKey             => "Mines Rusty Key",
            TR1Type.Mines_P1_BoulderFuse          => "Mines Fuse (Boulder)",
            TR1Type.Mines_P1_ConveyorFuse         => "Mines Fuse (Conveyor)",
            TR1Type.Mines_P1_CowboyFuse           => "Mines Fuse (Cowboy)",
            TR1Type.Mines_P1_CowboyAltFuse        => "Mines Fuse (Cowboy Alt)",
            TR1Type.Mines_P2_PyramidKey           => "Mines Pyramid Key",
            _ => baseName,
        };
    }

    private void BuildItemDefinitions(TR1ArchipelagoData data)
    {
        int baseId = 770000;

        // Key items (progression) — defined from alias enum
        foreach (var alias in Enum.GetValues<TR1Type>())
        {
            string name = GetKeyItemDisplayName(alias, alias.ToString());
            if (name != alias.ToString()) // Only aliases with known display names
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

        // Weapons (useful)
        AddItemDef(data, baseId, TR1Type.Shotgun_S_P, "Shotgun", "weapon", "useful");
        AddItemDef(data, baseId, TR1Type.Magnums_S_P, "Magnums", "weapon", "useful");
        AddItemDef(data, baseId, TR1Type.Uzis_S_P, "Uzis", "weapon", "useful");

        // Ammo (filler)
        AddItemDef(data, baseId, TR1Type.ShotgunAmmo_S_P, "Shotgun Shells", "ammo", "filler");
        AddItemDef(data, baseId, TR1Type.MagnumAmmo_S_P, "Magnum Clips", "ammo", "filler");
        AddItemDef(data, baseId, TR1Type.UziAmmo_S_P, "Uzi Clips", "ammo", "filler");

        // Medipacks
        AddItemDef(data, baseId, TR1Type.LargeMed_S_P, "Large Medipack", "large_medipack", "useful");
        AddItemDef(data, baseId, TR1Type.SmallMed_S_P, "Small Medipack", "small_medipack", "filler");
    }

    private static void AddItemDef(TR1ArchipelagoData data, int baseId, TR1Type type, string name, string category, string classification)
    {
        data.ItemDefinitions[type.ToString()] = new ItemDefinition
        {
            Id = baseId + (int)(uint)type,
            Name = name,
            Category = category,
            ApClassification = classification,
        };
    }

    private void BuildKeyDependencies(TR1ArchipelagoData data)
    {
        // For each level's key items, record which level they belong to
        foreach (var level in data.Levels)
        {
            foreach (var keyItem in level.KeyItems)
            {
                data.KeyDependencies[keyItem.Alias] = new KeyDependency
                {
                    Level = level.File,
                    BaseType = keyItem.Type,
                };
            }
        }
    }

    private static string BuildKeyItemName(string levelFile, string displayName, TR1Type type, int sameTypeCount)
    {
        string baseName = type.ToString();
        string typeName = _keyTypeNames.GetValueOrDefault((levelFile, baseName), baseName.Replace("_S_P", ""));

        if (sameTypeCount > 0)
            return $"{displayName} - {typeName} #{sameTypeCount + 1}";
        return $"{displayName} - {typeName}";
    }

    private static readonly Dictionary<string, string> _levelShortNames = new()
    {
        ["Caves"] = "Caves",
        ["City of Vilcabamba"] = "Vilcabamba",
        ["Lost Valley"] = "Valley",
        ["Tomb of Qualopec"] = "Qualopec",
        ["St. Francis' Folly"] = "Folly",
        ["Colosseum"] = "Colosseum",
        ["Palace Midas"] = "Midas",
        ["The Cistern"] = "Cistern",
        ["Tomb of Tihocan"] = "Tihocan",
        ["City of Khamoon"] = "Khamoon",
        ["Obelisk of Khamoon"] = "Obelisk",
        ["Sanctuary of the Scion"] = "Sanctuary",
        ["Natla's Mines"] = "Mines",
        ["Atlantis"] = "Atlantis",
        ["The Great Pyramid"] = "Pyramid",
    };

    private static string BuildKeyItemAlias(string displayName, TR1Type type, int sameTypeCount)
    {
        string shortLevel = _levelShortNames.GetValueOrDefault(displayName, displayName.Replace(" ", ""));
        string shortType = type.ToString().Replace("_S_P", "");

        if (sameTypeCount > 0)
            return $"{shortLevel}_{shortType}_{sameTypeCount + 1}";
        return $"{shortLevel}_{shortType}";
    }

    // Known secret counts per level (from TR1 game data)
    private static readonly Dictionary<string, int> _secretCounts = new()
    {
        [TR1LevelNames.CAVES]      = 3,
        [TR1LevelNames.VILCABAMBA] = 3,
        [TR1LevelNames.VALLEY]     = 5,
        [TR1LevelNames.QUALOPEC]   = 3,
        [TR1LevelNames.FOLLY]      = 4,
        [TR1LevelNames.COLOSSEUM]  = 3,
        [TR1LevelNames.MIDAS]      = 3,
        [TR1LevelNames.CISTERN]    = 3,
        [TR1LevelNames.TIHOCAN]    = 2,
        [TR1LevelNames.KHAMOON]    = 3,
        [TR1LevelNames.OBELISK]    = 3,
        [TR1LevelNames.SANCTUARY]  = 1,
        [TR1LevelNames.MINES]      = 3,
        [TR1LevelNames.ATLANTIS]   = 3,
        [TR1LevelNames.PYRAMID]    = 3,
    };
}
