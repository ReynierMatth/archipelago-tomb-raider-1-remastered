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
        var backupManager = new BackupManager(gameDir);
        var memory = new ProcessMemory();
        var cts = new CancellationTokenSource();

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

            ConsoleUI.Info("Backing up original level files...");
            backupManager.BackupAll();

            ConsoleUI.Info("Patching level files...");
            var patcher = new LevelPatcher(gameDir, session);
            patcher.PatchAll();

            ConsoleUI.Success("Level files patched.");

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
            if (backupManager.HasBackups() && ConsoleUI.Confirm("Restore original level files?"))
            {
                backupManager.RestoreAll();
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
