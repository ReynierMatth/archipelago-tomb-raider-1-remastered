using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TRDataExporter;

Console.WriteLine("TR1 Remastered -> Archipelago Data Exporter");
Console.WriteLine("============================================");

// Game data directory (where .PHD files are)
string defaultGameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Tomb Raider I-III Remastered\1\DATA";
string gameDataDir = args.Length > 0 ? args[0] : defaultGameDir;

// Resource base (TR-Rando resources for routes/secrets)
string resourceBase = Path.Combine(AppContext.BaseDirectory, "Resources", "TR1");

string outputPath = args.Length > 1 ? args[1] : "tr1r_data.json";

if (!Directory.Exists(gameDataDir))
{
    Console.WriteLine($"ERROR: Game data directory not found: {gameDataDir}");
    Console.WriteLine($"Usage: TRDataExporter [game_data_dir] [output_path]");
    Console.WriteLine($"  game_data_dir: Path to TR1 Remastered DATA directory (containing .PHD files)");
    return;
}

Console.WriteLine($"Game data: {gameDataDir}");
Console.WriteLine($"Resources: {resourceBase}");
Console.WriteLine();

var exporter = new TR1DataExporter(gameDataDir, resourceBase);
var data = exporter.Export();

var settings = new JsonSerializerSettings
{
    Formatting = Formatting.Indented,
    ContractResolver = new CamelCasePropertyNamesContractResolver(),
    NullValueHandling = NullValueHandling.Ignore
};

string json = JsonConvert.SerializeObject(data, settings);
File.WriteAllText(outputPath, json);

Console.WriteLine();
Console.WriteLine($"Exported {data.Levels.Count} levels");
Console.WriteLine($"  Total pickups:   {data.Levels.Sum(l => l.Pickups.Count)}");
Console.WriteLine($"  Total key items: {data.Levels.Sum(l => l.KeyItems.Count)}");
Console.WriteLine($"  Total secrets:   {data.Levels.Sum(l => l.Secrets.Count)}");
Console.WriteLine($"  Item defs:       {data.ItemDefinitions.Count}");
Console.WriteLine($"Written to: {Path.GetFullPath(outputPath)}");
