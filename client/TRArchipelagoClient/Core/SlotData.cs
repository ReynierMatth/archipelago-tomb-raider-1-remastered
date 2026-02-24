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
    public List<string> LevelSequence { get; set; } = new();

    public static SlotData FromDictionary(IReadOnlyDictionary<string, object> data)
    {
        var slotData = new SlotData
        {
            Game = "Tomb Raider 1 Remastered",
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
        if (data.TryGetValue("level_sequence", out var levelSeq) && levelSeq is JArray arr)
            slotData.LevelSequence = arr.ToObject<List<string>>() ?? new();

        return slotData;
    }
}
