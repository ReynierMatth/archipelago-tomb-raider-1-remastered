using Newtonsoft.Json;
using TRArchipelagoClient.UI;

namespace TRArchipelagoClient.Core;

/// <summary>
/// Tracks AP state per save number in a local JSON file.
/// When the player saves, a snapshot of the current AP state is recorded.
/// When the player loads, the snapshot is used to reconcile without false pickups.
/// </summary>
public class SaveStateStore
{
    private readonly string _filePath;
    private SaveStateFile _data;

    public SaveStateStore(string slotName, string seed)
    {
        _filePath = Path.Combine(AppContext.BaseDirectory, $"tr1r_ap_state_{slotName}.json");

        if (File.Exists(_filePath))
        {
            try
            {
                string json = File.ReadAllText(_filePath);
                _data = JsonConvert.DeserializeObject<SaveStateFile>(json) ?? new();

                if (_data.Seed != seed)
                {
                    ConsoleUI.Warning($"[SAVE] Seed mismatch (file={_data.Seed}, server={seed}). Starting fresh.");
                    _data = new SaveStateFile { Seed = seed };
                }
                else
                {
                    ConsoleUI.Info($"[SAVE] Loaded state file with {_data.Snapshots.Count} snapshots.");
                }
            }
            catch (Exception ex)
            {
                ConsoleUI.Warning($"[SAVE] Failed to load state file: {ex.Message}. Starting fresh.");
                _data = new SaveStateFile { Seed = seed };
            }
        }
        else
        {
            _data = new SaveStateFile { Seed = seed };
        }
    }

    public void RecordSave(int saveNumber, SaveSnapshot snapshot)
    {
        _data.Snapshots[saveNumber] = snapshot;
    }

    public SaveSnapshot? GetSnapshot(int saveNumber)
    {
        return _data.Snapshots.GetValueOrDefault(saveNumber);
    }

    public bool HasSnapshot(int saveNumber)
    {
        return _data.Snapshots.ContainsKey(saveNumber);
    }

    public void Persist()
    {
        try
        {
            string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            ConsoleUI.Error($"[SAVE] Failed to persist state: {ex.Message}");
        }
    }
}

/// <summary>
/// Root object persisted to JSON.
/// </summary>
public class SaveStateFile
{
    public string Seed { get; set; } = "";
    public Dictionary<int, SaveSnapshot> Snapshots { get; set; } = new();
}

/// <summary>
/// Snapshot of the AP state at the time of a game save.
/// </summary>
public class SaveSnapshot
{
    /// <summary>Runtime level ID at the time of save.</summary>
    public int LevelId { get; set; }

    /// <summary>LocationMapper index for the level.</summary>
    public int MapperIndex { get; set; }

    /// <summary>AP IDs of ring items (weapons/ammo/medipacks) received up to this save.</summary>
    public List<long> ReceivedRingItems { get; set; } = new();

    /// <summary>Key items received per mapper index: mapperIdx → list of AP IDs.</summary>
    public Dictionary<int, List<long>> ReceivedKeyItems { get; set; } = new();

    /// <summary>AP IDs of key items consumed (used in a door/lock): apItemId → count used.</summary>
    public Dictionary<long, int> UsedKeyItems { get; set; } = new();

    /// <summary>AP location IDs of entity pickups that have been checked.</summary>
    public HashSet<long> CheckedEntityLocations { get; set; } = new();

    /// <summary>AP location IDs of secrets that have been found.</summary>
    public HashSet<long> CheckedSecretLocations { get; set; } = new();

    /// <summary>Index into the AP item stream at the time of save.</summary>
    public int ItemsReceivedIndex { get; set; }
}
