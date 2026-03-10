using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TRDataExporter;

Console.WriteLine("TR Remastered -> Archipelago Data Exporter");
Console.WriteLine("==========================================");

string defaultBaseDir = @"C:\Program Files (x86)\Steam\steamapps\common\Tomb Raider I-III Remastered";

string game = args.Length > 0 ? args[0].ToLower() : "tr1";
string baseDir = args.Length > 1 ? args[1] : defaultBaseDir;
string outputPath = args.Length > 2 ? args[2] : null;
bool includeGold = args.Any(a => a == "--gold");

var settings = new JsonSerializerSettings
{
    Formatting = Formatting.Indented,
    ContractResolver = new CamelCasePropertyNamesContractResolver(),
    NullValueHandling = NullValueHandling.Ignore
};

TRArchipelagoData data;

switch (game)
{
    case "tr1":
    {
        string gameDir = Path.Combine(baseDir, "1", "DATA");
        string resourceBase = Path.Combine(AppContext.BaseDirectory, "Resources", "TR1");
        outputPath ??= "tr1r_data.json";
        Console.WriteLine($"Exporting TR1 from: {gameDir}");
        var exporter = new TR1DataExporter(gameDir, resourceBase);
        data = exporter.Export();
        break;
    }
    case "tr2":
    {
        string gameDir = Path.Combine(baseDir, "2", "DATA");
        outputPath ??= "tr2r_data.json";
        Console.WriteLine($"Exporting TR2 from: {gameDir} (gold={includeGold})");
        var exporter = new TR2DataExporter(gameDir);
        data = exporter.Export(includeGold);
        break;
    }
    case "tr3":
    {
        string gameDir = Path.Combine(baseDir, "3", "DATA");
        outputPath ??= "tr3r_data.json";
        Console.WriteLine($"Exporting TR3 from: {gameDir} (gold={includeGold})");
        var exporter = new TR3DataExporter(gameDir);
        data = exporter.Export(includeGold);
        break;
    }
    default:
        Console.WriteLine($"Unknown game: {game}");
        Console.WriteLine("Usage: TRDataExporter <tr1|tr2|tr3> [base_dir] [output_path] [--gold]");
        return;
}

string json = JsonConvert.SerializeObject(data, settings);
File.WriteAllText(outputPath, json);

Console.WriteLine();
Console.WriteLine($"Exported {data.Levels.Count} levels");
Console.WriteLine($"  Total pickups:   {data.Levels.Sum(l => l.Pickups.Count)}");
Console.WriteLine($"  Total key items: {data.Levels.Sum(l => l.KeyItems.Count)}");
Console.WriteLine($"  Total secrets:   {data.Levels.Sum(l => l.Secrets.Count)}");
Console.WriteLine($"  Item defs:       {data.ItemDefinitions.Count}");
Console.WriteLine($"Written to: {Path.GetFullPath(outputPath)}");
