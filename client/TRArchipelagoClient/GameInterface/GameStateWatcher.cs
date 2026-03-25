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
///   - Save/load events via Save_Number with deterministic state reconciliation
///
/// Uses pointer chains from the Burns Multiplayer Mod (Patch 4.1).
/// </summary>
public class GameStateWatcher : IDisposable
{
    private const int PollIntervalMs = 100;

    private readonly APSession _session;
    private readonly ProcessMemory _memory;
    private ItemMapper _itemMapper;
    private LocationMapper _locationMapper;
    private readonly InventoryManager _inventory;
    private readonly InventoryScanner _scanner;
    private readonly SaveStateStore _stateStore;
    private readonly KeyItemMonitor _keyMonitor;

    // Game context (active game map + DLL base)
    private readonly GameContext _context;

    // Cached addresses
    private IntPtr _laraPtr;
    private IntPtr _entitiesBase;

    // Tracked state
    private int _lastLevelId = -1;
    private bool _wasInGame;
    private short _lastHealth = 1000; // MaxHealth, updated from _context.Map after init
    private int _lastLevelCompleted;
    private ushort _lastSecretsFound;
    private int _itemsReceivedIndex;
    private int _lastSaveNumber = -1;

    // The save number of the currently active snapshot (for save-aware checks)
    private int _activeSaveNumber = -1;

    // Tracks KeyItemsEnsured transition to re-snapshot the KeyItemMonitor
    private bool _lastKeyItemsEnsured;

    // Saved Lara pointer to detect heap shifts (real load vs inventory open)
    private IntPtr _laraPtrBeforeTransition;

    // BinaryTick-based in-game detection (fallback when IsInGameScene offset is stale)
    private int _lastBinaryTick;
    private bool _binaryTickMoving;

    // Entity tracking: entityIndex -> last known flags value
    private readonly Dictionary<int, short> _entityFlags = new();

    // Items waiting for ring injection (deferred until Compass pointer is found)
    private readonly Queue<(long ItemId, ItemCategory Category)> _pendingItems = new();

    // Complete history of all received ring items (weapons, ammo, medipacks).
    // Used to replay items when the player starts a new game from the main menu.
    private readonly List<(long ItemId, ItemCategory Category)> _allReceivedRingItems = new();

    // Cooldown after level transition: wait for the game engine to finish
    // reinitializing entities/inventory before monitoring.
    private int _levelSettleTicks;
    private bool _settleFromMenu; // true = menu→game, false = level→level

    // Post-settle stabilization: re-snapshot entity flags after a few ticks
    // to absorb engine initialization flag changes (especially TR2/TR3).
    private int _postSettleResnapTicks;

    // Tracks which game versions have had their ring items replayed this session.
    // Only replay once per game — subsequent entries use normal save reconciliation.
    private readonly HashSet<int> _replayedGameVersions = new();

    // Which entity indices are AP locations (set by LevelPatcher)
    // Keys are composite: gameVersion * 1000 + mapperIdx
    private readonly Dictionary<int, Dictionary<int, long>> _levelEntityLocations;

    /// <summary>
    /// Composite key for _levelEntityLocations: gameVersion * 1000 + mapperIdx.
    /// Prevents collisions between games that share level indices.
    /// </summary>
    private int EntityLocationKey(int mapperIdx) => _context.GameVersion * 1000 + mapperIdx;

    // Completed tracking
    private readonly HashSet<int> _completedLevels = new();

    public GameStateWatcher(
        APSession session,
        ProcessMemory memory,
        ItemMapper itemMapper,
        LocationMapper locationMapper,
        Dictionary<int, Dictionary<int, long>> levelEntityLocations,
        SaveStateStore stateStore,
        GameContext context)
    {
        _session = session;
        _memory = memory;
        _itemMapper = itemMapper;
        _locationMapper = locationMapper;
        _context = context;
        _inventory = new InventoryManager(memory, itemMapper, locationMapper);
        _inventory.SetGameContext(context);
        _inventory.SetSlotData(session.SlotData);
        _scanner = new InventoryScanner(memory, context);
        _inventory.Scanner = _scanner;
        _levelEntityLocations = levelEntityLocations;
        _stateStore = stateStore;
        _keyMonitor = new KeyItemMonitor(memory, context);
    }

    /// <summary>
    /// Waits until tomb123.exe is running and any game DLL is loaded.
    /// Updates the GameContext with the detected game.
    /// </summary>
    public async Task WaitForGameAsync(CancellationToken ct = default)
    {
        ConsoleUI.Info("Waiting for tomb123.exe...");

        while (!ct.IsCancellationRequested)
        {
            if (_memory.TryAttach())
            {
                // Check for any loaded game DLL
                _memory.RefreshTomb1Base(); // also refreshes tomb2/tomb3
                IntPtr dllBase = _context.Map.GetDllBase(_memory);
                if (dllBase != IntPtr.Zero)
                {
                    _context.DllBase = dllBase;
                    ConsoleUI.Success($"Attached! {_context.Map.ModuleName} at 0x{dllBase:X}");
                    return;
                }

                ConsoleUI.Info("tomb123.exe found, waiting for game DLL to load...");
            }

            await Task.Delay(1000, ct);

            if (_memory.IsAttached)
            {
                _memory.RefreshTomb1Base();
                IntPtr dllBase = _context.Map.GetDllBase(_memory);
                if (dllBase != IntPtr.Zero)
                {
                    _context.DllBase = dllBase;
                    ConsoleUI.Success($"{_context.Map.ModuleName} loaded at 0x{dllBase:X}");
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
        // Detect game switch (TR1 ↔ TR2 ↔ TR3)
        int gameVersion = _memory.ReadInt32(_memory.ExeBase + TR1RMemoryMap.Exe_GameVersion);
        if (gameVersion != _context.GameVersion && gameVersion >= 0 && gameVersion <= 2)
        {
            var newMap = GameMemoryMapFactory.Create(gameVersion);
            IntPtr newBase = newMap.GetDllBase(_memory);
            if (newBase != IntPtr.Zero)
            {
                _context.SwitchGame(gameVersion, newMap, newBase);
                ConsoleUI.Info($"[GSW] Switched to {newMap.GameKey.ToUpper()} ({newMap.ModuleName} at 0x{newBase:X})");

                // Switch per-game mappers if registered in context
                if (_context.ActiveItemMapper != null)
                    _itemMapper = _context.ActiveItemMapper;
                if (_context.ActiveLocationMapper != null)
                    _locationMapper = _context.ActiveLocationMapper;

                // Reset all cached state
                _lastLevelId = -1;
                _wasInGame = false;
                _entityFlags.Clear();
                _scanner.Reset();
                _inventory.InvalidateCompassPointer();
            }
        }

        var map = _context.Map;
        IntPtr dllBase = _context.DllBase;

        // Check if we're in-game (not menu/loading)
        int inGameScene = _memory.ReadInt32(dllBase, map.IsInGameScene);
        bool isInGame;

        if (inGameScene >= 0 && inGameScene <= 10)
        {
            // Normal range: 0 = menu/loading, >0 = in gameplay
            isInGame = inGameScene > 0;
        }
        else
        {
            // Garbage value — IsInGameScene offset is stale for this game.
            // Fallback: check if entities are loaded (valid pointer + count > 0).
            // In menus, the entities array is null/empty. In levels, it's populated.
            IntPtr entPtr = _memory.ReadPointer(dllBase + map.EntitiesPointer);
            int entCount = _memory.ReadInt32(dllBase + map.EntitiesCount);
            int fallbackLevel = _memory.ReadInt32(dllBase, map.LevelId);

            isInGame = entPtr != IntPtr.Zero && entCount > 5
                && fallbackLevel > map.Level_Home
                && fallbackLevel != map.Level_MainMenu;
        }

        if (!isInGame)
        {
            if (_wasInGame)
                _laraPtrBeforeTransition = _laraPtr;
            _wasInGame = false;
            return;
        }

        // Read current level
        int levelId = _memory.ReadInt32(dllBase, map.LevelId);

        // Skip non-game levels (Home, Menu)
        if (levelId == map.Level_Home || levelId == map.Level_MainMenu)
        {
            _lastLevelId = -1;
            return;
        }

        // Resolve Lara pointer (TR1/TR3 use this for HP; TR2 doesn't need it)
        if (_context.GameVersion != 1) // not TR2
            _laraPtr = _memory.ReadPointer(dllBase + TR1RMemoryMap.LaraBase);
        // For TR2, health is read from a static address via map.ReadHealth()

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
            _inventory.InvalidateCompassPointer();
            _inventory.ResetKeyItemEnsurance();

            // First frame in-game — capture current state without reporting changes.
            // This block also fires on inventory open / cutscenes (isInGameScene briefly 0),
            // so we must NOT reset to 0 (would re-report secrets/pickups as new).
            // NOTE: Do NOT snapshot entity flags here — this block fires at unstable
            // moments (during loading flickers) and would overwrite the stable snapshot
            // from OnSettleComplete. Entity flags are managed by OnSettleComplete (after
            // level transitions) and the Save_Number change handler (after save loads).
            _lastHealth = map.ReadHealth(_memory, dllBase);
            _lastLevelCompleted = _memory.ReadInt32(dllBase, map.LevelCompleted);
            _lastSecretsFound = map.ReadSecrets(_memory, dllBase);

            // If a settle period is active, restart it — we just came back from a
            // loading screen (isInGameScene was 0). The settle must count from AFTER
            // the game finishes loading, not from when the levelId changed.
            if (_levelSettleTicks > 0)
            {
                _levelSettleTicks = 20;
            }
            else if (_activeSaveNumber >= 0
                     && _laraPtrBeforeTransition != IntPtr.Zero
                     && _laraPtr != _laraPtrBeforeTransition)
            {
                // Heap shifted (Lara pointer changed) → real load happened, not
                // just an inventory open. HandleSaveNumberChange will catch most
                // loads (different counter), but if the player reloads the exact
                // same save (counter unchanged), only this block detects it.
                _keyMonitor.Pause();
                _entityFlags.Clear();
                SnapshotEntityFlags(levelId);
                _inventory.ResetSentinelRemovals();

                var snapshot = _stateStore.GetSnapshot(_activeSaveNumber);
                if (snapshot != null)
                {
                    ReconcileAfterLoad(snapshot, levelId);
                    ConsoleUI.Info($"[SAVE] Reconciled from snapshot #{_activeSaveNumber} (same-counter reload)");
                }

                _keyMonitor.SnapshotKeysRing();
                _keyMonitor.Resume();
            }
        }

        // During settle after menu→game transition: skip ALL monitoring.
        // The game engine may still be initializing rings/entities/health.
        // At settle end, take fresh snapshots from the now-stable game state.
        if (_levelSettleTicks > 0)
        {
            _levelSettleTicks--;
            if (_levelSettleTicks == 0)
                OnSettleComplete(levelId);
            return;
        }

        // Detect save/load events via Save_Number change in WSB.
        // MUST be before CheckEntityPickups — after a reload, entity flags revert
        // to the saved state, so we need to re-snapshot before checking for changes.
        int saveNumber = _memory.ReadInt32(
            dllBase + map.WorldStateBackup + map.WSB_SaveCounter);
        if (_lastSaveNumber >= 0 && saveNumber != _lastSaveNumber)
        {
            HandleSaveNumberChange(saveNumber, levelId);
        }
        _lastSaveNumber = saveNumber;

        // Post-settle stabilization: re-snapshot to absorb engine init flag changes
        if (_postSettleResnapTicks > 0)
        {
            _postSettleResnapTicks--;
            if (_postSettleResnapTicks == 0)
            {
                _entityFlags.Clear();
                SnapshotEntityFlags(levelId);
            }
            // Don't check entity pickups during stabilization
        }
        else
        {
            // Check entity pickups (real-time!)
            CheckEntityPickups(levelId);
        }

        // Check secrets
        CheckSecrets(levelId);

        // Check level completion flag
        CheckLevelCompletion(levelId);

        // Check health / death
        CheckHealth();

        // Detect key items consumed in doors/locks
        CheckKeyItemUsage(levelId);

        // Auto-find live inventory address
        _scanner.Poll();

        // Process received items from AP
        ProcessReceivedItems(levelId);

        // Remove parasitic small medipacks from sentinel pickups
        _inventory.ProcessSentinelRemovals();

        // Ensure received key items are present in Keys Ring
        _inventory.EnsureKeyItemsInRing(levelId);

        // Once key items stabilize in the ring, re-snapshot the KeyItemMonitor
        // so it can detect future usage (key disappearing from ring = used in a door).
        // The monitor snapshot from OnSettleComplete is stale because it was taken
        // BEFORE EnsureKeyItemsInRing injected the items.
        if (_inventory.KeyItemsEnsured && !_lastKeyItemsEnsured)
        {
            _keyMonitor.SnapshotKeysRing();
            ConsoleUI.Info("[SAVE] Keys Ring stabilized — monitoring for key usage");
        }
        _lastKeyItemsEnsured = _inventory.KeyItemsEnsured;
    }

    /// <summary>
    /// Determines whether a Save_Number change is a new save or a load,
    /// and handles accordingly.
    /// </summary>
    private void HandleSaveNumberChange(int saveNumber, int levelId)
    {
        // Pause key item monitoring during the transition
        _keyMonitor.Pause();

        if (saveNumber > _lastSaveNumber && !_stateStore.HasSnapshot(saveNumber))
        {
            // Save_Number increased and we have no snapshot → player saved the game
            OnGameSaved(saveNumber, levelId);
        }
        else
        {
            // Save_Number changed to a known value (or decreased) → player loaded a save
            OnSaveLoaded(saveNumber, levelId);
        }
    }

    /// <summary>
    /// Called when the player saves the game (new save number, no existing snapshot).
    /// Captures the current AP state into a SaveSnapshot and persists it.
    /// </summary>
    private void OnGameSaved(int saveNumber, int levelId)
    {
        int mapperIdx = _context.Map.ToLocationMapperIndex(levelId);

        // Build checked location sets from AP session
        var checkedEntities = new HashSet<long>();
        var checkedSecrets = new HashSet<long>();
        foreach (long locId in _session.GetCheckedLocations())
        {
            var locType = _locationMapper.GetLocationType(locId);
            if (locType == LocationMapper.LocationType.Pickup)
                checkedEntities.Add(locId);
            else if (locType == LocationMapper.LocationType.Secret)
                checkedSecrets.Add(locId);
        }

        // Capture the LIVE used key items — this is the truth at save time.
        // We must NOT copy from the active snapshot because MarkKeyItemUsed
        // intentionally skips the active snapshot to preserve its save-time state.
        var usedKeys = _inventory.GetUsedKeyItems();

        var snapshot = new SaveSnapshot
        {
            LevelId = levelId,
            MapperIndex = mapperIdx,
            ReceivedRingItems = _allReceivedRingItems.Select(r => r.ItemId).ToList(),
            ReceivedKeyItems = _inventory.CloneReceivedKeyItems(),
            UsedKeyItems = usedKeys,
            CheckedEntityLocations = checkedEntities,
            CheckedSecretLocations = checkedSecrets,
            ItemsReceivedIndex = _itemsReceivedIndex,
        };

        _stateStore.RecordSave(saveNumber, snapshot);
        _activeSaveNumber = saveNumber;
        _stateStore.Persist();

        ConsoleUI.Info($"[SAVE] Game saved (save #{saveNumber}, level={levelId}, entities={checkedEntities.Count}, secrets={checkedSecrets.Count})");

        // Resume key item monitoring with a fresh snapshot
        _keyMonitor.SnapshotKeysRing();
        _keyMonitor.Resume();
    }

    /// <summary>
    /// Called when the player loads a save (Save_Number matches a known snapshot or decreased).
    /// Re-snapshots entity flags and reconciles AP state from the saved snapshot.
    /// </summary>
    private void OnSaveLoaded(int saveNumber, int levelId)
    {
        ConsoleUI.Info($"[SAVE] Save loaded (save #{saveNumber})");

        // Re-snapshot entity flags for the current game state (post-load)
        _entityFlags.Clear();
        SnapshotEntityFlags(levelId);

        // Reset inventory pointers (heap shifts on any load)
        _inventory.ResetKeyItemEnsurance();
        _inventory.InvalidateCompassPointer();
        _inventory.ResetSentinelRemovals();

        _activeSaveNumber = saveNumber;

        // Reconcile from snapshot if available
        var snapshot = _stateStore.GetSnapshot(saveNumber);
        if (snapshot != null)
        {
            ReconcileAfterLoad(snapshot, levelId);
        }
        else
        {
            ConsoleUI.Warning($"[SAVE] No snapshot for save #{saveNumber} — skipping reconciliation");
        }

        // Take a fresh Keys Ring snapshot for usage detection
        _keyMonitor.SnapshotKeysRing();
        _keyMonitor.Resume();
    }

    /// <summary>
    /// Reconciles AP state after loading a save. Queues ring items received after
    /// the save and re-injects non-used key items.
    /// </summary>
    private void ReconcileAfterLoad(SaveSnapshot snapshot, int levelId)
    {
        int mapperIdx = _context.Map.ToLocationMapperIndex(levelId);

        // Set used key items so EnsureKeyItemsInRing skips them
        _inventory.SetUsedKeyItems(new Dictionary<long, int>(snapshot.UsedKeyItems));

        // Queue ring items received AFTER this save for re-injection
        int savedIndex = snapshot.ItemsReceivedIndex;
        if (savedIndex < _allReceivedRingItems.Count)
        {
            int count = _allReceivedRingItems.Count - savedIndex;
            _pendingItems.Clear();
            for (int i = savedIndex; i < _allReceivedRingItems.Count; i++)
                _pendingItems.Enqueue(_allReceivedRingItems[i]);

            ConsoleUI.Info($"[SAVE] Queued {count} ring items received after save #{_activeSaveNumber}");
        }

        // Reconcile key items for the current level
        if (mapperIdx >= 0)
        {
            _inventory.ReconcileKeyItems(mapperIdx, new Dictionary<long, int>(snapshot.UsedKeyItems));
        }
    }

    private void OnLevelChanged(int previousLevelId, int newLevelId)
    {
        string newName = _context.Map.LevelNames.GetValueOrDefault(newLevelId, $"Level {newLevelId}");

        // Level completion is handled by CheckLevelCompletion (LevelCompleted flag),
        // NOT here — OnLevelChanged also fires on save loads.

        ConsoleUI.LevelChange(newName);

        // Pause key item monitoring during level transition
        _keyMonitor.Pause();

        // Reset inventory scanner (heap addresses shift on level load)
        _scanner.Reset();

        // Invalidate Compass pointer — will be re-found lazily when ring is stable
        _inventory.InvalidateCompassPointer();

        // Reset key item tracking for the new level
        _inventory.ResetKeyItemEnsurance();
        _inventory.ResetSentinelRemovals();

        // Reset per-level state
        _entityFlags.Clear();
        _lastSecretsFound = 0;
        _lastLevelCompleted = 0;
        _lastHealth = _context.Map.MaxHealth;
        _lastSaveNumber = -1; // re-capture on next tick

        // Wait for the game engine to finish initializing entities/rings
        // before monitoring. OnSettleComplete takes fresh snapshots once stable.
        _settleFromMenu = previousLevelId == -1;
        _levelSettleTicks = 20; // 2 seconds (20 × 100ms)
    }

    /// <summary>
    /// Called when the settle period after a level transition ends.
    /// The game engine has had 2 seconds to fully initialize. Takes fresh
    /// snapshots of all game state so that CheckEntityPickups has a stable baseline.
    /// For menu→game transitions, also decides whether to replay ring items.
    /// </summary>
    private void OnSettleComplete(int levelId)
    {
        // Drain the AP item queue BEFORE reconciling.
        // Items accumulate during settle — we need them in _allReceivedRingItems
        // so ReconcileAfterLoad can correctly slice by savedIndex.
        // Do NOT add to _pendingItems here; reconciliation decides what to inject.
        DrainReceivedItemQueue(levelId);

        // Fresh snapshots from now-stable game state
        _entityFlags.Clear();
        SnapshotEntityFlags(levelId);

        // Schedule a re-snapshot after a few ticks to absorb any remaining
        // engine initialization flag changes (TR2/TR3 entity init is delayed).
        _postSettleResnapTicks = 5; // 0.5 seconds

        var map = _context.Map;
        IntPtr dllBase = _context.DllBase;
        _lastSecretsFound = map.ReadSecrets(_memory, dllBase);
        _lastHealth = map.ReadHealth(_memory, dllBase);
        _lastLevelCompleted = _memory.ReadInt32(dllBase, map.LevelCompleted);
        _lastSaveNumber = _memory.ReadInt32(
            dllBase + map.WorldStateBackup + map.WSB_SaveCounter);
        _scanner.Reset();
        _inventory.InvalidateCompassPointer();

        // Take a fresh Keys Ring snapshot and resume monitoring
        _keyMonitor.SnapshotKeysRing();
        _keyMonitor.Resume();

        // After switching to a different game (TR1↔TR2↔TR3), replay all ring items
        // so items received while playing another game get injected.
        // The InventoryManager will only inject items whose recipes match the
        // active game — cross-game items are silently skipped.
        // Only replay once per game switch (not on every TR3→menu→TR3 cycle).
        if (!_replayedGameVersions.Contains(_context.GameVersion) && _allReceivedRingItems.Count > 0)
        {
            _replayedGameVersions.Add(_context.GameVersion);
            _pendingItems.Clear();
            foreach (var item in _allReceivedRingItems)
                _pendingItems.Enqueue(item);
            ConsoleUI.Info($"[GSW] First entry into {_context.Map.GameKey.ToUpper()} — replaying {_allReceivedRingItems.Count} ring items");
            return;
        }

        if (_settleFromMenu)
        {
            // Detect new game vs loaded save by checking the Main Ring.
            // New game: ring has only anchor + Pistols (count = 2).
            // Loaded save: ring has additional items from the save.
            short ringCount = _memory.ReadInt16(dllBase + map.MainRingCount);

            if (ringCount <= 2 && _allReceivedRingItems.Count > 0)
            {
                // New game — replay all ring items (no snapshot exists for fresh games)
                _pendingItems.Clear();
                foreach (var item in _allReceivedRingItems)
                    _pendingItems.Enqueue(item);
                ConsoleUI.Info($"[GSW] New game — replaying {_allReceivedRingItems.Count} ring items");
                return;
            }
        }

        // Reconcile from the current save's snapshot (works for menu→game,
        // level→level transitions, and cross-level loads).
        if (_stateStore.HasSnapshot(_lastSaveNumber))
        {
            _activeSaveNumber = _lastSaveNumber;
            var snapshot = _stateStore.GetSnapshot(_lastSaveNumber);
            if (snapshot != null)
                ReconcileAfterLoad(snapshot, levelId);
        }
        else if (_lastSaveNumber >= 0)
        {
            // The game saved during the level transition (e.g. typewriter save
            // prompt between levels). The client didn't see HandleSaveNumberChange
            // because it happened during settle. Create a baseline snapshot so
            // reloading this save later has something to reconcile from.
            OnGameSaved(_lastSaveNumber, levelId);
            _activeSaveNumber = _lastSaveNumber;
            ConsoleUI.Info($"[SAVE] Created baseline snapshot for save #{_lastSaveNumber}");
        }
    }

    /// <summary>
    /// Takes a snapshot of all tracked entity flags for the current level.
    /// </summary>
    private void SnapshotEntityFlags(int levelId)
    {
        _entityFlags.Clear();

        int mapperIdx = _context.Map.ToLocationMapperIndex(levelId);
        int elKey = EntityLocationKey(mapperIdx);
        if (mapperIdx < 0 || !_levelEntityLocations.ContainsKey(elKey))
            return;

        // Resolve entities array
        var map = _context.Map;
        _entitiesBase = _memory.ReadPointer(_context.DllBase, map.EntitiesPointer);
        if (_entitiesBase == IntPtr.Zero)
            return;

        var entityLocations = _levelEntityLocations[elKey];
        foreach (int entityIndex in entityLocations.Keys)
        {
            IntPtr entityAddr = _entitiesBase + (entityIndex * map.EntitySize);
            short flags = _memory.ReadInt16(entityAddr + map.Item_Flags);
            _entityFlags[entityIndex] = flags;
        }
    }

    /// <summary>
    /// Checks each tracked entity for flag changes (pickup detection).
    /// Uses the active save snapshot to distinguish real pickups from reload artifacts.
    /// </summary>
    private void CheckEntityPickups(int levelId)
    {
        int mapperIdx = _context.Map.ToLocationMapperIndex(levelId);
        int elKey = EntityLocationKey(mapperIdx);
        if (mapperIdx < 0 || !_levelEntityLocations.ContainsKey(elKey))
            return;

        if (_entitiesBase == IntPtr.Zero)
        {
            _entitiesBase = _memory.ReadPointer(_context.DllBase, _context.Map.EntitiesPointer);
            if (_entitiesBase == IntPtr.Zero) return;
        }

        // Get the active snapshot for save-aware checking
        var activeSnapshot = _stateStore.GetSnapshot(_activeSaveNumber);

        var entityLocations = _levelEntityLocations[elKey];
        foreach (var (entityIndex, locationId) in entityLocations)
        {
            IntPtr entityAddr = _entitiesBase + (entityIndex * _context.Map.EntitySize);
            short currentFlags = _memory.ReadInt16(entityAddr + _context.Map.Item_Flags);

            if (_entityFlags.TryGetValue(entityIndex, out short previousFlags))
            {
                if (currentFlags != previousFlags)
                {
                    _entityFlags[entityIndex] = currentFlags;

                    // If this location was already checked in the active snapshot,
                    // this is a reload artifact — skip both the AP send and sentinel removal.
                    if (activeSnapshot != null && activeSnapshot.CheckedEntityLocations.Contains(locationId))
                        continue;

                    bool isNew = _session.SendLocationCheck(locationId);
                    if (isNew)
                    {
                        string locName = _session.GetLocationName(locationId);
                        ConsoleUI.ItemSent(locName, "Archipelago");
                    }

                    // Update the active snapshot with the new check
                    activeSnapshot?.CheckedEntityLocations.Add(locationId);

                    // Queue sentinel removal for real pickups.
                    // Suppressed during level completion (end-of-level flag cleanup).
                    if (_lastLevelCompleted != 1)
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
        int mapperIdx = _context.Map.ToLocationMapperIndex(levelId);
        if (mapperIdx < 0) return;

        ReadSecretsState();
    }

    private void ReadSecretsState()
    {
        var map = _context.Map;
        ushort secrets = map.ReadSecrets(_memory, _context.DllBase);

        if (secrets != _lastSecretsFound)
        {
            var newSecrets = map.DetectNewSecrets(_lastSecretsFound, secrets);
            int mapperIdx = map.ToLocationMapperIndex(_lastLevelId);

            foreach (int s in newSecrets)
            {
                if (mapperIdx >= 0)
                {
                    long secretLocId = _locationMapper.GetSecretLocationId(mapperIdx, s);
                    _session.SendLocationCheck(secretLocId);

                    string levelName = map.LevelNames.GetValueOrDefault(_lastLevelId, "Unknown");
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
        int completed = _memory.ReadInt32(_context.DllBase, _context.Map.LevelCompleted);

        if (completed == 1 && _lastLevelCompleted != 1)
        {
            int mapperIdx = _context.Map.ToLocationMapperIndex(levelId);
            if (mapperIdx >= 0 && !_completedLevels.Contains(levelId))
            {
                long locId = _locationMapper.GetLevelCompleteId(mapperIdx);
                _session.SendLocationCheck(locId);
                _completedLevels.Add(levelId);

                string levelName = _context.Map.LevelNames.GetValueOrDefault(levelId, $"Level {levelId}");
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
        short health = _context.Map.ReadHealth(_memory, _context.DllBase);

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
    /// Detects key items consumed during normal gameplay (used in a door/lock).
    /// When a key disappears from the Keys Ring without a save/load event,
    /// it was used. We mark it as used in all snapshots to prevent re-injection.
    /// </summary>
    private void CheckKeyItemUsage(int levelId)
    {
        int mapperIdx = _context.Map.ToLocationMapperIndex(levelId);
        if (mapperIdx < 0) return;

        var usedKeys = _keyMonitor.DetectUsedKeys();
        foreach (var (pointer, qtyLost) in usedKeys)
        {
            long apItemId = _inventory.ResolvePointerToApId(pointer, mapperIdx);
            if (apItemId != 0)
            {
                // Mark as used in LIVE state only (once per unit lost).
                // Snapshots are not modified — they stay frozen at save-time state.
                // The next OnGameSaved will capture the live used keys into a new snapshot.
                for (int i = 0; i < qtyLost; i++)
                    _inventory.AddUsedKeyItem(apItemId);

                string itemName = _session.GetItemName(apItemId);
                ConsoleUI.Info($"[SAVE] Key item used: {itemName} (qty={qtyLost})");
            }
        }
    }

    /// <summary>
    /// Drains the AP item queue into _allReceivedRingItems without injecting.
    /// Called from OnSettleComplete so that ReconcileAfterLoad has the full
    /// item history before deciding what to inject.
    /// </summary>
    private void DrainReceivedItemQueue(int levelId)
    {
        int drained = 0;
        while (_session.TryDequeueReceivedItem(out var item))
        {
            string itemName = _session.GetItemName(item.ItemId);
            string playerName = _session.GetPlayerName(item.Player);
            ConsoleUI.ItemReceived(itemName, playerName);
            _itemsReceivedIndex++;

            // Check all registered games for category (cross-game items are common)
            var mapper = _context.GetItemMapperForId(item.ItemId) ?? _itemMapper;
            var category = mapper.GetCategory(item.ItemId);

            if (category == ItemCategory.KeyItem)
            {
                // Store key items so EnsureKeyItemsInRing can inject them later.
                // GiveKeyItem only needs memory for immediate injection (same level);
                // cross-level items are just stored in _receivedKeyItems.
                _inventory.GiveKeyItem(item.ItemId, levelId);
            }
            else if (category != ItemCategory.Trap)
            {
                _allReceivedRingItems.Add((item.ItemId, category));
            }

            drained++;
        }

        if (drained > 0)
            ConsoleUI.Info($"[GSW] Drained {drained} items from AP queue ({_allReceivedRingItems.Count} ring items tracked)");
    }

    /// <summary>
    /// Processes items received from other AP players and injects them in real-time.
    /// Items that need ring injection (weapons, ammo, medipacks) are deferred to
    /// _pendingItems if the inventory isn't ready yet. Traps and key items are
    /// processed immediately (they don't need the Compass pointer).
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

            // Check all registered games for category (cross-game items are common)
            var mapper = _context.GetItemMapperForId(item.ItemId) ?? _itemMapper;
            var category = mapper.GetCategory(item.ItemId);

            // Traps and key items don't need the ring — process immediately
            if (category == ItemCategory.Trap)
            {
                _inventory.ApplyTrap(item.ItemId);
                continue;
            }
            if (category == ItemCategory.KeyItem)
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
                case ItemCategory.Weapon:
                    _inventory.GiveWeapon(itemId);
                    break;
                case ItemCategory.Ammo:
                    _inventory.GiveAmmo(itemId);
                    break;
                case ItemCategory.Medipack:
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
            0 => _completedLevels.Count >= _session.SlotData.TotalLevels, // all_levels
            1 => _completedLevels.Count >= _session.SlotData.LevelsForGoal, // n_levels
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
