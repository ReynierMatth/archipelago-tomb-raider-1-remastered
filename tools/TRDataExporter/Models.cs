namespace TRDataExporter;

public class TR1ArchipelagoData
{
    public string Game { get; set; } = "Tomb Raider 1 Remastered";
    public List<LevelData> Levels { get; set; } = new();
    public List<string> LevelSequence { get; set; } = new();
    public Dictionary<string, KeyDependency> KeyDependencies { get; set; } = new();
    public Dictionary<string, ItemDefinition> ItemDefinitions { get; set; } = new();
}

public class LevelData
{
    public string Name { get; set; }
    public string File { get; set; }
    public int Sequence { get; set; }
    public string Region { get; set; }
    public List<PickupData> Pickups { get; set; } = new();
    public List<KeyItemData> KeyItems { get; set; } = new();
    public List<SecretData> Secrets { get; set; } = new();
    public List<RouteData> Routes { get; set; } = new();
}

public class PickupData
{
    public int EntityIndex { get; set; }
    public string Type { get; set; }
    public string Category { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public short Room { get; set; }
}

public class KeyItemData
{
    public int EntityIndex { get; set; }
    public string Type { get; set; }
    public string Alias { get; set; }
    public string Name { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public short Room { get; set; }
}

public class SecretData
{
    public int Index { get; set; }
    public List<int> RewardEntities { get; set; } = new();
}

public class RouteData
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Room { get; set; }
    public string KeyItemsLow { get; set; }
    public string KeyItemsHigh { get; set; }
    public string Range { get; set; }
    public bool RequiresReturnPath { get; set; }
}

public class KeyDependency
{
    public string Level { get; set; }
    public string BaseType { get; set; }
    public List<int> UnlocksRooms { get; set; } = new();
}

public class ItemDefinition
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }
    public string ApClassification { get; set; }
}
