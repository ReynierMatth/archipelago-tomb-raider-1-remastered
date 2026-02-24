using TRArchipelagoClient.Core;
using TRArchipelagoClient.UI;
using TRLevelControl.Model;

namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Monitors savegame.dat for changes and synchronizes game state with the
/// Archipelago server. This is the save-file-based approach — it detects
/// events when the player saves the game, rather than in real-time.
///
/// Reliable detection:
///   - Level changes (level index in save differs from last known)
///   - Secrets found (per-level bitmask comparison)
///   - Inventory state (weapons, ammo, medipacks)
///   - Victory conditions
///
/// Limitations vs runtime memory reading:
///   - Events are only detected when the player saves
///   - Individual entity pickups cannot be distinguished (only aggregate count)
///   - DeathLink sending is not possible (players cannot save when dead)
///   - Item injection requires the player to reload their save
/// </summary>
public class SaveFileGameWatcher : IDisposable
{
    private const int DebounceMs = 500;

    private readonly APSession _session;
    private readonly SaveFileReader _reader;
    private readonly SaveFileInventoryWriter _writer;
    private FileSystemWatcher? _fileWatcher;

    // State tracking
    private SaveSlotState? _previousState;
    private int _lastLevelIndex = -1; // 1-based
    private readonly HashSet<int> _completedLevels = new(); // 1-based level indices
    private readonly Dictionary<int, ushort> _levelSecretStates = new(); // levelIndex(1-based) -> bitmask
    private int _itemsReceivedIndex = 0;

    // Debounce
    private Timer? _debounceTimer;
    private readonly object _lock = new();

    // Cancellation for the async wait loop
    private CancellationTokenSource? _cts;

    public SaveFileGameWatcher(APSession session, string saveFilePath)
    {
        _session = session;
        _reader = new SaveFileReader(saveFilePath);
        _writer = new SaveFileInventoryWriter(saveFilePath);
    }

    /// <summary>
    /// Waits until the save file exists (the player has started a game and saved at least once).
    /// </summary>
    public async Task WaitForSaveFileAsync(CancellationToken ct = default)
    {
        while (!_reader.SaveFileExists && !ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);
        }
    }

    /// <summary>
    /// Starts monitoring the save file and runs until cancelled or disposed.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        string? directory = Path.GetDirectoryName(_reader.SaveFilePath);
        string fileName = Path.GetFileName(_reader.SaveFilePath);

        if (directory == null)
        {
            ConsoleUI.Error("Invalid save file path.");
            return;
        }

        // Read initial state
        _previousState = _reader.ReadLatestSlot();
        if (_previousState != null)
        {
            _lastLevelIndex = _previousState.LevelIndex;
            _levelSecretStates[_previousState.LevelIndex] = _previousState.SecretsFound;
            ConsoleUI.Info($"Current state: {_previousState.LevelName}, " +
                          $"HP: {_previousState.Health}/{TR1RMemoryMap.MaxHealth}, " +
                          $"Secrets: {_previousState.SecretCount}");
        }
        else
        {
            ConsoleUI.Info("No existing TR1 saves found. Waiting for first save...");
        }

        // Set up file watcher
        _fileWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _fileWatcher.Changed += OnFileChanged;

        ConsoleUI.Success("Save file monitoring active. Play the game and save to sync progress!");

        // Process any items already received from AP before we started
        ProcessReceivedItems();

        // Keep running until cancelled
        try
        {
            await Task.Delay(Timeout.Infinite, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    /// <summary>
    /// Stops monitoring.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Changed -= OnFileChanged;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }
        _debounceTimer?.Dispose();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: the game may write the file in multiple passes
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => ProcessSaveChange(), null, DebounceMs, Timeout.Infinite);
        }
    }

    private void ProcessSaveChange()
    {
        try
        {
            var newState = _reader.ReadLatestSlot();
            if (newState == null) return;

            // Skip if this is the exact same save (no actual change)
            if (_previousState != null && newState.SaveNumber == _previousState.SaveNumber)
                return;

            ConsoleUI.Info($"Save #{newState.SaveNumber} detected - {newState.LevelName}, " +
                          $"HP: {newState.Health}/{TR1RMemoryMap.MaxHealth}");

            DetectLevelChange(newState);
            DetectSecrets(newState);
            DetectPickups(newState);
            ProcessReceivedItems();
            InjectPendingItems(newState);
            CheckVictory();

            _previousState = newState;
        }
        catch (Exception ex)
        {
            ConsoleUI.Error($"Error processing save: {ex.Message}");
        }
    }

    private void DetectLevelChange(SaveSlotState newState)
    {
        if (newState.LevelIndex == _lastLevelIndex || _lastLevelIndex <= 0)
        {
            _lastLevelIndex = newState.LevelIndex;
            return;
        }

        // Player moved to a new level — mark the previous level as complete
        if (!_completedLevels.Contains(_lastLevelIndex))
        {
            // Convert 1-based save index to 0-based LocationMapper index
            int mapperIdx = _lastLevelIndex - 1;
            if (mapperIdx >= 0)
            {
                long locId = LocationMapper.GetLevelCompleteId(mapperIdx);
                _session.SendLocationCheck(locId);
                _completedLevels.Add(_lastLevelIndex);

                string prevName = TR1RMemoryMap.LevelNames.GetValueOrDefault(_lastLevelIndex, $"Level {_lastLevelIndex}");
                ConsoleUI.Success($"Completed: {prevName}");
            }
        }

        ConsoleUI.LevelChange(newState.LevelName);

        // Reset per-level secret tracking for the new level if we haven't seen it
        if (!_levelSecretStates.ContainsKey(newState.LevelIndex))
        {
            _levelSecretStates[newState.LevelIndex] = 0;
        }

        _lastLevelIndex = newState.LevelIndex;
    }

    private void DetectSecrets(SaveSlotState newState)
    {
        int level = newState.LevelIndex; // 1-based
        ushort previousSecrets = _levelSecretStates.GetValueOrDefault(level, (ushort)0);
        ushort currentSecrets = newState.SecretsFound;

        if (currentSecrets == previousSecrets)
            return;

        // Find newly discovered secrets (bits that are set now but weren't before)
        ushort newBits = (ushort)(currentSecrets & ~previousSecrets);
        int mapperIdx = level - 1; // 0-based for LocationMapper

        for (int s = 0; s < 16; s++)
        {
            if ((newBits & (1 << s)) != 0)
            {
                if (mapperIdx >= 0)
                {
                    long secretLocId = LocationMapper.GetSecretLocationId(mapperIdx, s);
                    _session.SendLocationCheck(secretLocId);
                }

                ConsoleUI.SecretFound(s + 1, newState.LevelName);
            }
        }

        _levelSecretStates[level] = currentSecrets;
    }

    private void DetectPickups(SaveSlotState newState)
    {
        if (_previousState == null) return;

        // We can only track the aggregate pickup count, not individual entities.
        // If the player is on the same level, compare pickup counts.
        if (newState.LevelIndex == _previousState.LevelIndex)
        {
            int delta = newState.Pickups - _previousState.Pickups;
            if (delta > 0)
            {
                ConsoleUI.Info($"{delta} new pickup(s) collected (entity-level tracking requires runtime mode)");
            }
        }
    }

    private void ProcessReceivedItems()
    {
        while (_session.TryDequeueReceivedItem(out var item))
        {
            string itemName = _session.GetItemName(item.ItemId);
            string playerName = _session.GetPlayerName(item.Player);

            var category = ItemMapper.GetCategory(item.ItemId);
            PendingItem? pending = MapToPendingItem(item.ItemId, category, itemName);

            if (pending != null)
            {
                _writer.QueueItem(pending);
            }

            ConsoleUI.ItemReceived(itemName, playerName);
            _itemsReceivedIndex++;
        }

        if (_writer.HasPendingItems)
        {
            ConsoleUI.Warning($"{_writer.PendingCount} item(s) queued. Save your game to receive them!");
        }
    }

    private void InjectPendingItems(SaveSlotState newState)
    {
        if (!_writer.HasPendingItems) return;

        int written = _writer.WritePendingItems(newState.SlotIndex);
        if (written > 0)
        {
            ConsoleUI.Warning($"{written} item(s) injected into save slot. Reload your save to receive them!");
        }
    }

    private static PendingItem? MapToPendingItem(long apItemId, ItemMapper.ItemCategory category, string displayName)
    {
        return category switch
        {
            ItemMapper.ItemCategory.Weapon => new PendingItem
            {
                Type = PendingItemType.Weapon,
                Amount = GetWeaponFlag(apItemId),
                DisplayName = displayName,
            },
            ItemMapper.ItemCategory.Ammo => MapAmmoItem(apItemId, displayName),
            ItemMapper.ItemCategory.Medipack => MapMedipackItem(apItemId, displayName),
            ItemMapper.ItemCategory.Trap => MapTrapItem(apItemId, displayName),
            // Key items via save file need special handling (not yet implemented)
            ItemMapper.ItemCategory.KeyItem => null,
            _ => null,
        };
    }

    private static int GetWeaponFlag(long apItemId)
    {
        var type = ItemMapper.GetTR1Type(apItemId);
        return type switch
        {
            TR1Type.Shotgun_S_P => TR1RMemoryMap.Weapon_Shotgun,
            TR1Type.Magnums_S_P => TR1RMemoryMap.Weapon_Magnums,
            TR1Type.Uzis_S_P => TR1RMemoryMap.Weapon_Uzis,
            _ => 0,
        };
    }

    private static PendingItem? MapAmmoItem(long apItemId, string displayName)
    {
        var type = ItemMapper.GetTR1Type(apItemId);
        return type switch
        {
            TR1Type.ShotgunAmmo_S_P => new PendingItem { Type = PendingItemType.ShotgunAmmo, Amount = 2, DisplayName = displayName },
            TR1Type.MagnumAmmo_S_P => new PendingItem { Type = PendingItemType.MagnumAmmo, Amount = 50, DisplayName = displayName },
            TR1Type.UziAmmo_S_P => new PendingItem { Type = PendingItemType.UziAmmo, Amount = 100, DisplayName = displayName },
            _ => null,
        };
    }

    private static PendingItem? MapMedipackItem(long apItemId, string displayName)
    {
        var type = ItemMapper.GetTR1Type(apItemId);
        return type switch
        {
            TR1Type.SmallMed_S_P => new PendingItem { Type = PendingItemType.SmallMedipack, Amount = 1, DisplayName = displayName },
            TR1Type.LargeMed_S_P => new PendingItem { Type = PendingItemType.LargeMedipack, Amount = 1, DisplayName = displayName },
            _ => null,
        };
    }

    private static PendingItem? MapTrapItem(long apItemId, string displayName)
    {
        int trapType = (int)(apItemId - 769_000);
        return trapType switch
        {
            1 => new PendingItem { Type = PendingItemType.DamageTrap, Amount = 0, DisplayName = displayName },
            2 => new PendingItem { Type = PendingItemType.AmmoDrain, Amount = 0, DisplayName = displayName },
            3 => new PendingItem { Type = PendingItemType.MedipackDrain, Amount = 0, DisplayName = displayName },
            _ => null,
        };
    }

    private void CheckVictory()
    {
        if (_session.SlotData == null) return;

        bool victory = _session.SlotData.Goal switch
        {
            // final_boss: complete The Great Pyramid (level index 15, 1-based)
            0 => _completedLevels.Contains(15),
            // all_secrets: would need to track secrets across all levels persistently
            1 => false, // TODO: sum secrets from all level states
            // n_levels: complete N levels
            2 => _completedLevels.Count >= _session.SlotData.LevelsForGoal,
            _ => false,
        };

        if (victory)
        {
            _session.SendGoalComplete();
            ConsoleUI.Success("GOAL COMPLETE! Congratulations!");
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
