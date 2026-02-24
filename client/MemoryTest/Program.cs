using TRArchipelagoClient.GameInterface;

Console.Title = "TR1R Memory Test";
Console.WriteLine("=== TR1 Remastered - Memory Reader Test ===");
Console.WriteLine("Launch tomb123.exe and start playing TR1.\n");

using var memory = new ProcessMemory();

// Wait for game
Console.Write("Waiting for tomb123.exe...");
while (!memory.TryAttach())
{
    Console.Write(".");
    Thread.Sleep(1000);
}
Console.WriteLine($"\nAttached! PID found.");
Console.WriteLine($"  tomb123.exe base: 0x{memory.ExeBase:X}");

// Wait for tomb1.dll
Console.Write("Waiting for tomb1.dll...");
while (memory.Tomb1Base == IntPtr.Zero)
{
    memory.RefreshTomb1Base();
    if (memory.Tomb1Base == IntPtr.Zero)
    {
        Console.Write(".");
        Thread.Sleep(1000);
    }
}
IntPtr t1 = memory.Tomb1Base;
Console.WriteLine($"\n  tomb1.dll base: 0x{t1:X}");

Console.WriteLine("\nChoose mode:");
Console.WriteLine("  1 = Live monitoring (HP, pos, level, entities)");
Console.WriteLine("  2 = Inventory scanner (find live ammo/weapon offsets)");
Console.WriteLine("  3 = Entity flag watcher (test pickup detection)");
Console.WriteLine("  4 = Entity struct dumper (find real struct layout)");
Console.Write("\nMode: ");
string mode = Console.ReadLine()?.Trim() ?? "1";

switch (mode)
{
    case "2":
        RunInventoryScanner(memory, t1);
        break;
    case "3":
        RunEntityWatcher(memory, t1);
        break;
    case "4":
        RunEntityDumper(memory, t1);
        break;
    default:
        RunLiveMonitor(memory, t1);
        break;
}

// ====================================================================
// MODE 1: Live monitoring
// ====================================================================
static void RunLiveMonitor(ProcessMemory memory, IntPtr t1)
{
    Console.WriteLine("\n--- Real-time monitoring (Ctrl+C to stop) ---\n");

    int lastLevel = -1;
    short lastHealth = -1;
    ushort lastSecrets = 0;

    while (memory.IsAttached)
    {
        try
        {
            int inGame = memory.ReadInt32(t1, TR1RMemoryMap.IsInGameScene);
            int levelId = memory.ReadInt32(t1, TR1RMemoryMap.LevelId);
            int levelCompleted = memory.ReadInt32(t1, TR1RMemoryMap.LevelCompleted);
            int entityCount = memory.ReadInt16(t1 + TR1RMemoryMap.EntitiesCount);
            ushort secrets = memory.ReadUInt16(t1 + TR1RMemoryMap.WorldStateBackup + TR1RMemoryMap.Save_SecretsFound);

            IntPtr laraPtr = memory.ReadPointer(t1, TR1RMemoryMap.LaraBase);
            short health = 0;
            int posX = 0, posY = 0, posZ = 0;
            short room = 0, speed = 0;

            if (laraPtr != IntPtr.Zero)
            {
                health = memory.ReadInt16(laraPtr + TR1RMemoryMap.Item_HitPoints);
                posX = memory.ReadInt32(laraPtr + TR1RMemoryMap.Item_PosX);
                posY = memory.ReadInt32(laraPtr + TR1RMemoryMap.Item_PosY);
                posZ = memory.ReadInt32(laraPtr + TR1RMemoryMap.Item_PosZ);
                room = memory.ReadInt16(laraPtr + TR1RMemoryMap.Item_RoomNum);
                speed = memory.ReadInt16(laraPtr + TR1RMemoryMap.Item_Speed);
            }

            string levelName = TR1RMemoryMap.LevelNames.GetValueOrDefault(levelId, $"Unknown ({levelId})");
            string state = inGame > 0 ? "IN-GAME" : "MENU";

            if (levelId != lastLevel)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n>>> LEVEL CHANGE: {levelName} (ID={levelId}) <<<\n");
                Console.ResetColor();
            }

            if (secrets != lastSecrets)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                int count = 0; ushort v = secrets;
                while (v != 0) { count += v & 1; v >>= 1; }
                Console.WriteLine($"  *** SECRET! Bitmask: 0b{Convert.ToString(secrets, 2).PadLeft(8, '0')} ({count} total) ***");
                Console.ResetColor();
            }

            Console.Write($"\r  [{state}] {levelName,-25} HP:{health,5}/{TR1RMemoryMap.MaxHealth}" +
                          $"  Pos:({posX,7},{posY,7},{posZ,7}) Room:{room,3}" +
                          $"  Spd:{speed,4}  Ent:{entityCount,3}" +
                          $"  Secrets:0b{Convert.ToString(secrets, 2).PadLeft(3, '0')}" +
                          $"  Done:{levelCompleted}     ");

            if (levelId != lastLevel || health != lastHealth)
                Console.WriteLine();

            lastLevel = levelId;
            lastHealth = health;
            lastSecrets = secrets;
        }
        catch (Exception ex) { Console.WriteLine($"\n  Error: {ex.Message}"); }

        Thread.Sleep(100);
    }
}

// ====================================================================
// MODE 2: Inventory scanner - find live offsets by watching memory changes
// ====================================================================
static void RunInventoryScanner(ProcessMemory memory, IntPtr t1)
{
    // We'll scan the LARA_INFO region around tomb1.dll+0x310e80
    // and also a wider area to find inventory data
    int laraInfoBase = 0x310e80;
    int scanSize = 0x200; // 512 bytes should cover LARA_INFO

    Console.WriteLine("\n=== INVENTORY SCANNER ===");
    Console.WriteLine($"Scanning {scanSize} bytes from tomb1.dll+0x{laraInfoBase:X}");
    Console.WriteLine();
    Console.WriteLine("Instructions:");
    Console.WriteLine("  1. Get into the game (in a level, not menu)");
    Console.WriteLine("  2. Press ENTER to take a snapshot BEFORE picking up an item");
    Console.WriteLine("  3. Pick up the item in-game");
    Console.WriteLine("  4. Press ENTER again to see what changed");
    Console.WriteLine("  5. Repeat for different item types (ammo, medipack, weapon)");
    Console.WriteLine();

    while (memory.IsAttached)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("Press ENTER to take BASELINE snapshot (before picking up)...");
        Console.ResetColor();
        Console.ReadLine();

        // Take baseline snapshot
        IntPtr scanAddr = t1 + laraInfoBase;
        byte[] baseline = memory.ReadBytes(scanAddr, scanSize);

        // Also read current health for reference
        IntPtr laraPtr = memory.ReadPointer(t1, TR1RMemoryMap.LaraBase);
        short health = laraPtr != IntPtr.Zero ? memory.ReadInt16(laraPtr + TR1RMemoryMap.Item_HitPoints) : (short)0;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Baseline captured! (HP={health})");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("Now PICK UP AN ITEM, then press ENTER...");
        Console.ResetColor();
        Console.ReadLine();

        // Read current state
        byte[] current = memory.ReadBytes(scanAddr, scanSize);
        short healthAfter = laraPtr != IntPtr.Zero ? memory.ReadInt16(laraPtr + TR1RMemoryMap.Item_HitPoints) : (short)0;

        // Compare and show differences
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n  === CHANGES DETECTED (HP: {health} -> {healthAfter}) ===\n");
        Console.ResetColor();

        int changesFound = 0;
        for (int i = 0; i < scanSize; i++)
        {
            if (baseline[i] != current[i])
            {
                int absOffset = laraInfoBase + i;
                Console.Write($"  tomb1.dll+0x{absOffset:X6} (LARA_INFO+0x{i:X3}): ");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"{baseline[i]:X2}");
                Console.ResetColor();
                Console.Write(" -> ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"{current[i]:X2}");
                Console.ResetColor();

                // Try to interpret as Int16 and Int32
                if (i + 1 < scanSize)
                {
                    short oldI16 = BitConverter.ToInt16(baseline, i);
                    short newI16 = BitConverter.ToInt16(current, i);
                    Console.Write($"  (as Int16: {oldI16} -> {newI16})");
                }
                if (i + 3 < scanSize)
                {
                    int oldI32 = BitConverter.ToInt32(baseline, i);
                    int newI32 = BitConverter.ToInt32(current, i);
                    Console.Write($"  (as Int32: {oldI32} -> {newI32})");
                }

                Console.WriteLine();
                changesFound++;
            }
        }

        if (changesFound == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  No changes detected in LARA_INFO region!");
            Console.WriteLine("  The inventory might be stored elsewhere. Let's try other regions...");
            Console.ResetColor();

            // Try scanning a wider area
            Console.WriteLine("\n  Scanning wider regions...");

            // Scan around WorldStateBackup
            int wsb = TR1RMemoryMap.WorldStateBackup;
            byte[] wsbBaseline = memory.ReadBytes(t1 + wsb, 0x700);
            Console.Write("  Press ENTER after picking up another item to scan WorldStateBackup...");
            Console.ReadLine();
            byte[] wsbCurrent = memory.ReadBytes(t1 + wsb, 0x700);

            for (int i = 0; i < 0x700; i++)
            {
                if (wsbBaseline[i] != wsbCurrent[i])
                {
                    int absOffset = wsb + i;
                    Console.Write($"  tomb1.dll+0x{absOffset:X6} (WSB+0x{i:X3}): ");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"{wsbBaseline[i]:X2}");
                    Console.ResetColor();
                    Console.Write(" -> ");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"{wsbCurrent[i]:X2}");
                    Console.ResetColor();

                    if (i + 1 < 0x700)
                    {
                        short oldI16 = BitConverter.ToInt16(wsbBaseline, i);
                        short newI16 = BitConverter.ToInt16(wsbCurrent, i);
                        Console.Write($"  (as Int16: {oldI16} -> {newI16})");
                    }
                    Console.WriteLine();
                }
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n  {changesFound} byte(s) changed total.");
            Console.ResetColor();
        }

        Console.WriteLine();
    }
}

// ====================================================================
// MODE 3: Entity flag watcher - test pickup detection
// ====================================================================
static void RunEntityWatcher(ProcessMemory memory, IntPtr t1)
{
    Console.WriteLine("\n=== ENTITY FLAG WATCHER ===");
    Console.WriteLine("This watches entity flags for changes (pickup detection test)\n");

    IntPtr entitiesBase = memory.ReadPointer(t1, TR1RMemoryMap.EntitiesPointer);
    int entityCount = memory.ReadInt16(t1 + TR1RMemoryMap.EntitiesCount);

    Console.WriteLine($"Entity array: 0x{entitiesBase:X}");
    Console.WriteLine($"Entity count: {entityCount}");
    Console.WriteLine();

    if (entitiesBase == IntPtr.Zero || entityCount <= 0)
    {
        Console.WriteLine("No entities found. Make sure you're in a level (not menu).");
        return;
    }

    // Snapshot all entity flags and object IDs
    var entityData = new Dictionary<int, (short objectId, short flags, short hitPoints)>();

    Console.WriteLine("Taking initial entity snapshot...");
    for (int i = 0; i < entityCount; i++)
    {
        IntPtr entityAddr = entitiesBase + (i * TR1RMemoryMap.EntitySize);
        short objectId = memory.ReadInt16(entityAddr + TR1RMemoryMap.Item_ObjectId);
        short flags = memory.ReadInt16(entityAddr + TR1RMemoryMap.Item_Flags);
        short hp = memory.ReadInt16(entityAddr + TR1RMemoryMap.Item_HitPoints);
        entityData[i] = (objectId, flags, hp);
    }

    // Show all entities with their types
    Console.WriteLine($"\nEntities in level ({entityCount} total):");
    Console.ForegroundColor = ConsoleColor.Gray;
    for (int i = 0; i < entityCount; i++)
    {
        var (objId, flags, hp) = entityData[i];
        if (objId != 0) // Skip null entities
        {
            Console.WriteLine($"  [{i,3}] TypeID={objId,4}  Flags=0x{flags:X4}  HP={hp}");
        }
    }
    Console.ResetColor();

    Console.WriteLine("\n--- Watching for flag changes (pick up items!) ---\n");

    while (memory.IsAttached)
    {
        for (int i = 0; i < entityCount; i++)
        {
            IntPtr entityAddr = entitiesBase + (i * TR1RMemoryMap.EntitySize);
            short currentFlags = memory.ReadInt16(entityAddr + TR1RMemoryMap.Item_Flags);
            short currentHp = memory.ReadInt16(entityAddr + TR1RMemoryMap.Item_HitPoints);

            var (objId, prevFlags, prevHp) = entityData[i];

            if (currentFlags != prevFlags || currentHp != prevHp)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"  CHANGE Entity[{i}] TypeID={objId}: ");

                if (currentFlags != prevFlags)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write($"Flags 0x{prevFlags:X4} -> 0x{currentFlags:X4}  ");
                }
                if (currentHp != prevHp)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"HP {prevHp} -> {currentHp}  ");
                }

                Console.ResetColor();
                Console.WriteLine();

                entityData[i] = (objId, currentFlags, currentHp);
            }
        }

        Thread.Sleep(100);
    }
}

// ====================================================================
// MODE 4: Entity struct dumper - find the real struct layout
// ====================================================================
static void RunEntityDumper(ProcessMemory memory, IntPtr t1)
{
    Console.WriteLine("\n=== ENTITY STRUCT DUMPER ===");
    Console.WriteLine("Dumps raw bytes of entities to find the real struct layout.\n");

    IntPtr entitiesBase = memory.ReadPointer(t1, TR1RMemoryMap.EntitiesPointer);
    int entityCount = memory.ReadInt16(t1 + TR1RMemoryMap.EntitiesCount);

    Console.WriteLine($"Entity array: 0x{entitiesBase:X}");
    Console.WriteLine($"Entity count: {entityCount}");

    IntPtr laraPtr = memory.ReadPointer(t1, TR1RMemoryMap.LaraBase);
    Console.WriteLine($"Lara pointer:  0x{laraPtr:X}");

    if (entitiesBase == IntPtr.Zero || entityCount <= 0)
    {
        Console.WriteLine("No entities. Get into a level first.");
        return;
    }

    // Find Lara in entity array
    int laraIndex = -1;
    long laraOffset = laraPtr.ToInt64() - entitiesBase.ToInt64();
    Console.WriteLine($"\nLara offset from entities base: 0x{laraOffset:X} ({laraOffset} bytes)");

    if (laraOffset > 0)
    {
        // Check if it aligns with expected entity size
        Console.WriteLine($"  offset / 0xE50 = {laraOffset / (double)0xE50:F4}");

        // Try common entity sizes to find the right one
        foreach (int trySize in new[] { 0xE50, 0xE48, 0xE40, 0xE58, 0xE60, 0xE00, 0xF00, 0xE54, 0xE4C })
        {
            if (laraOffset % trySize == 0)
            {
                laraIndex = (int)(laraOffset / trySize);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  MATCH: EntitySize=0x{trySize:X} -> Lara at index {laraIndex}");
                Console.ResetColor();
            }
        }

        // Also try dividing by known Lara ID
        short laraId = memory.ReadInt16(t1 + TR1RMemoryMap.LaraId);
        Console.WriteLine($"  LaraId from static var: {laraId}");
        if (laraId > 0)
        {
            int calcSize = (int)(laraOffset / laraId);
            Console.WriteLine($"  offset / LaraId = 0x{calcSize:X} ({calcSize} bytes) -> possible entity size?");
        }
    }

    // Dump Lara's entity header (first 0x70 bytes)
    Console.WriteLine($"\n--- Lara entity dump (first 0x70 bytes from 0x{laraPtr:X}) ---\n");
    if (laraPtr != IntPtr.Zero)
    {
        byte[] raw = memory.ReadBytes(laraPtr, 0x70);
        for (int row = 0; row < 0x70; row += 16)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  +0x{row:X2}: ");
            Console.ResetColor();
            for (int col = 0; col < 16 && row + col < 0x70; col++)
                Console.Write($"{raw[row + col]:X2} ");

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("  | i16:");
            for (int col = 0; col < 16 && row + col + 1 < 0x70; col += 2)
            {
                short val = BitConverter.ToInt16(raw, row + col);
                Console.Write($"{val,7}");
            }
            Console.ResetColor();
            Console.WriteLine();
        }

        // Highlight interesting values
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n  Interesting values:");
        short knownHealth = memory.ReadInt16(laraPtr + 0x26);
        Console.WriteLine($"    +0x26 = {knownHealth} (health from Burns mod offset)");

        // Scan for health-like values
        for (int off = 0; off < 0x40; off += 2)
        {
            short val = BitConverter.ToInt16(raw, off);
            if (val >= 500 && val <= 1000)
                Console.WriteLine($"    +0x{off:X2} = {val} (could be health)");
        }
        Console.ResetColor();
    }

    // Dump a pickup entity for comparison
    Console.WriteLine("\n--- First few entities header dumps ---\n");
    int dumpSize = Math.Min(5, entityCount);
    for (int idx = 0; idx < dumpSize; idx++)
    {
        IntPtr entityAddr = entitiesBase + idx * TR1RMemoryMap.EntitySize;
        byte[] raw = memory.ReadBytes(entityAddr, 0x30);

        bool isLara = (entityAddr == laraPtr);
        Console.Write($"  Entity[{idx}]");
        if (isLara) { Console.ForegroundColor = ConsoleColor.Cyan; Console.Write(" (LARA)"); Console.ResetColor(); }
        Console.Write($" +0x00: ");
        for (int j = 0; j < 0x30; j++) Console.Write($"{raw[j]:X2} ");
        Console.WriteLine();
    }

    Console.WriteLine("\nPress ENTER to also dump flags area...");
    Console.ReadLine();

    // Dump flags area around +0x1E4
    Console.WriteLine("--- Flags area (+0x1D0 to +0x200) for first entities ---\n");
    for (int idx = 0; idx < Math.Min(10, entityCount); idx++)
    {
        IntPtr entityAddr = entitiesBase + idx * TR1RMemoryMap.EntitySize;
        byte[] flagArea = memory.ReadBytes(entityAddr + 0x1D0, 0x30);
        short flags = BitConverter.ToInt16(flagArea, 0x14); // +0x1E4 relative to struct start

        Console.Write($"  E[{idx,2}] Flags@+0x1E4=0x{flags:X4}  raw: ");
        for (int j = 0x10; j < 0x1A; j++) Console.Write($"{flagArea[j]:X2} ");
        Console.WriteLine();
    }
}
