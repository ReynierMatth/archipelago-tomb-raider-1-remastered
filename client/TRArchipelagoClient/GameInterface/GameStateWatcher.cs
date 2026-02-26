using TRArchipelagoClient.Core;
using TRArchipelagoClient.UI;

namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Real-time game state monitor. Polls tomb1.dll process memory at 100ms intervals
/// and synchronizes with the Archipelago server instantly.
///
/// Detection capabilities (all real-time, no save required):
///   - Level changes via LevelId offset
///   - Level completion via LevelCompleted flag
///   - Entity pickups via entity flags in the entities array
///   - Secrets via secrets bitmask in WorldStateBackup buffer
///   - Player death via health (Item_HitPoints)
///   - Game state (menu, in-game, loading)
///
/// Uses pointer chains from the Burns Multiplayer Mod (Patch 4.1).
/// </summary>
public class GameStateWatcher : IDisposable
{
    private const int PollIntervalMs = 100;

    private readonly APSession _session;
    private readonly ProcessMemory _memory;
    private readonly InventoryManager _inventory;
    private readonly InventoryScanner _scanner;

    // Cached base addresses
    private IntPtr _tomb1Base;
    private IntPtr _laraPtr;
    private IntPtr _entitiesBase;

    // Tracked state
    private int _lastLevelId = -1;
    private bool _wasInGame;
    private short _lastHealth = TR1RMemoryMap.MaxHealth;
    private int _lastLevelCompleted;
    private ushort _lastSecretsFound;
    private int _itemsReceivedIndex;
    private int _lastSaveNumber = -1;

    // Entity tracking: entityIndex -> last known flags value
    private readonly Dictionary<int, short> _entityFlags = new();

    // Items waiting for ring injection (deferred until Pistols pointer is found)
    private readonly Queue<(long ItemId, ItemMapper.ItemCategory Category)> _pendingItems = new();

    // Complete history of all received ring items (weapons, ammo, medipacks).
    // Used to replay items when the player starts a new game from the main menu.
    private readonly List<(long ItemId, ItemMapper.ItemCategory Category)> _allReceivedRingItems = new();

    // Cooldown after new-game detection: wait for the game engine to finish
    // reinitializing the inventory before writing to ring memory.
    private int _levelSettleTicks;

    // Which entity indices are AP locations (set by LevelPatcher)
    private readonly Dictionary<int, Dictionary<int, long>> _levelEntityLocations;

    // Completed tracking
    private readonly HashSet<int> _completedLevels = new();

    public GameStateWatcher(
        APSession session,
        ProcessMemory memory,
        Dictionary<int, Dictionary<int, long>> levelEntityLocations)
    {
        _session = session;
        _memory = memory;
        _inventory = new InventoryManager(memory);
        _scanner = new InventoryScanner(memory);
        _inventory.Scanner = _scanner;
        _levelEntityLocations = levelEntityLocations;
    }

    /// <summary>
    /// Waits until tomb123.exe is running and tomb1.dll is loaded.
    /// </summary>
    public async Task WaitForGameAsync(CancellationToken ct = default)
    {
        ConsoleUI.Info("Waiting for tomb123.exe...");

        while (!ct.IsCancellationRequested)
        {
            if (_memory.TryAttach())
            {
                if (_memory.Tomb1Base != IntPtr.Zero)
                {
                    _tomb1Base = _memory.Tomb1Base;
                    ConsoleUI.Success($"Attached! tomb1.dll at 0x{_tomb1Base:X}");
                    return;
                }

                // Process found but tomb1.dll not loaded yet (might be on menu)
                ConsoleUI.Info("tomb123.exe found, waiting for tomb1.dll to load...");
            }

            await Task.Delay(1000, ct);

            // Try refreshing module list
            if (_memory.IsAttached && _memory.Tomb1Base == IntPtr.Zero)
            {
                _memory.RefreshTomb1Base();
                if (_memory.Tomb1Base != IntPtr.Zero)
                {
                    _tomb1Base = _memory.Tomb1Base;
                    ConsoleUI.Success($"tomb1.dll loaded at 0x{_tomb1Base:X}");
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Main monitoring loop. Polls every 100ms until the game exits or cancelled.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        ConsoleUI.Success("Real-time monitoring active!");

        while (!ct.IsCancellationRequested && _memory.IsAttached)
        {
            try
            {
                PollGameState();
            }
            catch (Exception ex)
            {
                ConsoleUI.Error($"Poll error: {ex.Message}");
            }

            await Task.Delay(PollIntervalMs, ct);
        }

        ConsoleUI.Info("Game process ended.");
    }

    private void PollGameState()
    {
        // Check if we're in-game (not menu/loading)
        int inGameScene = _memory.ReadInt32(_tomb1Base, TR1RMemoryMap.IsInGameScene);
        bool isInGame = inGameScene > 0;

        if (!isInGame)
        {
            _wasInGame = false;
            return;
        }

        // Read current level
        int levelId = _memory.ReadInt32(_tomb1Base, TR1RMemoryMap.LevelId);

        // Skip non-game levels (Home=0, Menu=24)
        if (levelId == TR1RMemoryMap.Level_Home || levelId == TR1RMemoryMap.Level_MainMenu)
        {
            _lastLevelId = -1; // ensure OnLevelChanged fires when re-entering gameplay
            return;
        }

        // Resolve Lara pointer (must dereference)
        _laraPtr = _memory.ReadPointer(_tomb1Base, TR1RMemoryMap.LaraBase);
        if (_laraPtr == IntPtr.Zero)
            return;

        // Detect level change
        if (levelId != _lastLevelId)
        {
            OnLevelChanged(_lastLevelId, levelId);
            _lastLevelId = levelId;
        }

        if (!_wasInGame)
        {
            _wasInGame = true;

            // Reset cached pointers — heap addresses shift on any load (even same level)
            _scanner.Reset();
            _inventory.InvalidatePistolsPointer();

            // First frame in-game, initialize state
            _lastHealth = _memory.ReadInt16(_laraPtr + TR1RMemoryMap.Item_HitPoints);
            _lastLevelCompleted = _memory.ReadInt32(_tomb1Base, TR1RMemoryMap.LevelCompleted);
            _lastSecretsFound = 0;
            ReadSecretsState();
            _entityFlags.Clear();
            SnapshotEntityFlags(levelId);
        }

        // Check entity pickups (real-time!)
        CheckEntityPickups(levelId);

        // Check secrets
        CheckSecrets(levelId);

        // Check level completion flag
        CheckLevelCompletion(levelId);

        // Check health / death
        CheckHealth();

        // Auto-find live inventory address
        _scanner.Poll(_tomb1Base);

        // Process received items from AP (dequeues from AP always, but ring writes
        // are gated by _levelSettleTicks to avoid writing to uninitialized memory)
        ProcessReceivedItems(levelId);

        // Remove parasitic small medipacks from sentinel pickups
        _inventory.ProcessSentinelRemovals();

        // Wait for game to settle after new-game detection before touching rings
        if (_levelSettleTicks > 0)
        {
            _levelSettleTicks--;
            return;
        }

        // Detect save/load events via Save_Number change in WSB
        int saveNumber = _memory.ReadInt32(
            _memory.Tomb1Base + TR1RMemoryMap.WorldStateBackup + TR1RMemoryMap.Save_Number);
        if (_lastSaveNumber >= 0 && saveNumber != _lastSaveNumber)
        {
            // Save or load just happened — re-check ring vs AP items
            _inventory.ResetKeyItemEnsurance();
            _inventory.InvalidatePistolsPointer();
        }
        _lastSaveNumber = saveNumber;

        // Ensure received key items are present in Keys Ring
        _inventory.EnsureKeyItemsInRing(levelId);
    }

    private void OnLevelChanged(int previousLevelId, int newLevelId)
    {
        string newName = TR1RMemoryMap.LevelNames.GetValueOrDefault(newLevelId, $"Level {newLevelId}");

        // Level completion is handled by CheckLevelCompletion (LevelCompleted flag),
        // NOT here — OnLevelChanged also fires on save loads.

        ConsoleUI.LevelChange(newName);

        // Reset inventory scanner (heap addresses shift on level load)
        _scanner.Reset();

        // Invalidate Pistols pointer — will be re-found lazily when ring is stable
        _inventory.InvalidatePistolsPointer();

        // Reset key item tracking for the new level
        _inventory.ResetKeyItemEnsurance();
        _inventory.ResetSentinelRemovals();

        // Reset per-level state
        _entityFlags.Clear();
        _lastSecretsFound = 0;
        _lastLevelCompleted = 0;
        _lastHealth = TR1RMemoryMap.MaxHealth;
        _lastSaveNumber = -1; // re-capture on next tick

        // Coming from menu/home — replay all ring items (new game or save loaded from menu)
        if (previousLevelId == -1 && _allReceivedRingItems.Count > 0)
        {
            _pendingItems.Clear();
            foreach (var item in _allReceivedRingItems)
                _pendingItems.Enqueue(item);

            // Wait for the game engine to finish reinitializing the inventory.
            // Writing to ring memory too early gets overwritten by the engine's own init.
            _levelSettleTicks = 20; // 2 seconds (20 × 100ms)
            ConsoleUI.Info($"[GSW] New game detected — replaying {_allReceivedRingItems.Count} ring items (waiting 2s for init)");
        }

        // Snapshot entity flags for the new level
        SnapshotEntityFlags(newLevelId);
    }

    /// <summary>
    /// Takes a snapshot of all tracked entity flags for the current level.
    /// </summary>
    private void SnapshotEntityFlags(int levelId)
    {
        _entityFlags.Clear();

        int mapperIdx = TR1RMemoryMap.ToLocationMapperIndex(levelId);
        if (mapperIdx < 0 || !_levelEntityLocations.ContainsKey(mapperIdx))
            return;

        // Resolve entities array
        _entitiesBase = _memory.ReadPointer(_tomb1Base, TR1RMemoryMap.EntitiesPointer);
        if (_entitiesBase == IntPtr.Zero)
            return;

        var entityLocations = _levelEntityLocations[mapperIdx];
        foreach (int entityIndex in entityLocations.Keys)
        {
            IntPtr entityAddr = _entitiesBase + (entityIndex * TR1RMemoryMap.EntitySize);
            short flags = _memory.ReadInt16(entityAddr + TR1RMemoryMap.Item_Flags);
            _entityFlags[entityIndex] = flags;
        }
    }

    /// <summary>
    /// Checks each tracked entity for flag changes (pickup detection).
    /// When an entity's flags change, it means the player interacted with it.
    /// </summary>
    private void CheckEntityPickups(int levelId)
    {
        int mapperIdx = TR1RMemoryMap.ToLocationMapperIndex(levelId);
        if (mapperIdx < 0 || !_levelEntityLocations.ContainsKey(mapperIdx))
            return;

        if (_entitiesBase == IntPtr.Zero)
        {
            _entitiesBase = _memory.ReadPointer(_tomb1Base, TR1RMemoryMap.EntitiesPointer);
            if (_entitiesBase == IntPtr.Zero) return;
        }

        var entityLocations = _levelEntityLocations[mapperIdx];
        foreach (var (entityIndex, locationId) in entityLocations)
        {
            IntPtr entityAddr = _entitiesBase + (entityIndex * TR1RMemoryMap.EntitySize);
            short currentFlags = _memory.ReadInt16(entityAddr + TR1RMemoryMap.Item_Flags);

            if (_entityFlags.TryGetValue(entityIndex, out short previousFlags))
            {
                if (currentFlags != previousFlags)
                {
                    // Entity flags changed — it was picked up!
                    _session.SendLocationCheck(locationId);
                    string locName = _session.GetLocationName(locationId);
                    ConsoleUI.ItemSent(locName, "Archipelago");
                    _entityFlags[entityIndex] = currentFlags;

                    // Cancel the parasitic small medipack the game adds when
                    // picking up a sentinel (SmallMed_S_P) entity.
                    _inventory.QueueSentinelRemoval();
                }
            }
            else
            {
                // First time seeing this entity
                _entityFlags[entityIndex] = currentFlags;
            }
        }
    }

    /// <summary>
    /// Reads secrets bitmask from the WorldStateBackup buffer and detects new secrets.
    /// </summary>
    private void CheckSecrets(int levelId)
    {
        int mapperIdx = TR1RMemoryMap.ToLocationMapperIndex(levelId);
        if (mapperIdx < 0) return;

        ReadSecretsState();
    }

    private void ReadSecretsState()
    {
        // Secrets bitmask is at the same offset as in save file, within the WorldStateBackup
        ushort secrets = _memory.ReadUInt16(
            _tomb1Base + TR1RMemoryMap.WorldStateBackup + TR1RMemoryMap.Runtime_SecretsFound);

        if (secrets != _lastSecretsFound)
        {
            ushort newBits = (ushort)(secrets & ~_lastSecretsFound);
            int mapperIdx = TR1RMemoryMap.ToLocationMapperIndex(_lastLevelId);

            for (int s = 0; s < 16; s++)
            {
                if ((newBits & (1 << s)) != 0 && mapperIdx >= 0)
                {
                    long secretLocId = LocationMapper.GetSecretLocationId(mapperIdx, s);
                    _session.SendLocationCheck(secretLocId);

                    string levelName = TR1RMemoryMap.LevelNames.GetValueOrDefault(_lastLevelId, "Unknown");
                    ConsoleUI.SecretFound(s + 1, levelName);
                }
            }

            _lastSecretsFound = secrets;
        }
    }

    /// <summary>
    /// Checks the LevelCompleted flag for real-time level completion detection.
    /// </summary>
    private void CheckLevelCompletion(int levelId)
    {
        int completed = _memory.ReadInt32(_tomb1Base, TR1RMemoryMap.LevelCompleted);

        if (completed == 1 && _lastLevelCompleted != 1)
        {
            int mapperIdx = TR1RMemoryMap.ToLocationMapperIndex(levelId);
            if (mapperIdx >= 0 && !_completedLevels.Contains(levelId))
            {
                long locId = LocationMapper.GetLevelCompleteId(mapperIdx);
                _session.SendLocationCheck(locId);
                _completedLevels.Add(levelId);

                string levelName = TR1RMemoryMap.LevelNames.GetValueOrDefault(levelId, $"Level {levelId}");
                ConsoleUI.Success($"Completed: {levelName}");

                CheckVictory();
            }
        }

        _lastLevelCompleted = completed;
    }

    /// <summary>
    /// Checks player health for DeathLink.
    /// </summary>
    private void CheckHealth()
    {
        short health = _memory.ReadInt16(_laraPtr + TR1RMemoryMap.Item_HitPoints);

        // Detect death: health dropped to 0 or below
        if (health <= 0 && _lastHealth > 0)
        {
            if (_session.SlotData?.DeathLink == true)
            {
                _session.SendDeathLink();
                ConsoleUI.Warning("You died! DeathLink sent to other players.");
            }
        }

        _lastHealth = health;
    }

    /// <summary>
    /// Processes items received from other AP players and injects them in real-time.
    /// Items that need ring injection (weapons, ammo, medipacks) are deferred to
    /// _pendingItems if the inventory isn't ready yet. Traps and key items are
    /// processed immediately (they don't need the Pistols pointer).
    /// </summary>
    private void ProcessReceivedItems(int levelId)
    {
        // Dequeue all new items from AP
        while (_session.TryDequeueReceivedItem(out var item))
        {
            string itemName = _session.GetItemName(item.ItemId);
            string playerName = _session.GetPlayerName(item.Player);
            ConsoleUI.ItemReceived(itemName, playerName);
            _itemsReceivedIndex++;

            var category = ItemMapper.GetCategory(item.ItemId);

            // Traps and key items don't need the ring — process immediately
            if (category == ItemMapper.ItemCategory.Trap)
            {
                _inventory.ApplyTrap(item.ItemId);
                continue;
            }
            if (category == ItemMapper.ItemCategory.KeyItem)
            {
                _inventory.GiveKeyItem(item.ItemId, levelId);
                continue;
            }

            // Ring items: queue for injection (processed below) + save for new game replay
            _pendingItems.Enqueue((item.ItemId, category));
            _allReceivedRingItems.Add((item.ItemId, category));
        }

        // Process pending ring items (weapons, ammo, medipacks)
        if (_pendingItems.Count == 0) return;
        if (_levelSettleTicks > 0) return; // wait for game to finish initializing
        if (!_inventory.IsInventoryReady()) return;

        while (_pendingItems.Count > 0)
        {
            var (itemId, category) = _pendingItems.Dequeue();
            switch (category)
            {
                case ItemMapper.ItemCategory.Weapon:
                    _inventory.GiveWeapon(itemId);
                    break;
                case ItemMapper.ItemCategory.Ammo:
                    _inventory.GiveAmmo(itemId);
                    break;
                case ItemMapper.ItemCategory.Medipack:
                    _inventory.GiveMedipack(itemId);
                    break;
            }
        }
    }

    private void CheckVictory()
    {
        if (_session.SlotData == null) return;

        bool victory = _session.SlotData.Goal switch
        {
            0 => _completedLevels.Contains(TR1RMemoryMap.Level_Pyramid),
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
        _memory.Dispose();
    }
}
