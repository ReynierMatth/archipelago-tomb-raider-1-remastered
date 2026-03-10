using Newtonsoft.Json;

namespace TRDataExporter;

/// <summary>
/// Analyzes TR-Rando route data to determine which pickups are gated behind key item doors.
/// Routes define waypoints along the level path with KeyItemsLow/High markers indicating
/// where locked doors are. Pickups in rooms that only appear AFTER a High marker are gated.
/// </summary>
public static class RouteAnalyzer
{
    private const int Step4 = 1024;
    private const int Step1 = 256;

    /// <summary>
    /// Replicate GetKeyItemID from LocationPicker.cs to match route IDs.
    /// </summary>
    public static uint ComputeKeyItemId(
        int levelSequence, int entityX, int entityY, int entityZ,
        short entityRoom, uint entityTypeId,
        int roomOriginX, int roomOriginZ)
    {
        int x = (entityX - roomOriginX) / Step4;
        int z = (entityZ - roomOriginZ) / Step4;
        int y = entityY / Step1;

        long id = 10000
            + (levelSequence - 1) * 1000
            + entityTypeId
            + entityRoom * 2
            + x * z
            + y;
        return (uint)id;
    }

    /// <summary>
    /// Annotate pickups with required key items based on route analysis.
    /// A pickup requires a key item if its room only appears AFTER that key item's
    /// High marker in the route (meaning it's behind the locked door).
    /// </summary>
    public static void AnnotatePickups(
        string routeFilePath,
        string levelFile,
        int levelSequence,
        List<(int x, int z)> roomOrigins,
        List<(int x, int y, int z, short room, uint typeId, string name)> keyItems,
        List<PickupData> pickups)
    {
        if (!File.Exists(routeFilePath))
            return;

        var routes = JsonConvert.DeserializeObject<Dictionary<string, List<RouteWaypoint>>>(
            File.ReadAllText(routeFilePath));

        if (routes == null || !routes.TryGetValue(levelFile, out var route) || route == null || route.Count == 0)
            return;

        // Filter to default range (empty/null Range) and no return path
        var filteredRoute = route
            .Where(w => string.IsNullOrEmpty(w.Range) && !w.RequiresReturnPath)
            .ToList();

        if (filteredRoute.Count == 0)
            return;

        // Compute key item IDs and map to display names
        var keyItemIdToName = new Dictionary<string, string>();
        foreach (var ki in keyItems)
        {
            if (ki.room < 0 || ki.room >= roomOrigins.Count) continue;
            var (rx, rz) = roomOrigins[ki.room];
            uint kiId = ComputeKeyItemId(levelSequence, ki.x, ki.y, ki.z, ki.room, ki.typeId, rx, rz);
            string idStr = kiId.ToString();
            if (!keyItemIdToName.ContainsKey(idStr))
                keyItemIdToName[idStr] = ki.name;
        }

        if (keyItemIdToName.Count == 0)
            return;

        // For each key item, determine which rooms are gated behind it
        var gatedRooms = new Dictionary<string, HashSet<short>>();

        foreach (var (idStr, kiName) in keyItemIdToName)
        {
            // Find the first High marker for this key item ID
            int highIndex = -1;
            for (int i = 0; i < filteredRoute.Count; i++)
            {
                if (ContainsKeyId(filteredRoute[i].KeyItemsHigh, idStr))
                {
                    highIndex = i;
                    break;
                }
            }

            if (highIndex < 0) continue;

            // Rooms that appear BEFORE the high marker are accessible without the key
            var roomsBeforeGate = new HashSet<short>();
            for (int i = 0; i < highIndex; i++)
            {
                roomsBeforeGate.Add((short)filteredRoute[i].Room);
            }

            // Rooms that ONLY appear AFTER the high marker are gated
            var gatedRoomSet = new HashSet<short>();
            for (int i = highIndex; i < filteredRoute.Count; i++)
            {
                short room = (short)filteredRoute[i].Room;
                if (!roomsBeforeGate.Contains(room))
                {
                    gatedRoomSet.Add(room);
                }
            }

            if (gatedRoomSet.Count > 0)
            {
                gatedRooms[kiName] = gatedRoomSet;
            }
        }

        // Annotate each pickup with its required key items
        int annotated = 0;
        foreach (var pickup in pickups)
        {
            foreach (var (kiName, rooms) in gatedRooms)
            {
                if (rooms.Contains(pickup.Room))
                {
                    pickup.RequiredKeyItems.Add(kiName);
                }
            }
            if (pickup.RequiredKeyItems.Count > 0)
                annotated++;
        }

        if (annotated > 0)
            Console.WriteLine($"    {annotated} pickups gated behind key items");
    }

    private static bool ContainsKeyId(string keyIds, string idStr)
    {
        if (string.IsNullOrEmpty(keyIds)) return false;
        return keyIds.Split(',').Contains(idStr);
    }
}

public class RouteWaypoint
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Room { get; set; }
    public string KeyItemsLow { get; set; }
    public string KeyItemsHigh { get; set; }
    public string Range { get; set; }
    public bool RequiresReturnPath { get; set; }
    public bool Validated { get; set; } = true;
}
