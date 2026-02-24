using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TRDataExporter;

Console.WriteLine("TR1 Remastered -> Archipelago Data Exporter");
Console.WriteLine("============================================");

string resourceBase = Path.Combine(AppContext.BaseDirectory, "Resources", "TR1");
string outputPath = args.Length > 0 ? args[0] : "tr1r_data.json";

var exporter = new TR1DataExporter(resourceBase);
var data = exporter.Export();

var settings = new JsonSerializerSettings
{
    Formatting = Formatting.Indented,
    ContractResolver = new CamelCasePropertyNamesContractResolver(),
    NullValueHandling = NullValueHandling.Ignore
};

string json = JsonConvert.SerializeObject(data, settings);
File.WriteAllText(outputPath, json);

Console.WriteLine($"Exported {data.Levels.Count} levels");
Console.WriteLine($"  Total pickups:  {data.Levels.Sum(l => l.Pickups.Count)}");
Console.WriteLine($"  Total key items: {data.Levels.Sum(l => l.KeyItems.Count)}");
Console.WriteLine($"  Total secrets:  {data.Levels.Sum(l => l.Secrets.Count)}");
Console.WriteLine($"  Total routes:   {data.Levels.Sum(l => l.Routes.Count)}");
Console.WriteLine($"Written to: {Path.GetFullPath(outputPath)}");
