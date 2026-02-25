using TRArchipelagoClient.Core;
using TRArchipelagoClient.GameInterface;
using TRArchipelagoClient.Patching;
using TRArchipelagoClient.UI;

namespace TRArchipelagoClient;

class Program
{
    static async Task Main(string[] args)
    {
        Console.Title = "TR1 Remastered - Archipelago Client";
        ConsoleUI.PrintBanner();

        if (args.Contains("--test"))
        {
            await RunTestMode();
            return;
        }

        if (args.Contains("--find-secrets"))
        {
            await FindSecretsAddress();
            return;
        }

        if (args.Contains("--find-inventory"))
        {
            await FindInventoryAddress();
            return;
        }

        if (args.Contains("--write-test"))
        {
            await WriteTest();
            return;
        }

        if (args.Contains("--quick-write"))
        {
            await QuickWrite();
            return;
        }

        if (args.Contains("--probe"))
        {
            await ProbePointers();
            return;
        }

        // --- Normal AP mode ---
        string server = GetArg(args, 0, "localhost:38281");
        string? slotName = GetArg(args, 1, null) ?? ConsoleUI.Prompt("Slot name");
        string password = GetArg(args, 2, "") ?? "";

        string? gameDir = GetArg(args, 3, null) ?? ConsoleUI.Prompt("TR1-3 Remastered game directory");
        if (!Directory.Exists(gameDir))
        {
            ConsoleUI.Error($"Game directory not found: {gameDir}");
            return;
        }

        ConsoleUI.Info($"Connecting to {server} as {slotName}...");

        var session = new APSession();
        var memory = new ProcessMemory();
        var cts = new CancellationTokenSource();
        LevelPatcher? patcher = null;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            bool connected = await session.ConnectAsync(server ?? "localhost:38281", slotName ?? "", password);
            if (!connected)
            {
                ConsoleUI.Error("Failed to connect to Archipelago server.");
                return;
            }

            ConsoleUI.Success($"Connected! Game: {session.SlotData?.Game ?? "unknown"}");

            ConsoleUI.Info("Backing up and patching level files...");
            patcher = new LevelPatcher(gameDir, session);
            patcher.PatchAll();

            ConsoleUI.Success("Level files patched (pickups replaced with sentinels).");

            var entityLocations = patcher.GetAllMappingsByLevelIndex();
            ConsoleUI.Info($"Tracking {entityLocations.Values.Sum(m => m.Count)} pickup locations across {entityLocations.Count} levels.");

            var watcher = new GameStateWatcher(session, memory, entityLocations);

            ConsoleUI.Info("Waiting for game to launch...");
            ConsoleUI.Info("Start tomb123.exe and begin playing TR1!\n");

            await watcher.WaitForGameAsync(cts.Token);
            await watcher.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            ConsoleUI.Info("\nShutting down...");
        }
        catch (Exception ex)
        {
            ConsoleUI.Error($"Error: {ex.Message}");
        }
        finally
        {
            if (patcher?.BackupManager.HasBackups() == true &&
                ConsoleUI.Confirm("Restore original level files?"))
            {
                patcher.BackupManager.RestoreAll();
                ConsoleUI.Success("Original files restored.");
            }

            memory.Dispose();
            session.Disconnect();
        }
    }

    /// <summary>
    /// Standalone test mode: monitors game memory without AP connection.
    /// Detects pickups, level changes, health, secrets in real-time.
    /// </summary>
    static async Task RunTestMode()
    {
        ConsoleUI.Info("=== TEST MODE (no AP server needed) ===\n");

        var memory = new ProcessMemory();
        var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            // Wait for game
            ConsoleUI.Info("Waiting for tomb123.exe...");
            while (!cts.IsCancellationRequested)
            {
                if (memory.TryAttach())
                {
                    if (memory.Tomb1Base != IntPtr.Zero)
                    {
                        ConsoleUI.Success($"Attached! tomb1.dll at 0x{memory.Tomb1Base:X}");
                        break;
                    }
                    ConsoleUI.Info("tomb123.exe found, waiting for tomb1.dll...");
                }
                await Task.Delay(1000, cts.Token);
                if (memory.IsAttached && memory.Tomb1Base == IntPtr.Zero)
                    memory.RefreshTomb1Base();
            }

            if (cts.IsCancellationRequested) return;

            IntPtr tomb1Base = memory.Tomb1Base;

            // State tracking
            int lastLevelId = -1;
            short lastHealth = -1;
            bool healthInitialized = false;
            bool wasInGame = false;
            int lastLevelCompleted = 0;
            ushort lastSecrets = 0;
            var entityFlags = new Dictionary<int, short>();
            var entityInitialHp = new Dictionary<int, short>(); // to classify entity type

            ConsoleUI.Success("Monitoring active! Pick up items, change levels, etc.\n");

            while (!cts.IsCancellationRequested && memory.IsAttached)
            {
                int inGameScene = memory.ReadInt32(tomb1Base, TR1RMemoryMap.IsInGameScene);
                bool isInGame = inGameScene > 0;

                if (!isInGame)
                {
                    if (wasInGame)
                    {
                        ConsoleUI.Info("Paused (menu/inventory/loading)");
                        wasInGame = false;
                    }
                    await Task.Delay(100, cts.Token);
                    continue;
                }

                int levelId = memory.ReadInt32(tomb1Base, TR1RMemoryMap.LevelId);

                // Skip home/menu
                if (levelId == TR1RMemoryMap.Level_Home || levelId == TR1RMemoryMap.Level_MainMenu)
                {
                    await Task.Delay(100, cts.Token);
                    continue;
                }

                IntPtr laraPtr = memory.ReadPointer(tomb1Base, TR1RMemoryMap.LaraBase);
                if (laraPtr == IntPtr.Zero)
                {
                    await Task.Delay(100, cts.Token);
                    continue;
                }

                // Level change — reset entity tracking, secrets
                if (levelId != lastLevelId)
                {
                    string name = TR1RMemoryMap.LevelNames.GetValueOrDefault(levelId, $"Level {levelId}");
                    ConsoleUI.LevelChange(name);
                    entityFlags.Clear();
                    entityInitialHp.Clear();
                    lastLevelId = levelId;
                    lastLevelCompleted = 0;
                    healthInitialized = false;

                    // Read current secrets to avoid re-detecting already found ones
                    lastSecrets = memory.ReadUInt16(
                        tomb1Base + TR1RMemoryMap.WorldStateBackup + TR1RMemoryMap.Runtime_SecretsFound);
                    if (lastSecrets != 0)
                        ConsoleUI.Info($"Secrets already found: 0x{lastSecrets:X4} ({CountBits(lastSecrets)} secrets)");

                    // Snapshot all entity flags + HP
                    IntPtr entBase = memory.ReadPointer(tomb1Base, TR1RMemoryMap.EntitiesPointer);
                    int entCount = memory.ReadInt32(tomb1Base, TR1RMemoryMap.EntitiesCount);
                    if (entBase != IntPtr.Zero && entCount > 0)
                    {
                        ConsoleUI.Info($"Entities: {entCount}");
                        for (int i = 0; i < entCount; i++)
                        {
                            IntPtr entAddr = entBase + (i * TR1RMemoryMap.EntitySize);
                            short flags = memory.ReadInt16(entAddr + TR1RMemoryMap.Item_Flags);
                            short hp = memory.ReadInt16(entAddr + TR1RMemoryMap.Item_HitPoints);
                            entityFlags[i] = flags;
                            entityInitialHp[i] = hp;
                        }
                    }
                }

                // First frame back in gameplay — only init health on level start, not menu return
                if (!wasInGame)
                {
                    wasInGame = true;
                    if (!healthInitialized)
                    {
                        lastHealth = memory.ReadInt16(laraPtr + TR1RMemoryMap.Item_HitPoints);
                        healthInitialized = true;
                    }
                }

                // --- Entity flag changes (pickup + enemy detection) ---
                IntPtr entitiesBase = memory.ReadPointer(tomb1Base, TR1RMemoryMap.EntitiesPointer);
                if (entitiesBase != IntPtr.Zero)
                {
                    int entCount = memory.ReadInt32(tomb1Base, TR1RMemoryMap.EntitiesCount);
                    for (int i = 1; i < entCount; i++) // skip 0 (Lara)
                    {
                        IntPtr entAddr = entitiesBase + (i * TR1RMemoryMap.EntitySize);
                        short flags = memory.ReadInt16(entAddr + TR1RMemoryMap.Item_Flags);

                        if (entityFlags.TryGetValue(i, out short prevFlags) && flags != prevFlags)
                        {
                            short initHp = entityInitialHp.GetValueOrDefault(i, (short)0);
                            bool isPickup = initHp < 0; // pickups have negative/garbage HP

                            if (isPickup)
                            {
                                // Only log the first change (the actual pickup moment)
                                if (prevFlags == entityFlags.GetValueOrDefault(i, (short)0)
                                    || prevFlags == entityInitialHp.GetValueOrDefault(i, (short)0))
                                {
                                    ConsoleUI.Success($"PICKUP [#{i}] Flags 0x{prevFlags:X4} -> 0x{flags:X4}");
                                }
                            }
                            else if (initHp > 0)
                            {
                                short currentHp = memory.ReadInt16(entAddr + TR1RMemoryMap.Item_HitPoints);
                                if (currentHp <= 0 && initHp > 0)
                                {
                                    // Only log death, not every animation frame
                                    ConsoleUI.Info($"ENEMY KILLED [#{i}] HP: {initHp} -> {currentHp}");
                                    entityInitialHp[i] = currentHp; // don't re-log
                                }
                            }

                            entityFlags[i] = flags;
                        }
                    }
                }

                // --- Health tracking (persists across menu!) ---
                short health = memory.ReadInt16(laraPtr + TR1RMemoryMap.Item_HitPoints);
                if (health != lastHealth && lastHealth >= 0)
                {
                    int diff = health - lastHealth;
                    if (health <= 0 && lastHealth > 0)
                        ConsoleUI.Warning($"DEATH! HP: {lastHealth} -> {health}");
                    else if (diff > 0)
                        ConsoleUI.Success($"HP: {lastHealth} -> {health} (+{diff} healed)");
                    else if (diff < -20)
                        ConsoleUI.Warning($"HP: {lastHealth} -> {health} ({diff} damage)");
                    lastHealth = health;
                }
                else
                {
                    lastHealth = health;
                }

                // --- Secrets (from WorldStateBackup — testing if it updates live) ---
                ushort secrets = memory.ReadUInt16(
                    tomb1Base + TR1RMemoryMap.WorldStateBackup + TR1RMemoryMap.Runtime_SecretsFound);
                if (secrets != lastSecrets)
                {
                    ushort newBits = (ushort)(secrets & ~lastSecrets);
                    for (int s = 0; s < 16; s++)
                    {
                        if ((newBits & (1 << s)) != 0)
                        {
                            string levelName = TR1RMemoryMap.LevelNames.GetValueOrDefault(levelId, "Unknown");
                            ConsoleUI.SecretFound(s + 1, levelName);
                        }
                    }
                    lastSecrets = secrets;
                }

                // --- Level completion ---
                int levelCompleted = memory.ReadInt32(tomb1Base, TR1RMemoryMap.LevelCompleted);
                if (levelCompleted == 1 && lastLevelCompleted != 1)
                {
                    string name = TR1RMemoryMap.LevelNames.GetValueOrDefault(levelId, $"Level {levelId}");
                    ConsoleUI.Success($"LEVEL COMPLETED: {name}");
                }
                lastLevelCompleted = levelCompleted;

                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            ConsoleUI.Info("\nTest mode ended.");
        }
        finally
        {
            memory.Dispose();
        }
    }

    /// <summary>
    /// Scans tomb1.dll memory to find the runtime address of the secrets bitmask.
    /// Takes two snapshots (before/after finding a secret) and shows addresses that changed
    /// in a bitmask pattern (new bit set).
    /// </summary>
    static async Task FindSecretsAddress()
    {
        ConsoleUI.Info("=== FIND SECRETS ADDRESS ===\n");
        ConsoleUI.Info("This will scan tomb1.dll memory to find where secrets are stored.");
        ConsoleUI.Info("You need to find a NEW secret between the two snapshots.\n");

        var memory = new ProcessMemory();

        ConsoleUI.Info("Waiting for tomb123.exe...");
        while (true)
        {
            if (memory.TryAttach() && memory.Tomb1Base != IntPtr.Zero)
            {
                ConsoleUI.Success($"Attached! tomb1.dll at 0x{memory.Tomb1Base:X}");
                break;
            }
            await Task.Delay(1000);
            if (memory.IsAttached)
                memory.RefreshTomb1Base();
        }

        IntPtr tomb1Base = memory.Tomb1Base;

        // Scan ranges covering known game state areas
        // Range: 0xe0000 to 0x500000 (~4.2 MB)
        int scanStart = 0xe0000;
        int scanSize = 0x500000 - scanStart;

        ConsoleUI.Info($"Scan range: tomb1.dll+0x{scanStart:X} to tomb1.dll+0x{scanStart + scanSize:X} ({scanSize / 1024} KB)");

        // Also show current WorldStateBackup value for reference
        ushort wsbSecrets = memory.ReadUInt16(tomb1Base + TR1RMemoryMap.WorldStateBackup + TR1RMemoryMap.Runtime_SecretsFound);
        ConsoleUI.Info($"WorldStateBackup secrets (for reference): 0x{wsbSecrets:X4} ({CountBits(wsbSecrets)} bits set)");

        Console.WriteLine();
        ConsoleUI.Warning("Step 1: Go into a level and stand NEAR a secret you haven't found yet.");
        ConsoleUI.Warning("         Do NOT pick it up yet. Press ENTER to take snapshot 1...");
        Console.ReadLine();

        ConsoleUI.Info("Taking snapshot 1...");
        byte[] snapshot1 = memory.ReadBytes(tomb1Base + scanStart, scanSize);
        ConsoleUI.Success($"Snapshot 1 taken ({snapshot1.Length} bytes)");

        Console.WriteLine();
        ConsoleUI.Warning("Step 2: Now go find/collect the secret. Press ENTER after the secret chime plays...");
        Console.ReadLine();

        ConsoleUI.Info("Taking snapshot 2...");
        byte[] snapshot2 = memory.ReadBytes(tomb1Base + scanStart, scanSize);
        ConsoleUI.Success($"Snapshot 2 taken ({snapshot2.Length} bytes)");

        // Compare: find UInt16 values where exactly one new bit was set
        Console.WriteLine();
        ConsoleUI.Info("Searching for bitmask changes (new bit set)...\n");

        int bitmaskMatches = 0;
        int totalChanges = 0;

        for (int i = 0; i <= snapshot1.Length - 2; i += 2) // step by 2 for UInt16 alignment
        {
            ushort val1 = BitConverter.ToUInt16(snapshot1, i);
            ushort val2 = BitConverter.ToUInt16(snapshot2, i);

            if (val1 == val2) continue;
            totalChanges++;

            // Check if exactly one new bit was added: val2 = val1 | (1 << N)
            ushort diff = (ushort)(val2 & ~val1); // new bits
            ushort lost = (ushort)(val1 & ~val2); // lost bits

            if (lost == 0 && diff != 0 && (diff & (diff - 1)) == 0) // exactly one new bit, no bits lost
            {
                int bitIndex = 0;
                ushort tmp = diff;
                while (tmp > 1) { tmp >>= 1; bitIndex++; }

                int offset = scanStart + i;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  BITMASK MATCH: tomb1.dll+0x{offset:X} = 0x{val1:X4} -> 0x{val2:X4} (bit {bitIndex} set)");
                Console.ResetColor();
                bitmaskMatches++;
            }
        }

        Console.WriteLine();
        ConsoleUI.Info($"Total UInt16 changes: {totalChanges}");
        ConsoleUI.Info($"Bitmask pattern matches: {bitmaskMatches}");

        if (bitmaskMatches == 0)
        {
            ConsoleUI.Warning("No bitmask matches found. Try again, or the secret detection may need a different approach.");
        }
        else if (bitmaskMatches <= 5)
        {
            ConsoleUI.Success("Few matches found! One of these is likely the secrets address.");
            ConsoleUI.Info("Add the offset to TR1RMemoryMap.cs as 'RuntimeSecretsFound'.");
        }
        else
        {
            ConsoleUI.Info("Many matches. Try narrowing down by repeating with a different secret.");
        }

        memory.Dispose();
    }

    /// <summary>
    /// Scans tomb1.dll static data + LARA_INFO area before/after the "all weapons" cheat.
    /// Based on TRX source: ammo is stored as int32 fields in LARA_INFO (static in tomb1.dll).
    /// Also scans for INV_RING_SOURCE (the ring arrays holding medipack/weapon quantities).
    /// </summary>
    static async Task FindInventoryAddress()
    {
        ConsoleUI.Info("=== FIND INVENTORY (TRX-informed scan) ===\n");
        ConsoleUI.Info("Based on TRX source code:");
        ConsoleUI.Info("  - Ammo = int32 fields in LARA_INFO (static in tomb1.dll)");
        ConsoleUI.Info("  - Medipacks/weapons = int32 qtys in INV_RING_SOURCE[4] (also static)");
        ConsoleUI.Info("  - Cheat sets: shotgun=300, magnums=1000, uzis=2000 (TRX values)");
        ConsoleUI.Info("Cheat: step forward > step back > 3x turn CCW > backflip\n");

        var memory = new ProcessMemory();

        ConsoleUI.Info("Waiting for tomb123.exe...");
        while (true)
        {
            if (memory.TryAttach() && memory.Tomb1Base != IntPtr.Zero)
            {
                ConsoleUI.Success($"Attached! tomb1.dll at 0x{memory.Tomb1Base:X}");
                break;
            }
            await Task.Delay(1000);
            if (memory.IsAttached)
                memory.RefreshTomb1Base();
        }

        IntPtr tomb1Base = memory.Tomb1Base;

        // We scan a large range of tomb1.dll static data (covers LARA_INFO and global vars)
        // Known LARA_INFO starts around 0x310E80, but rings could be anywhere
        int scanStart = 0x100000;
        int scanSize = 0x4D0000 - scanStart; // up to end of WSB area
        ConsoleUI.Info($"Scan range: tomb1.dll+0x{scanStart:X} to tomb1.dll+0x{scanStart + scanSize:X} ({scanSize / 1024} KB)");

        // Also show known LARA_INFO pointers
        IntPtr laraPtr = memory.ReadPointer(tomb1Base + TR1RMemoryMap.LaraBase);
        ConsoleUI.Info($"LaraBase ptr -> 0x{(long)laraPtr:X} (ITEM struct on heap)");
        ConsoleUI.Info($"Known LARA_INFO area: tomb1.dll+0x310E80\n");

        // --- Snapshot 1: BEFORE cheat ---
        ConsoleUI.Warning("Step 1: Be in a level with NO extra weapons (start of game ideally).");
        ConsoleUI.Warning("         Do NOT use the cheat yet. Press ENTER...");
        Console.ReadLine();

        ConsoleUI.Info("Taking snapshot of tomb1.dll...");
        byte[] snap1 = memory.ReadBytes(tomb1Base + scanStart, scanSize);
        ConsoleUI.Success($"Snapshot 1: {snap1.Length} bytes");

        // Also snapshot heap around Lara ITEM (in case LARA_INFO is on heap too)
        byte[]? heapSnap1 = null;
        int heapScanSize = 0x2000;
        if (laraPtr != IntPtr.Zero)
        {
            heapSnap1 = memory.ReadBytes(laraPtr, heapScanSize);
            ConsoleUI.Info($"Also snapshotting {heapScanSize} bytes from Lara ITEM at 0x{(long)laraPtr:X}");
        }

        // --- Snapshot 2: AFTER cheat ---
        Console.WriteLine();
        ConsoleUI.Warning("Step 2: Do the cheat (fwd > back > 3x turn CCW > backflip).");
        ConsoleUI.Warning("         Open inventory to confirm you got all weapons. Press ENTER...");
        Console.ReadLine();

        ConsoleUI.Info("Taking snapshot 2...");
        byte[] snap2 = memory.ReadBytes(tomb1Base + scanStart, scanSize);

        byte[]? heapSnap2 = null;
        if (laraPtr != IntPtr.Zero)
            heapSnap2 = memory.ReadBytes(laraPtr, heapScanSize);

        // =================================================================
        // SCAN 1: Find int32 changes in tomb1.dll (ammo fields in LARA_INFO)
        // =================================================================
        ConsoleUI.Info("\n=== Scanning tomb1.dll for int32 changes (ammo) ===\n");

        using var log = new StreamWriter("scan_results.txt");
        log.WriteLine("=== TRX-informed inventory scan ===");
        log.WriteLine($"tomb1.dll base: 0x{(long)tomb1Base:X}");
        log.WriteLine($"Scan range: +0x{scanStart:X} to +0x{scanStart + scanSize:X}\n");

        var int32Changes = new List<(int offset, int before, int after)>();

        for (int i = 0; i <= snap1.Length - 4; i += 4) // int32 aligned
        {
            int v1 = BitConverter.ToInt32(snap1, i);
            int v2 = BitConverter.ToInt32(snap2, i);

            if (v1 != v2)
                int32Changes.Add((scanStart + i, v1, v2));
        }

        ConsoleUI.Info($"Total int32 changes: {int32Changes.Count}");

        // Filter: values that went from 0 to a "likely ammo" range
        // TRX cheat values: shotgun=300, magnums=1000, uzis=2000
        // TR1R might differ, so look for 0 -> [50, 20000]
        var ammoLikely = int32Changes
            .Where(c => c.before == 0 && c.after >= 50 && c.after <= 20000)
            .ToList();

        ConsoleUI.Info($"Int32: 0 -> [50,20000] (likely ammo): {ammoLikely.Count} hits\n");
        log.WriteLine($"=== Int32 changes: 0 -> [50,20000] ({ammoLikely.Count} hits) ===");

        foreach (var (off, before, after) in ammoLikely)
        {
            string known = DescribeKnownOffset(off);
            string line = $"  tomb1.dll+0x{off:X}: {before} -> {after}{known}";
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(line);
            Console.ResetColor();
            log.WriteLine(line);
        }

        // Also show 0 -> [1,50] (medipack counts, weapon insertion counts)
        var smallChanges = int32Changes
            .Where(c => c.before == 0 && c.after >= 1 && c.after <= 50)
            .ToList();

        Console.WriteLine();
        ConsoleUI.Info($"Int32: 0 -> [1,50] (likely medipack/ring counts): {smallChanges.Count} hits\n");
        log.WriteLine($"\n=== Int32 changes: 0 -> [1,50] ({smallChanges.Count} hits) ===");

        foreach (var (off, before, after) in smallChanges)
        {
            string known = DescribeKnownOffset(off);
            string line = $"  tomb1.dll+0x{off:X}: {before} -> {after}{known}";
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(line);
            Console.ResetColor();
            log.WriteLine(line);
        }

        // Also show ALL int32 changes near LARA_INFO (0x310E00 - 0x311200)
        var laraAreaChanges = int32Changes
            .Where(c => c.offset >= 0x310E00 && c.offset <= 0x311200)
            .ToList();

        Console.WriteLine();
        ConsoleUI.Info($"ALL int32 changes in LARA_INFO area (0x310E00-0x311200): {laraAreaChanges.Count}\n");
        log.WriteLine($"\n=== ALL int32 changes in LARA_INFO area ({laraAreaChanges.Count}) ===");

        foreach (var (off, before, after) in laraAreaChanges)
        {
            string known = DescribeKnownOffset(off);
            string line = $"  tomb1.dll+0x{off:X}: {before} -> {after}{known}";
            Console.WriteLine(line);
            log.WriteLine(line);
        }

        // =================================================================
        // SCAN 2: Look for ALL changes in a wider range around LARA_INFO
        // The TRX LARA_INFO is large (~1KB+), ammo fields come after arms/meshes
        // Scan 0x310000 - 0x312000 for any value changes
        // =================================================================
        Console.WriteLine();
        ConsoleUI.Info("=== All changes in extended LARA_INFO range (0x310000-0x312000) ===\n");
        log.WriteLine($"\n=== All changes 0x310000-0x312000 ===");

        int laraRangeStart = 0x310000 - scanStart;
        int laraRangeEnd = 0x312000 - scanStart;

        for (int i = laraRangeStart; i < laraRangeEnd && i <= snap1.Length - 4; i += 4)
        {
            int v1 = BitConverter.ToInt32(snap1, i);
            int v2 = BitConverter.ToInt32(snap2, i);

            if (v1 != v2)
            {
                int off = scanStart + i;
                string known = DescribeKnownOffset(off);
                string line = $"  tomb1.dll+0x{off:X}: {v1} -> {v2} (0x{v1:X} -> 0x{v2:X}){known}";
                Console.WriteLine(line);
                log.WriteLine(line);
            }
        }

        // =================================================================
        // SCAN 3: Find INV_RING_SOURCE
        // TRX structure: { int16 current, int16 count, int32 qtys[24], ptr items[24] }
        // After cheat, main ring should have count >= 4 (pistol+shotgun+magnums+uzis)
        // The qtys would be 1 for weapons, and medipack counts
        // Look for int16 going from small to 4+ (the count field)
        // =================================================================
        Console.WriteLine();
        ConsoleUI.Info("=== Searching for INV_RING_SOURCE (ring count changes) ===\n");
        log.WriteLine($"\n=== INV_RING_SOURCE search ===");

        // The ring count is int16, should go from 1-2 (pistols+maybe compass) to 4+ after cheat
        var ringCountCandidates = new List<(int offset, short before, short after)>();

        for (int i = 0; i <= snap1.Length - 2; i += 2)
        {
            short v1 = BitConverter.ToInt16(snap1, i);
            short v2 = BitConverter.ToInt16(snap2, i);

            // Count went up by ~3 (added shotgun, magnums, uzis)
            if (v1 >= 1 && v1 <= 5 && v2 >= 4 && v2 <= 10 && v2 > v1 && (v2 - v1) >= 2)
            {
                int off = scanStart + i;
                // Check if the next 24 int32s look like quantities (small positive values)
                if (i + 4 + 24 * 4 < snap2.Length)
                {
                    bool looksLikeRing = true;
                    int nonZeroQtys = 0;
                    var qtys = new List<int>();
                    for (int q = 0; q < v2 && q < 24; q++)
                    {
                        int qty = BitConverter.ToInt32(snap2, i + 4 + q * 4);
                        qtys.Add(qty);
                        if (qty < 0 || qty > 100000) { looksLikeRing = false; break; }
                        if (qty > 0) nonZeroQtys++;
                    }

                    if (looksLikeRing && nonZeroQtys >= v2 - 1) // most slots should have qty > 0
                    {
                        string qtyStr = string.Join(", ", qtys);
                        string line = $"  RING? tomb1.dll+0x{off:X}: count {v1} -> {v2}, qtys=[{qtyStr}]";
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine(line);
                        Console.ResetColor();
                        log.WriteLine(line);
                        ringCountCandidates.Add((off, v1, v2));
                    }
                }
            }
        }

        if (ringCountCandidates.Count == 0)
            ConsoleUI.Info("No ring candidates found with int16 count + int32 qtys layout.");

        // =================================================================
        // SCAN 4: Heap around Lara ITEM (if LARA_INFO is after ITEM on heap)
        // =================================================================
        if (heapSnap1 != null && heapSnap2 != null)
        {
            Console.WriteLine();
            ConsoleUI.Info("=== Heap: int32 changes around Lara ITEM ===\n");
            log.WriteLine($"\n=== Heap changes around Lara ITEM (0x{(long)laraPtr:X}) ===");

            for (int i = 0; i <= heapSnap1.Length - 4; i += 4)
            {
                int v1 = BitConverter.ToInt32(heapSnap1, i);
                int v2 = BitConverter.ToInt32(heapSnap2, i);

                if (v1 != v2 && v2 >= 50 && v2 <= 20000 && v1 == 0)
                {
                    string line = $"  Lara+0x{i:X}: {v1} -> {v2} (0x{(long)laraPtr + i:X})";
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(line);
                    Console.ResetColor();
                    log.WriteLine(line);
                }
            }

            // Also show all changes in typical ITEM struct + beyond
            int heapChanges = 0;
            for (int i = 0; i <= heapSnap1.Length - 4; i += 4)
            {
                int v1 = BitConverter.ToInt32(heapSnap1, i);
                int v2 = BitConverter.ToInt32(heapSnap2, i);
                if (v1 != v2) heapChanges++;
            }
            ConsoleUI.Info($"Total int32 changes in Lara heap area: {heapChanges}");
        }

        // =================================================================
        // INTERACTIVE WRITE TEST
        // =================================================================
        Console.WriteLine();
        ConsoleUI.Info("=== Interactive write test ===");
        ConsoleUI.Info("Enter an offset (hex, relative to tomb1.dll) to write-test.");
        ConsoleUI.Info("We'll write a value and you check if inventory changed.\n");

        while (true)
        {
            ConsoleUI.Warning("Offset (hex, e.g. 310F00) or ENTER to quit:");
            string? input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) break;

            try
            {
                int offset = Convert.ToInt32(input, 16);
                IntPtr addr = tomb1Base + offset;
                int current = memory.ReadInt32(addr);
                ConsoleUI.Info($"Current value at tomb1.dll+0x{offset:X}: {current} (0x{current:X})");

                ConsoleUI.Warning("Value to write (decimal, e.g. 500):");
                string? valStr = Console.ReadLine()?.Trim();
                if (int.TryParse(valStr, out int writeVal))
                {
                    memory.Write(addr, writeVal);
                    int readBack = memory.ReadInt32(addr);
                    ConsoleUI.Info($"Wrote {writeVal}, read back {readBack}. Check inventory!");
                    ConsoleUI.Warning("Press ENTER to continue (value stays)...");
                    Console.ReadLine();
                }
            }
            catch (Exception ex)
            {
                ConsoleUI.Error($"Error: {ex.Message}");
            }
        }

        log.Flush();
        ConsoleUI.Success("Results saved to scan_results.txt");
        memory.Dispose();
    }

    /// <summary>
    /// Describes known offsets for easier identification in scan results.
    /// </summary>
    static string DescribeKnownOffset(int offset) => offset switch
    {
        0x310E80 => " [LaraId]",
        0x310E82 => " [LaraGunType]",
        0x310E96 => " [LaraOxygen]",
        0x310EC0 => " [LaraGunFlags]",
        0x310F70 => " [LaraAimingEnemy]",
        >= 0x4C4E00 and < 0x4C8600 => $" [WSB+0x{offset - 0x4C4E00:X}]",
        _ => "",
    };

    static bool FindRegionContaining(Dictionary<IntPtr, byte[]> snapshot, long targetAddr, out byte[] data)
    {
        foreach (var (baseAddr, regionData) in snapshot)
        {
            long rStart = (long)baseAddr;
            if (targetAddr >= rStart && targetAddr < rStart + regionData.Length)
            {
                data = regionData;
                return true;
            }
        }
        data = Array.Empty<byte>();
        return false;
    }

    /// <summary>
    /// Quick write test: scans for 0->1->2->1 candidates and writes 10 to each one.
    /// Press ENTER between each to check inventory.
    /// </summary>
    static async Task QuickWrite()
    {
        ConsoleUI.Info("=== QUICK WRITE: test candidates one by one ===\n");

        var memory = new ProcessMemory();

        ConsoleUI.Info("Waiting for tomb123.exe...");
        while (true)
        {
            if (memory.TryAttach() && memory.Tomb1Base != IntPtr.Zero)
            {
                ConsoleUI.Success($"Attached! tomb1.dll at 0x{memory.Tomb1Base:X}");
                break;
            }
            await Task.Delay(1000);
            if (memory.IsAttached)
                memory.RefreshTomb1Base();
        }

        // Hardcoded candidates from last --find-inventory run (0->1->2->1 pattern)
        long[] candidateAddrs = {
            0x1E4E76ADE04,
            0x1E4E76D2D74,
            0x1E4E76E1BA8,
            0x1E4E76FD524,
            0x1E4E76FECB0,
        };

        ConsoleUI.Info($"Testing {candidateAddrs.Length} candidates from last scan.");
        ConsoleUI.Info("For each one: we write 10, you check inventory, press y if it worked.\n");

        foreach (long addr in candidateAddrs)
        {
            IntPtr ptr = new IntPtr(addr);
            byte before = memory.ReadByte(ptr);

            // Try as byte first
            memory.Write(ptr, (byte)10);
            byte readBack = memory.ReadByte(ptr);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  >> 0x{addr:X}: wrote 10 (byte), was {before}, readback {readBack}");
            Console.ResetColor();
            ConsoleUI.Warning("     Open inventory — do you have 10 small medipacks? (y/n)");

            string? resp = Console.ReadLine()?.Trim().ToLower();
            if (resp == "y")
            {
                ConsoleUI.Success($"FOUND! Live small medipack counter at 0x{addr:X}");
                FindPointerChain(memory, memory.Tomb1Base, addr);
                memory.Dispose();
                return;
            }
            memory.Write(ptr, before); // restore

            // Try as int32
            int before32 = memory.ReadInt32(ptr);
            memory.Write(ptr, (int)10);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  >> 0x{addr:X}: wrote 10 (int32), was {before32}");
            Console.ResetColor();
            ConsoleUI.Warning("     Open inventory — do you have 10 small medipacks? (y/n)");

            resp = Console.ReadLine()?.Trim().ToLower();
            if (resp == "y")
            {
                ConsoleUI.Success($"FOUND! Live small medipack counter (Int32) at 0x{addr:X}");
                FindPointerChain(memory, memory.Tomb1Base, addr);
                memory.Dispose();
                return;
            }
            memory.Write(ptr, before32); // restore
        }

        ConsoleUI.Warning("None of the candidates worked. Addresses may have shifted (restart game?).\n");
        PrintKnownPointers(memory);
        memory.Dispose();
    }

    /// <summary>
    /// Probes known pointers and calculates offsets to a target heap address.
    /// Also scans nearby memory around known pointers for the medipack counter.
    /// </summary>
    static async Task ProbePointers()
    {
        ConsoleUI.Info("=== PROBE: known pointers + inventory search ===\n");

        var memory = new ProcessMemory();

        ConsoleUI.Info("Waiting for tomb123.exe...");
        while (true)
        {
            if (memory.TryAttach() && memory.Tomb1Base != IntPtr.Zero)
            {
                ConsoleUI.Success($"Attached! tomb1.dll at 0x{memory.Tomb1Base:X}");
                break;
            }
            await Task.Delay(1000);
            if (memory.IsAttached)
                memory.RefreshTomb1Base();
        }

        IntPtr t = memory.Tomb1Base;

        PrintKnownPointers(memory);

        // Read all known pointers
        IntPtr laraPtr = memory.ReadPointer(t + TR1RMemoryMap.LaraBase);
        IntPtr entPtr = memory.ReadPointer(t + TR1RMemoryMap.EntitiesPointer);
        IntPtr roomsPtr = memory.ReadPointer(t + TR1RMemoryMap.RoomsPointer);

        // Scan around each known pointer for medipack-like values
        // The user should have some known number of medipacks
        ConsoleUI.Warning("\nHow many small medipacks do you currently have in-game?");
        string? countStr = Console.ReadLine()?.Trim();
        if (!int.TryParse(countStr, out int expectedCount))
        {
            ConsoleUI.Error("Invalid number.");
            memory.Dispose();
            return;
        }

        // Scan various offsets from known pointers
        var pointers = new (string name, IntPtr ptr)[]
        {
            ("LaraBase", laraPtr),
            ("EntitiesPtr", entPtr),
            ("RoomsPtr", roomsPtr),
        };

        ConsoleUI.Info($"\nSearching for byte={expectedCount} near known pointers...\n");

        foreach (var (name, ptr) in pointers)
        {
            if (ptr == IntPtr.Zero) continue;

            // Scan -0x2000 to +0x10000 from the pointer
            int scanBefore = 0x2000;
            int scanAfter = 0x10000;
            IntPtr scanStart = ptr - scanBefore;
            int scanSize = scanBefore + scanAfter;

            byte[] data = memory.ReadBytes(scanStart, scanSize);
            var matches = new List<int>();

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == (byte)expectedCount)
                    matches.Add(i - scanBefore); // offset from pointer
            }

            if (matches.Count <= 50)
            {
                foreach (int off in matches)
                {
                    // Try writing 99 and check if it's plausibly inventory
                    // Don't auto-write, just show candidates
                    IntPtr addr = ptr + off;
                    Console.WriteLine($"  {name}+0x{off:X} (0x{(long)addr:X}) = {expectedCount}");
                }
            }
            else
            {
                ConsoleUI.Info($"  {name}: {matches.Count} matches (too many to list)");
            }
        }

        // Also scan Lara INFO area (static variables 0x310E80 - 0x311100)
        ConsoleUI.Info($"\nLara INFO area (tomb1.dll+0x310E80 to +0x311100):");
        int infoStart = 0x310E80;
        int infoSize = 0x311100 - 0x310E80;
        byte[] infoData = memory.ReadBytes(t + infoStart, infoSize);

        for (int i = 0; i < infoData.Length; i++)
        {
            if (infoData[i] == (byte)expectedCount)
            {
                int offset = infoStart + i;
                Console.WriteLine($"  tomb1.dll+0x{offset:X} = {expectedCount}");
            }
        }

        // Offer write test for candidates near known pointers
        Console.WriteLine();
        ConsoleUI.Warning("Want to write-test a specific address? Enter hex address (or ENTER to skip):");
        string? addrStr = Console.ReadLine()?.Trim();
        while (!string.IsNullOrEmpty(addrStr))
        {
            try
            {
                long addr = Convert.ToInt64(addrStr, 16);
                IntPtr ptr2 = new IntPtr(addr);
                byte before = memory.ReadByte(ptr2);
                memory.Write(ptr2, (byte)99);
                ConsoleUI.Info($"Wrote 99 to 0x{addr:X} (was {before}). Check inventory! (ENTER to restore)");
                Console.ReadLine();
                memory.Write(ptr2, before);
            }
            catch (Exception ex)
            {
                ConsoleUI.Error($"Error: {ex.Message}");
            }

            ConsoleUI.Warning("Another address? (or ENTER to quit):");
            addrStr = Console.ReadLine()?.Trim();
        }

        memory.Dispose();
    }

    static void PrintKnownPointers(ProcessMemory memory)
    {
        IntPtr t = memory.Tomb1Base;
        ConsoleUI.Info("Known pointers:");

        IntPtr laraPtr = memory.ReadPointer(t + TR1RMemoryMap.LaraBase);
        IntPtr entPtr = memory.ReadPointer(t + TR1RMemoryMap.EntitiesPointer);
        IntPtr roomsPtr = memory.ReadPointer(t + TR1RMemoryMap.RoomsPointer);
        int levelId = memory.ReadInt32(t, TR1RMemoryMap.LevelId);

        Console.WriteLine($"  tomb1.dll base:    0x{(long)t:X}");
        Console.WriteLine($"  LaraBase ptr:      0x{(long)laraPtr:X}  (tomb1.dll+0x{TR1RMemoryMap.LaraBase:X})");
        Console.WriteLine($"  EntitiesPointer:   0x{(long)entPtr:X}  (tomb1.dll+0x{TR1RMemoryMap.EntitiesPointer:X})");
        Console.WriteLine($"  RoomsPointer:      0x{(long)roomsPtr:X}  (tomb1.dll+0x{TR1RMemoryMap.RoomsPointer:X})");
        Console.WriteLine($"  Level ID:          {levelId}");
    }

    /// <summary>
    /// Tries to find a pointer in tomb1.dll that points near the given heap address.
    /// This would give us a stable way to locate the address across restarts.
    /// </summary>
    static void FindPointerChain(ProcessMemory memory, IntPtr tomb1Base, long targetAddr)
    {
        ConsoleUI.Info("Searching for pointer chain from tomb1.dll...");

        // Scan tomb1.dll for any 8-byte value that points within 0x1000 of the target
        int scanSize = 0x500000;
        byte[] dllData = memory.ReadBytes(tomb1Base, scanSize);

        long targetPage = targetAddr & ~0xFFF; // align to 4KB page

        for (int i = 0; i <= dllData.Length - 8; i += 8)
        {
            long ptr = BitConverter.ToInt64(dllData, i);
            if (ptr == 0) continue;

            long diff = targetAddr - ptr;
            if (diff >= -0x10000 && diff <= 0x10000)
            {
                int dllOffset = i;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  Pointer: tomb1.dll+0x{dllOffset:X} = 0x{ptr:X} (target is at +0x{diff:X} from pointer)");
                Console.ResetColor();
            }
        }
    }

    static Dictionary<IntPtr, byte[]> TakeFullSnapshot(ProcessMemory memory, List<(IntPtr Address, int Size)> regions)
    {
        var snapshot = new Dictionary<IntPtr, byte[]>();
        foreach (var (addr, size) in regions)
        {
            try
            {
                byte[] data = memory.ReadBytes(addr, size);
                snapshot[addr] = data;
            }
            catch { /* skip unreadable */ }
        }
        return snapshot;
    }

    static byte ReadByteFromSnapshots(Dictionary<IntPtr, byte[]> snapshot, IntPtr addr)
    {
        foreach (var (baseAddr, data) in snapshot)
        {
            long offset = (long)addr - (long)baseAddr;
            if (offset >= 0 && offset < data.Length)
                return data[(int)offset];
        }
        return 0;
    }

    /// <summary>
    /// Writes test values to candidate inventory addresses to see which one
    /// actually affects the in-game small medipack count.
    /// </summary>
    static async Task WriteTest()
    {
        ConsoleUI.Info("=== WRITE TEST: find the real live inventory offset ===\n");

        var memory = new ProcessMemory();

        ConsoleUI.Info("Waiting for tomb123.exe...");
        while (true)
        {
            if (memory.TryAttach() && memory.Tomb1Base != IntPtr.Zero)
            {
                ConsoleUI.Success($"Attached! tomb1.dll at 0x{memory.Tomb1Base:X}");
                break;
            }
            await Task.Delay(1000);
            if (memory.IsAttached)
                memory.RefreshTomb1Base();
        }

        IntPtr tomb1Base = memory.Tomb1Base;

        // Candidate offsets from the --find-inventory scan that showed 0 -> 1 -> 2
        // Both are inside WorldStateBackup (base 0x4C4E00):
        //   0x4C5426 = WSB + 0x626
        //   0x4C5464 = WSB + 0x664
        // Our old (wrong?) offset was Save_SmallMedipacks = 0x4C8
        int[] candidateOffsets = { 0x626, 0x664, 0x4C8 };

        ConsoleUI.Info("Be in a level with 0 small medipacks in inventory.");
        ConsoleUI.Info("Open inventory (or check medipack count) after each write.\n");

        foreach (int offset in candidateOffsets)
        {
            IntPtr addr = tomb1Base + TR1RMemoryMap.WorldStateBackup + offset;
            byte current = memory.ReadByte(addr);

            ConsoleUI.Warning($"Press ENTER to write 10 at WSB+0x{offset:X} (tomb1.dll+0x{TR1RMemoryMap.WorldStateBackup + offset:X}), currently={current}");
            Console.ReadLine();

            memory.Write(addr, (byte)10);
            byte verify = memory.ReadByte(addr);
            ConsoleUI.Info($"  Written 10, read back: {verify}. Check your in-game inventory now!");

            ConsoleUI.Warning("Press ENTER to reset to 0 and try next offset...");
            Console.ReadLine();
            memory.Write(addr, (byte)0);
        }

        // Also try writing as UInt16 to the two candidates
        ConsoleUI.Info("\n=== Now trying UInt16 writes ===\n");
        int[] u16Offsets = { 0x626, 0x664 };

        foreach (int offset in u16Offsets)
        {
            IntPtr addr = tomb1Base + TR1RMemoryMap.WorldStateBackup + offset;
            ushort current = memory.ReadUInt16(addr);

            ConsoleUI.Warning($"Press ENTER to write 10 (u16) at WSB+0x{offset:X}, currently={current}");
            Console.ReadLine();

            memory.Write(addr, (ushort)10);
            ushort verify = memory.ReadUInt16(addr);
            ConsoleUI.Info($"  Written 10, read back: {verify}. Check your in-game inventory now!");

            ConsoleUI.Warning("Press ENTER to reset and continue...");
            Console.ReadLine();
            memory.Write(addr, (ushort)0);
        }

        ConsoleUI.Success("Write test complete.");
        memory.Dispose();
    }

    private static string? GetArg(string[] args, int index, string? defaultValue)
    {
        return index < args.Length ? args[index] : defaultValue;
    }

    private static int CountBits(ushort v)
    {
        int count = 0;
        while (v != 0) { count += v & 1; v >>= 1; }
        return count;
    }
}
