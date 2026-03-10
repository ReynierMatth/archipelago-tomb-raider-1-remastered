using TRLevelControl;
using TRLevelControl.Helpers;
using TRLevelControl.Model;

namespace TRDataExporter;

public class TR2DataExporter
{
    private readonly string _gameDataDir;

    private static readonly Dictionary<string, string> _levelDisplayNames = new()
    {
        [TR2LevelNames.GW]        = "Great Wall",
        [TR2LevelNames.VENICE]    = "Venice",
        [TR2LevelNames.BARTOLI]   = "Bartoli's Hideout",
        [TR2LevelNames.OPERA]     = "Opera House",
        [TR2LevelNames.RIG]       = "Oil Rig",
        [TR2LevelNames.DA]        = "Diving Area",
        [TR2LevelNames.FATHOMS]   = "The Fathoms",
        [TR2LevelNames.DORIA]     = "Maria Doria",
        [TR2LevelNames.LQ]        = "Living Quarters",
        [TR2LevelNames.DECK]      = "The Deck",
        [TR2LevelNames.TIBET]     = "Tibet",
        [TR2LevelNames.MONASTERY] = "Barkhang Monastery",
        [TR2LevelNames.COT]       = "Catacombs of the Talion",
        [TR2LevelNames.CHICKEN]   = "Ice Palace",
        [TR2LevelNames.XIAN]      = "Xian Caves",
        [TR2LevelNames.FLOATER]   = "Dragon's Lair",
        [TR2LevelNames.LAIR]      = "House of the Spirit",
        [TR2LevelNames.HOME]      = "Home Sweet Home",
        // Gold
        [TR2LevelNames.COLDWAR]   = "Cold War Bunker",
        [TR2LevelNames.FOOLGOLD]  = "Fool's Gold",
        [TR2LevelNames.FURNACE]   = "The Furnace of the Gods",
        [TR2LevelNames.KINGDOM]   = "Kingdom",
        [TR2LevelNames.VEGAS]     = "Red Alert",
    };

    private static readonly Dictionary<string, string> _levelRegions = new()
    {
        [TR2LevelNames.GW]        = "Great Wall",
        [TR2LevelNames.VENICE]    = "Italy",
        [TR2LevelNames.BARTOLI]   = "Italy",
        [TR2LevelNames.OPERA]     = "Italy",
        [TR2LevelNames.RIG]       = "Offshore",
        [TR2LevelNames.DA]        = "Offshore",
        [TR2LevelNames.FATHOMS]   = "Offshore",
        [TR2LevelNames.DORIA]     = "Offshore",
        [TR2LevelNames.LQ]        = "Offshore",
        [TR2LevelNames.DECK]      = "Offshore",
        [TR2LevelNames.TIBET]     = "Tibet",
        [TR2LevelNames.MONASTERY] = "Tibet",
        [TR2LevelNames.COT]       = "Tibet",
        [TR2LevelNames.CHICKEN]   = "Tibet",
        [TR2LevelNames.XIAN]      = "China",
        [TR2LevelNames.FLOATER]   = "China",
        [TR2LevelNames.LAIR]      = "China",
        [TR2LevelNames.HOME]      = "Home",
        // Gold
        [TR2LevelNames.COLDWAR]   = "Gold",
        [TR2LevelNames.FOOLGOLD]  = "Gold",
        [TR2LevelNames.FURNACE]   = "Gold",
        [TR2LevelNames.KINGDOM]   = "Gold",
        [TR2LevelNames.VEGAS]     = "Gold",
    };

    private static readonly Dictionary<(string level, string baseType), string> _keyTypeNames = new()
    {
        { (TR2LevelNames.GW, "Key1_S_P"), "Guardhouse Key" },
        { (TR2LevelNames.GW, "Key2_S_P"), "Rusty Key" },
        { (TR2LevelNames.VENICE, "Key1_S_P"), "Boathouse Key" },
        { (TR2LevelNames.VENICE, "Key2_S_P"), "Steel Key" },
        { (TR2LevelNames.VENICE, "Key3_S_P"), "Iron Key" },
        { (TR2LevelNames.BARTOLI, "Key1_S_P"), "Library Key" },
        { (TR2LevelNames.BARTOLI, "Key2_S_P"), "Detonator Key" },
        { (TR2LevelNames.OPERA, "Key1_S_P"), "Ornate Key" },
        { (TR2LevelNames.OPERA, "Puzzle1_S_P"), "Relay Box" },
        { (TR2LevelNames.OPERA, "Puzzle2_S_P"), "Circuit Board" },
        { (TR2LevelNames.RIG, "Key1_S_P"), "Red Pass Card" },
        { (TR2LevelNames.RIG, "Key2_S_P"), "Yellow Pass Card" },
        { (TR2LevelNames.RIG, "Key3_S_P"), "Green Pass Card" },
        { (TR2LevelNames.DA, "Key1_S_P"), "Red Pass Card" },
        { (TR2LevelNames.DA, "Key4_S_P"), "Blue Pass Card" },
        { (TR2LevelNames.DA, "Puzzle1_S_P"), "Machine Chip" },
        { (TR2LevelNames.DORIA, "Key1_S_P"), "Rest Room Key" },
        { (TR2LevelNames.DORIA, "Key2_S_P"), "Rusty Key" },
        { (TR2LevelNames.DORIA, "Key3_S_P"), "Cabin Key" },
        { (TR2LevelNames.DORIA, "Puzzle1_S_P"), "Breaker" },
        { (TR2LevelNames.LQ, "Key1_S_P"), "Theatre Key" },
        { (TR2LevelNames.DECK, "Key2_S_P"), "Stern Key" },
        { (TR2LevelNames.DECK, "Key3_S_P"), "Storage Key" },
        { (TR2LevelNames.DECK, "Key4_S_P"), "Cabin Key" },
        { (TR2LevelNames.DECK, "Puzzle4_S_P"), "The Seraph" },
        { (TR2LevelNames.TIBET, "Key1_S_P"), "Drawbridge Key" },
        { (TR2LevelNames.TIBET, "Key2_S_P"), "Hut Key" },
        { (TR2LevelNames.MONASTERY, "Key1_S_P"), "Strongroom Key" },
        { (TR2LevelNames.MONASTERY, "Key2_S_P"), "Trapdoor Key" },
        { (TR2LevelNames.MONASTERY, "Key3_S_P"), "Rooftops Key" },
        { (TR2LevelNames.MONASTERY, "Key4_S_P"), "Main Hall Key" },
        { (TR2LevelNames.MONASTERY, "Puzzle1_S_P"), "Wheel" },
        { (TR2LevelNames.MONASTERY, "Puzzle2_S_P"), "Gemstone" },
        { (TR2LevelNames.MONASTERY, "Puzzle4_S_P"), "The Seraph" },
        { (TR2LevelNames.COT, "Puzzle1_S_P"), "Tibetan Mask" },
        { (TR2LevelNames.COT, "Quest1_S_P"), "Gong Hammer" },
        { (TR2LevelNames.CHICKEN, "Key2_S_P"), "Gong Hammer" },
        { (TR2LevelNames.CHICKEN, "Puzzle1_S_P"), "Tibetan Mask" },
        { (TR2LevelNames.CHICKEN, "Quest2_S_P"), "Talion" },
        { (TR2LevelNames.XIAN, "Key2_S_P"), "Gold Key" },
        { (TR2LevelNames.XIAN, "Key3_S_P"), "Silver Key" },
        { (TR2LevelNames.XIAN, "Key4_S_P"), "Main Chamber Key" },
        { (TR2LevelNames.XIAN, "Puzzle1_S_P"), "The Dragon Seal" },
        { (TR2LevelNames.FLOATER, "Puzzle1_S_P"), "Mystic Plaque" },
        { (TR2LevelNames.FLOATER, "Puzzle2_S_P"), "Mystic Plaque" },
        { (TR2LevelNames.LAIR, "Puzzle1_S_P"), "Mystic Plaque" },
        { (TR2LevelNames.COLDWAR, "Key1_S_P"), "Guard Room Key" },
        { (TR2LevelNames.COLDWAR, "Key2_S_P"), "Shaft B Key" },
        { (TR2LevelNames.FOOLGOLD, "Key1_S_P"), "Key" },
        { (TR2LevelNames.FOOLGOLD, "Key4_S_P"), "Card Key 2" },
        { (TR2LevelNames.FOOLGOLD, "Puzzle1_S_P"), "Circuit Board" },
        { (TR2LevelNames.FURNACE, "Puzzle1_S_P"), "Mask" },
        { (TR2LevelNames.FURNACE, "Puzzle3_S_P"), "Gold Nugget" },
        { (TR2LevelNames.VEGAS, "Key1_S_P"), "Key" },
        { (TR2LevelNames.VEGAS, "Puzzle1_S_P"), "Elevator Junction" },
        { (TR2LevelNames.VEGAS, "Puzzle3_S_P"), "Circuit Board" },
    };

    private readonly string _routePath;

    public TR2DataExporter(string gameDataDir)
    {
        _gameDataDir = gameDataDir;
        _routePath = Path.Combine(AppContext.BaseDirectory, "Resources", "TR2", "Locations", "routes.json");
    }

    public TRArchipelagoData Export(bool includeGold)
    {
        var data = new TRArchipelagoData { Game = "Tomb Raider 2 Remastered" };
        var levels = includeGold ? TR2LevelNames.AsListWithGold : TR2LevelNames.AsList;
        var reader = new TR2LevelControl();

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

            TR2Level level;
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

                if (TR2TypeUtilities.IsKeyItemType(entity.TypeID))
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
                else if (TR2TypeUtilities.IsAnyPickupType(entity.TypeID)
                         && !TR2TypeUtilities.IsSecretType(entity.TypeID)
                         && !TR2TypeUtilities.IsKeyItemType(entity.TypeID))
                {
                    string category;
                    if (TR2TypeUtilities.IsGunType(entity.TypeID))
                        category = "weapon";
                    else if (TR2TypeUtilities.IsAmmoType(entity.TypeID))
                        category = "ammo";
                    else if (entity.TypeID == TR2Type.LargeMed_S_P)
                        category = "large_medipack";
                    else if (entity.TypeID == TR2Type.SmallMed_S_P)
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

            // Route analysis: annotate pickups with key item dependencies
            var roomOrigins = level.Rooms.Select(r => (r.Info.X, r.Info.Z)).ToList();
            var keyItemInfos = levelData.KeyItems
                .Select(ki => (ki.X, ki.Y, ki.Z, ki.Room, (uint)Enum.Parse<TR2Type>(ki.Type), ki.Name))
                .ToList();
            RouteAnalyzer.AnnotatePickups(_routePath, levelFile, levelData.Sequence, roomOrigins, keyItemInfos, levelData.Pickups);

            // TR2: all levels have 3 secrets (Stone, Jade, Gold dragons)
            for (int s = 0; s < 3; s++)
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
        ["Great Wall"] = "GW",
        ["Venice"] = "Venice",
        ["Bartoli's Hideout"] = "Bartoli",
        ["Opera House"] = "Opera",
        ["Oil Rig"] = "Rig",
        ["Diving Area"] = "DA",
        ["The Fathoms"] = "Fathoms",
        ["Maria Doria"] = "Doria",
        ["Living Quarters"] = "LQ",
        ["The Deck"] = "Deck",
        ["Tibet"] = "Tibet",
        ["Barkhang Monastery"] = "Barkhang",
        ["Catacombs of the Talion"] = "CoT",
        ["Ice Palace"] = "IcePalace",
        ["Xian Caves"] = "Xian",
        ["Dragon's Lair"] = "Floater",
        ["House of the Spirit"] = "Lair",
        ["Home Sweet Home"] = "Home",
        ["Cold War Bunker"] = "ColdWar",
        ["Fool's Gold"] = "FoolsGold",
        ["The Furnace of the Gods"] = "Furnace",
        ["Kingdom"] = "Kingdom",
        ["Red Alert"] = "Vegas",
    };

    private static string GetKeyItemDisplayName(TR2Type alias, string baseName)
    {
        return alias switch
        {
            TR2Type.GW_K1_GuardhouseKey        => "GW Guardhouse Key",
            TR2Type.GW_K2_RustyKey             => "GW Rusty Key",
            TR2Type.Venice_K1_BoathouseKey      => "Venice Boathouse Key",
            TR2Type.Venice_K2_SteelKey          => "Venice Steel Key",
            TR2Type.Venice_K3_IronKey           => "Venice Iron Key",
            TR2Type.Bartoli_K1_LibraryKey       => "Bartoli Library Key",
            TR2Type.Bartoli_K2_DetonatorKey     => "Bartoli Detonator Key",
            TR2Type.Opera_K1_OrnateKeyFans      => "Opera Ornate Key (Fans)",
            TR2Type.Opera_K1_OrnateKeyStart     => "Opera Ornate Key (Start)",
            TR2Type.Opera_P1_RelayBox           => "Opera Relay Box",
            TR2Type.Opera_P2_CircuitBoard       => "Opera Circuit Board",
            TR2Type.Rig_K1_RedPassCard          => "Rig Red Pass Card",
            TR2Type.Rig_K2_YellowPassCard       => "Rig Yellow Pass Card",
            TR2Type.Rig_K3_GreenPassCard        => "Rig Green Pass Card",
            TR2Type.DA_K1_RedPassCard           => "DA Red Pass Card",
            TR2Type.DA_K4_BluePassCard          => "DA Blue Pass Card",
            TR2Type.DA_P1_MachineChipChopper    => "DA Machine Chip (Chopper)",
            TR2Type.DA_P1_MachineChipMiddleRoom => "DA Machine Chip (Middle Room)",
            TR2Type.Wreck_K1_RestRoomKey        => "Doria Rest Room Key",
            TR2Type.Wreck_K2_RustyKey           => "Doria Rusty Key",
            TR2Type.Wreck_K3_CabinKey           => "Doria Cabin Key",
            TR2Type.Wreck_P1_RestroomBreaker    => "Doria Breaker (Restroom)",
            TR2Type.Wreck_P1_ShardRoomBreaker   => "Doria Breaker (Shard Room)",
            TR2Type.Wreck_P1_StaircaseBreaker   => "Doria Breaker (Staircase)",
            TR2Type.LQ_K1_TheatreKey            => "LQ Theatre Key",
            TR2Type.Deck_K2_SternKey            => "Deck Stern Key",
            TR2Type.Deck_K3_StorageKey          => "Deck Storage Key",
            TR2Type.Deck_K4_CabinKey            => "Deck Cabin Key",
            TR2Type.Deck_P4_TheSeraph           => "Deck Seraph",
            TR2Type.Tibet_K1_DrawbridgeKey      => "Tibet Drawbridge Key",
            TR2Type.Tibet_K2_HutKey             => "Tibet Hut Key",
            TR2Type.Barkhang_K1_StrongroomKey   => "Barkhang Strongroom Key",
            TR2Type.Barkhang_K2_TrapdoorKey     => "Barkhang Trapdoor Key",
            TR2Type.Barkhang_K3_RooftopsKey     => "Barkhang Rooftops Key",
            TR2Type.Barkhang_K4_MainHallKey     => "Barkhang Main Hall Key",
            TR2Type.Barkhang_P1_BurnerRoomWheel => "Barkhang Wheel (Burner Room)",
            TR2Type.Barkhang_P1_LadderTowerWheel => "Barkhang Wheel (Ladder Tower)",
            TR2Type.Barkhang_P1_OutsideWheel    => "Barkhang Wheel (Outside)",
            TR2Type.Barkhang_P1_PoolWheel       => "Barkhang Wheel (Pool)",
            TR2Type.Barkhang_P1_RooftopWheel    => "Barkhang Wheel (Rooftop)",
            TR2Type.Barkhang_P2_EastGemstone    => "Barkhang Gemstone (East)",
            TR2Type.Barkhang_P2_WestGemstone    => "Barkhang Gemstone (West)",
            TR2Type.Barkhang_P4_TheSeraph       => "Barkhang Seraph",
            TR2Type.CoT_P1_TibetanMaskStart     => "CoT Tibetan Mask (Start)",
            TR2Type.CoT_P1_TibetanMaskUnderwater => "CoT Tibetan Mask (Underwater)",
            TR2Type.CoT_Q1_GongHammer           => "CoT Gong Hammer",
            TR2Type.Chicken_K2_GongHammer       => "Ice Palace Gong Hammer",
            TR2Type.Chicken_P1_TibetanMask      => "Ice Palace Tibetan Mask",
            TR2Type.Chicken_Q2_Talion           => "Ice Palace Talion",
            TR2Type.Xian_K2_GoldKey             => "Xian Gold Key",
            TR2Type.Xian_K3_SilverKey           => "Xian Silver Key",
            TR2Type.Xian_K4_MainChamberKey      => "Xian Main Chamber Key",
            TR2Type.Xian_P1_TheDragonSeal       => "Xian Dragon Seal",
            TR2Type.Floater_P1_MysticPlaqueAltEnd => "Floater Mystic Plaque (Alt End)",
            TR2Type.Floater_P1_MysticPlaqueWest => "Floater Mystic Plaque (West)",
            TR2Type.Floater_P2_MysticPlaqueEast => "Floater Mystic Plaque (East)",
            TR2Type.Lair_P1_MysticPlaque        => "Lair Mystic Plaque",
            TR2Type.ColdWar_K1_GuardRoom        => "ColdWar Guard Room Key",
            TR2Type.ColdWar_K2_ShaftB           => "ColdWar Shaft B Key",
            TR2Type.FoolsGold_K1_End            => "FoolsGold Key (End)",
            TR2Type.FoolsGold_K1_Skidoo         => "FoolsGold Key (Skidoo)",
            TR2Type.FoolsGold_K4_CardKey2       => "FoolsGold Card Key 2",
            TR2Type.FoolsGold_P1_CircuitBoard   => "FoolsGold Circuit Board",
            TR2Type.Furnace_P1_Mask             => "Furnace Mask",
            TR2Type.Furnace_P3_GoldNugget       => "Furnace Gold Nugget",
            TR2Type.Vegas_K1_Winston            => "Vegas Key (Winston)",
            TR2Type.Vegas_K1_Pool               => "Vegas Key (Pool)",
            TR2Type.Vegas_P1_ElevatorJunction   => "Vegas Elevator Junction",
            TR2Type.Vegas_P3_CircuitBoard       => "Vegas Circuit Board",
            _ => baseName,
        };
    }

    private void BuildItemDefinitions(TRArchipelagoData data)
    {
        int baseId = 870_000;

        foreach (var alias in Enum.GetValues<TR2Type>())
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

        AddItemDef(data, baseId, TR2Type.Shotgun_S_P, "Shotgun", "weapon", "useful");
        AddItemDef(data, baseId, TR2Type.Automags_S_P, "Automags", "weapon", "useful");
        AddItemDef(data, baseId, TR2Type.Uzi_S_P, "Uzis", "weapon", "useful");
        AddItemDef(data, baseId, TR2Type.Harpoon_S_P, "Harpoon Gun", "weapon", "useful");
        AddItemDef(data, baseId, TR2Type.M16_S_P, "M16", "weapon", "useful");
        AddItemDef(data, baseId, TR2Type.GrenadeLauncher_S_P, "Grenade Launcher", "weapon", "useful");

        AddItemDef(data, baseId, TR2Type.ShotgunAmmo_S_P, "Shotgun Shells", "ammo", "filler");
        AddItemDef(data, baseId, TR2Type.AutoAmmo_S_P, "Auto Clips", "ammo", "filler");
        AddItemDef(data, baseId, TR2Type.UziAmmo_S_P, "Uzi Clips", "ammo", "filler");
        AddItemDef(data, baseId, TR2Type.HarpoonAmmo_S_P, "Harpoons", "ammo", "filler");
        AddItemDef(data, baseId, TR2Type.M16Ammo_S_P, "M16 Clips", "ammo", "filler");
        AddItemDef(data, baseId, TR2Type.Grenades_S_P, "Grenades", "ammo", "filler");

        AddItemDef(data, baseId, TR2Type.LargeMed_S_P, "Large Medipack", "large_medipack", "useful");
        AddItemDef(data, baseId, TR2Type.SmallMed_S_P, "Small Medipack", "small_medipack", "filler");
        AddItemDef(data, baseId, TR2Type.Flares_S_P, "Flares", "pickup", "filler");
    }

    private static void AddItemDef(TRArchipelagoData data, int baseId, TR2Type type, string name, string category, string classification)
    {
        data.ItemDefinitions[type.ToString()] = new ItemDefinition
        {
            Id = baseId + (int)(uint)type,
            Name = name,
            Category = category,
            ApClassification = classification,
        };
    }

    private static string BuildKeyItemName(string levelFile, string displayName, TR2Type type, int sameTypeCount)
    {
        string baseName = type.ToString();
        string typeName = _keyTypeNames.GetValueOrDefault((levelFile, baseName), baseName.Replace("_S_P", ""));
        if (sameTypeCount > 0)
            return $"{displayName} - {typeName} #{sameTypeCount + 1}";
        return $"{displayName} - {typeName}";
    }

    private static string BuildKeyItemAlias(string displayName, TR2Type type, int sameTypeCount)
    {
        string shortLevel = _levelShortNames.GetValueOrDefault(displayName, displayName.Replace(" ", ""));
        string shortType = type.ToString().Replace("_S_P", "");
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
}
