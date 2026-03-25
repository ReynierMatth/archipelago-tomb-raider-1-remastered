using Newtonsoft.Json.Linq;

namespace TRArchipelagoClient.Core;

/// <summary>
/// Deserialized slot data from the AP server.
/// Contains the game configuration for this player's slot.
/// </summary>
public class SlotData
{
    public string Game { get; set; }
    public int Goal { get; set; }
    public int LevelsForGoal { get; set; }
    public int SecretsMode { get; set; }
    public bool DeathLink { get; set; }
    public int StartingWeapons { get; set; }
    public int TotalLevels { get; set; }
    public int TotalSecrets { get; set; }
    public List<string> EnabledGames { get; set; } = new();
    public List<string> LevelSequence { get; set; } = new();

    /// <summary>
    /// Maps AP item ID -> slot type string (e.g. "K1", "K2", "P1", "P2", "Scion", "LeadBar").
    /// Used by the client to determine which inventory slot to inject key items into.
    /// </summary>
    public Dictionary<long, string> KeyItemSlots { get; set; } = new();

    public static SlotData FromDictionary(IReadOnlyDictionary<string, object> data)
    {
        var slotData = new SlotData
        {
            Game = "Tomb Raider Remastered",
        };

        if (data.TryGetValue("goal", out var goal))
            slotData.Goal = Convert.ToInt32(goal);
        if (data.TryGetValue("levels_for_goal", out var levelsForGoal))
            slotData.LevelsForGoal = Convert.ToInt32(levelsForGoal);
        if (data.TryGetValue("secrets_mode", out var secretsMode))
            slotData.SecretsMode = Convert.ToInt32(secretsMode);
        if (data.TryGetValue("death_link", out var deathLink))
            slotData.DeathLink = Convert.ToBoolean(deathLink);
        if (data.TryGetValue("starting_weapons", out var startingWeapons))
            slotData.StartingWeapons = Convert.ToInt32(startingWeapons);
        if (data.TryGetValue("total_levels", out var totalLevels))
            slotData.TotalLevels = Convert.ToInt32(totalLevels);
        if (data.TryGetValue("total_secrets", out var totalSecrets))
            slotData.TotalSecrets = Convert.ToInt32(totalSecrets);
        if (data.TryGetValue("enabled_games", out var enabledGames) && enabledGames is JArray gamesArr)
            slotData.EnabledGames = gamesArr.ToObject<List<string>>() ?? new();
        if (data.TryGetValue("level_sequence", out var levelSeq) && levelSeq is JArray arr)
            slotData.LevelSequence = arr.ToObject<List<string>>() ?? new();
        if (data.TryGetValue("key_item_slots", out var keySlots) && keySlots is JObject slotsObj)
        {
            foreach (var kv in slotsObj)
            {
                if (long.TryParse(kv.Key, out long apId))
                    slotData.KeyItemSlots[apId] = kv.Value?.ToString() ?? "";
            }
        }

        return slotData;
    }
}
