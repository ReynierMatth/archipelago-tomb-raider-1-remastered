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
Console.WriteLine("  5 = Inventory ring tester (add items to inventory)");
Console.WriteLine("  6 = Keys ring explorer (discover key item offsets)");
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
    case "5":
        RunInventoryRingTester(memory, t1);
        break;
    case "6":
        RunKeysRingExplorer(memory, t1);
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

// ====================================================================
// MODE 5: Inventory ring tester - add items to Lara's inventory
// ====================================================================
static void RunInventoryRingTester(ProcessMemory memory, IntPtr t1)
{
    Console.WriteLine("\n=== INVENTORY RING TESTER ===");
    Console.WriteLine("Add items directly to the main inventory ring via memory.\n");

    // Item definitions: name, relative index from Pistols, default qty
    var items = new (string Name, int RelIndex, int DefaultQty)[]
    {
        ("Shotgun",         TR1RMemoryMap.InvItemRelIndex.Shotgun,       1),
        ("Magnums",         TR1RMemoryMap.InvItemRelIndex.Magnums,       1),
        ("Uzis",            TR1RMemoryMap.InvItemRelIndex.Uzis,          1),
        ("Small Medipack",  TR1RMemoryMap.InvItemRelIndex.SmallMedipack, 1),
        ("Large Medipack",  TR1RMemoryMap.InvItemRelIndex.LargeMedipack, 1),
        ("Compass",         TR1RMemoryMap.InvItemRelIndex.Compass,       1),
    };

    while (memory.IsAttached)
    {
        // Read current ring state
        short ringCount = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingCount);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n--- Current Main Ring ({ringCount} items) ---");
        Console.ResetColor();

        // Find Pistols pointer (usually at index 1, but search to be safe)
        IntPtr pistolsPtr = IntPtr.Zero;
        for (int i = 0; i < ringCount; i++)
        {
            IntPtr itemPtr = memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + i * 8);
            short qty = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingQtys + i * 2);

            // Read object_id from the INVENTORY_ITEM struct
            short objectId = (itemPtr != IntPtr.Zero) ? memory.ReadInt16(itemPtr + TR1RMemoryMap.InvItem_ObjectId) : (short)-1;

            // Identify the item by checking if its pointer matches known relative offsets
            string name = $"Unknown (objId=0x{objectId:X})";
            if (pistolsPtr != IntPtr.Zero)
            {
                long offset = itemPtr.ToInt64() - pistolsPtr.ToInt64();
                int relIdx = (int)(offset / TR1RMemoryMap.InventoryItemStride);
                if (offset % TR1RMemoryMap.InventoryItemStride == 0)
                    name = IdentifyItem(relIdx);
            }

            // Pistols are at relative index 0 — detect by object_id pattern or position
            // Pistols is typically index 1 in the ring (after Compass)
            // We can identify Pistols: its pointer, when offset by known items, should match
            if (pistolsPtr == IntPtr.Zero && i <= 2 && itemPtr != IntPtr.Zero)
            {
                // Try this pointer as Pistols reference: check if Compass would be at +6*stride
                IntPtr testCompass = itemPtr + TR1RMemoryMap.InvItemRelIndex.Compass * TR1RMemoryMap.InventoryItemStride;
                // Read compass object_id — if we can read it and it looks valid, this might be pistols
                // Better approach: read items[0] (usually Compass at +6) and check if items[0] = this + 6*stride
                if (ringCount >= 2)
                {
                    IntPtr item0 = memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems);
                    long diff = item0.ToInt64() - itemPtr.ToInt64();
                    if (diff == TR1RMemoryMap.InvItemRelIndex.Compass * TR1RMemoryMap.InventoryItemStride)
                    {
                        pistolsPtr = itemPtr;
                        name = "Pistols";
                    }
                }
            }

            Console.Write($"  [{i,2}] ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"0x{itemPtr:X}");
            Console.ResetColor();
            Console.Write($"  objId=0x{objectId:X4}  qty={qty,3}");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  {name}");
            Console.ResetColor();
        }

        // If Pistols not found via Compass cross-check, try brute force:
        // Ring index 1 is almost always Pistols
        if (pistolsPtr == IntPtr.Zero && ringCount >= 2)
        {
            pistolsPtr = memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + 1 * 8);
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"  (Assuming items[1] = Pistols: 0x{pistolsPtr:X})");
            Console.ResetColor();
        }

        if (pistolsPtr == IntPtr.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Cannot find Pistols pointer! Make sure you're in a level with pistols.");
            Console.ResetColor();
            Console.WriteLine("Press ENTER to retry...");
            Console.ReadLine();
            continue;
        }

        // Re-display ring with proper names now that we have Pistols ref
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n  Pistols reference: 0x{pistolsPtr:X}");
        Console.ResetColor();

        // Check which items already exist in ring
        var existingItems = new Dictionary<int, int>(); // relIndex -> ring position
        for (int i = 0; i < ringCount; i++)
        {
            IntPtr itemPtr = memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + i * 8);
            long diff = itemPtr.ToInt64() - pistolsPtr.ToInt64();
            if (diff % TR1RMemoryMap.InventoryItemStride == 0)
            {
                int relIdx = (int)(diff / TR1RMemoryMap.InventoryItemStride);
                existingItems[relIdx] = i;
            }
        }

        // Show menu
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n--- Add Item ---");
        Console.ResetColor();
        for (int i = 0; i < items.Length; i++)
        {
            bool exists = existingItems.ContainsKey(items[i].RelIndex);
            string status = exists ? " (already in ring, will +1 qty)" : "";
            Console.WriteLine($"  {i + 1} = {items[i].Name}{status}");
        }
        Console.WriteLine($"  7 = Dump ring raw bytes");
        Console.WriteLine($"  8 = Give ammo (shotgun/magnum/uzi)");
        Console.WriteLine($"  0 = Refresh / re-read ring");
        Console.WriteLine($"  q = Quit");

        Console.Write("\nChoice: ");
        string? input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input) || input == "0")
            continue;
        if (input.ToLower() == "q")
            break;

        if (input == "7")
        {
            DumpRingRawBytes(memory, t1, ringCount);
            continue;
        }

        if (input == "8")
        {
            GiveAmmoMenu(memory, t1);
            continue;
        }

        if (!int.TryParse(input, out int choice) || choice < 1 || choice > items.Length)
        {
            Console.WriteLine("Invalid choice.");
            continue;
        }

        var selected = items[choice - 1];
        IntPtr targetPtr = pistolsPtr + selected.RelIndex * TR1RMemoryMap.InventoryItemStride;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n  Target: {selected.Name}");
        Console.WriteLine($"  Pointer: 0x{targetPtr:X} (Pistols + {selected.RelIndex} * 0x{TR1RMemoryMap.InventoryItemStride:X})");

        // Verify the target pointer looks valid (read object_id from the INVENTORY_ITEM struct)
        short targetObjId = memory.ReadInt16(targetPtr + TR1RMemoryMap.InvItem_ObjectId);
        Console.WriteLine($"  Object ID at target: 0x{targetObjId:X4} ({targetObjId})");
        Console.ResetColor();

        if (existingItems.TryGetValue(selected.RelIndex, out int existingPos))
        {
            // Item already in ring — increment qty
            IntPtr qtyAddr = t1 + TR1RMemoryMap.MainRingQtys + existingPos * 2;
            short currentQty = memory.ReadInt16(qtyAddr);
            short newQty = (short)Math.Min(currentQty + 1, 255);
            memory.Write(qtyAddr, newQty);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  >>> {selected.Name} qty: {currentQty} -> {newQty} (at ring index {existingPos})");
            Console.ResetColor();
        }
        else
        {
            // New item — append at end of ring
            ringCount = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingCount); // re-read
            if (ringCount >= TR1RMemoryMap.MaxRingItems)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  Ring is full (24 items)! Cannot add more.");
                Console.ResetColor();
                continue;
            }

            // Write the item pointer at items[count]
            IntPtr newItemSlot = t1 + TR1RMemoryMap.MainRingItems + ringCount * 8;
            memory.Write(newItemSlot, targetPtr.ToInt64());

            // Write qty at qtys[count]
            IntPtr newQtySlot = t1 + TR1RMemoryMap.MainRingQtys + ringCount * 2;
            memory.Write(newQtySlot, (short)selected.DefaultQty);

            // Increment count
            short newCount = (short)(ringCount + 1);
            memory.Write(t1 + TR1RMemoryMap.MainRingCount, newCount);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  >>> Added {selected.Name} at ring index {ringCount}, qty={selected.DefaultQty}");
            Console.WriteLine($"  >>> Ring count: {ringCount} -> {newCount}");
            Console.ResetColor();

            // Verify write
            IntPtr verifyPtr = memory.ReadPointer(newItemSlot);
            short verifyQty = memory.ReadInt16(newQtySlot);
            short verifyCount = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingCount);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Verify: ptr=0x{verifyPtr:X} qty={verifyQty} count={verifyCount}");
            Console.ResetColor();
        }
    }
}

static string IdentifyItem(int relIndex)
{
    // Try known main ring items first
    string? name = relIndex switch
    {
        TR1RMemoryMap.InvItemRelIndex.SmallMedipack => "Small Medipack",
        TR1RMemoryMap.InvItemRelIndex.LargeMedipack => "Large Medipack",
        TR1RMemoryMap.InvItemRelIndex.Pistols => "Pistols",
        TR1RMemoryMap.InvItemRelIndex.Shotgun => "Shotgun",
        TR1RMemoryMap.InvItemRelIndex.Compass => "Compass",
        TR1RMemoryMap.InvItemRelIndex.Uzis => "Uzis",
        TR1RMemoryMap.InvItemRelIndex.Magnums => "Magnums",
        _ => null,
    };
    return name ?? $"? (relIdx={relIndex})";
}

static void DumpRingRawBytes(ProcessMemory memory, IntPtr t1, short ringCount)
{
    Console.WriteLine("\n--- Raw Ring Dump ---");

    Console.WriteLine($"  Count @ +0x{TR1RMemoryMap.MainRingCount:X}: {ringCount}");
    Console.WriteLine();

    Console.WriteLine("  Items[] (8-byte pointers):");
    for (int i = 0; i < Math.Min(ringCount + 2, TR1RMemoryMap.MaxRingItems); i++)
    {
        IntPtr ptr = memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + i * 8);
        string marker = i < ringCount ? "*" : " ";
        Console.WriteLine($"    [{i,2}]{marker} 0x{ptr:X16}");
    }

    Console.WriteLine();
    Console.WriteLine("  Qtys[] (Int16):");
    for (int i = 0; i < Math.Min(ringCount + 2, TR1RMemoryMap.MaxRingItems); i++)
    {
        short qty = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingQtys + i * 2);
        string marker = i < ringCount ? "*" : " ";
        Console.WriteLine($"    [{i,2}]{marker} {qty}");
    }
}

static void GiveAmmoMenu(ProcessMemory memory, IntPtr t1)
{
    Console.WriteLine("\n--- Give Ammo ---");
    Console.WriteLine("  1 = Magnum ammo (+50)");
    Console.WriteLine("  2 = Uzi ammo (+100)");
    Console.WriteLine("  3 = Shotgun ammo (+12 internal = 2 shells)");
    Console.Write("Choice: ");
    string? input = Console.ReadLine()?.Trim();

    (int offset, int amount, string name) = input switch
    {
        "1" => (TR1RMemoryMap.Lara_MagnumAmmo, 50, "Magnum"),
        "2" => (TR1RMemoryMap.Lara_UziAmmo, 100, "Uzi"),
        "3" => (TR1RMemoryMap.Lara_ShotgunAmmo, 12, "Shotgun"),
        _ => (-1, 0, ""),
    };

    if (offset < 0) { Console.WriteLine("Invalid."); return; }

    IntPtr addr = t1 + offset;
    int current = memory.ReadInt32(addr);
    int newVal = Math.Min(current + amount, 999999);
    memory.Write(addr, newVal);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  {name} ammo: {current} -> {newVal} at tomb1.dll+0x{offset:X}");
    Console.ResetColor();
}

// ====================================================================
// MODE 6: Keys Ring Explorer - discover key item offsets
// ====================================================================
static void RunKeysRingExplorer(ProcessMemory memory, IntPtr t1)
{
    Console.WriteLine("\n=== KEYS RING EXPLORER ===");
    Console.WriteLine("Explore the Keys Ring and discover key item INVENTORY_ITEM offsets.\n");

    // First, find Pistols pointer (reference for all relIdx calculations)
    IntPtr pistolsPtr = FindPistolsPointer(memory, t1);
    if (pistolsPtr == IntPtr.Zero)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Cannot find Pistols pointer! Make sure you're in a level with pistols.");
        Console.ResetColor();
        return;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  Pistols reference: 0x{pistolsPtr:X}");
    Console.ResetColor();

    while (memory.IsAttached)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n--- Keys Ring Explorer Menu ---");
        Console.ResetColor();
        Console.WriteLine("  1 = Show current Keys Ring state");
        Console.WriteLine("  2 = Watch for key item pickup (live detection)");
        Console.WriteLine("  3 = Scan INVENTORY_ITEM table (find ALL items by object_id)");
        Console.WriteLine("  4 = Scan for Keys Ring qtys[] offset");
        Console.WriteLine("  5 = Inject a key item into Keys Ring");
        Console.WriteLine("  q = Quit");
        Console.Write("\nChoice: ");
        string? input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input) || input.ToLower() == "q")
            break;

        // Re-find Pistols in case of level change
        pistolsPtr = FindPistolsPointer(memory, t1);
        if (pistolsPtr == IntPtr.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Lost Pistols pointer. Are you still in a level?");
            Console.ResetColor();
            continue;
        }

        switch (input)
        {
            case "1":
                ShowKeysRingState(memory, t1, pistolsPtr);
                break;
            case "2":
                WatchKeyItemPickup(memory, t1, pistolsPtr);
                break;
            case "3":
                ScanInventoryItemTable(memory, t1, pistolsPtr);
                break;
            case "4":
                ScanKeysRingQtys(memory, t1, pistolsPtr);
                break;
            case "5":
                InjectKeyItem(memory, t1, pistolsPtr);
                break;
        }
    }
}

/// <summary>Finds the Pistols INVENTORY_ITEM pointer from the Main Ring.</summary>
static IntPtr FindPistolsPointer(ProcessMemory memory, IntPtr t1)
{
    short ringCount = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingCount);
    if (ringCount < 2) return IntPtr.Zero;

    // Strategy: items[0] is typically Compass. Pistols = Compass - 6*stride
    // Or items[1] is Pistols directly. Cross-check both.
    for (int i = 0; i < ringCount; i++)
    {
        IntPtr itemPtr = memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + i * 8);
        if (itemPtr == IntPtr.Zero) continue;

        // Check if items[0] (Compass) confirms this as Pistols
        IntPtr item0 = memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems);
        long diff = item0.ToInt64() - itemPtr.ToInt64();
        if (diff == TR1RMemoryMap.InvItemRelIndex.Compass * TR1RMemoryMap.InventoryItemStride)
            return itemPtr;
    }

    // Fallback: items[1] is almost always Pistols
    if (ringCount >= 2)
        return memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + 1 * 8);

    return IntPtr.Zero;
}

/// <summary>Shows the current state of the Keys Ring.</summary>
static void ShowKeysRingState(ProcessMemory memory, IntPtr t1, IntPtr pistolsPtr)
{
    short keyCount = memory.ReadInt16(t1 + TR1RMemoryMap.KeysRingCount);
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"\n  Keys Ring Count: {keyCount} (at tomb1.dll+0x{TR1RMemoryMap.KeysRingCount:X})");
    Console.ResetColor();

    if (keyCount == 0)
    {
        Console.WriteLine("  (empty — pick up a key item to populate)");
        return;
    }

    for (int i = 0; i < keyCount; i++)
    {
        IntPtr itemPtr = memory.ReadPointer(t1 + TR1RMemoryMap.KeysRingItems + i * 8);
        short objectId = (itemPtr != IntPtr.Zero) ? memory.ReadInt16(itemPtr + TR1RMemoryMap.InvItem_ObjectId) : (short)-1;

        // Calculate relIdx from Pistols
        long offset = itemPtr.ToInt64() - pistolsPtr.ToInt64();
        int relIdx = (offset % TR1RMemoryMap.InventoryItemStride == 0)
            ? (int)(offset / TR1RMemoryMap.InventoryItemStride)
            : 99999;

        string name = TR1RMemoryMap.ObjIdNames.GetValueOrDefault(objectId, $"Unknown");

        // Try to read qty from estimated qtys offset
        short qty = memory.ReadInt16(t1 + TR1RMemoryMap.KeysRingQtys + i * 2);

        Console.Write($"  [{i,2}] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"0x{itemPtr:X}");
        Console.ResetColor();
        Console.Write($"  objId=0x{objectId:X4}  relIdx={relIdx,4}  qty={qty}");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  {name}");
        Console.ResetColor();
    }

    // Also dump raw bytes around the qtys area for analysis
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"\n  Raw bytes near estimated KeysRingQtys (tomb1.dll+0x{TR1RMemoryMap.KeysRingQtys:X}):");
    byte[] raw = memory.ReadBytes(t1 + TR1RMemoryMap.KeysRingQtys, 48);
    Console.Write("    ");
    for (int i = 0; i < raw.Length; i++)
    {
        Console.Write($"{raw[i]:X2} ");
        if ((i + 1) % 16 == 0) Console.Write("\n    ");
    }
    Console.ResetColor();
    Console.WriteLine();
}

/// <summary>Watches for key item pickup in real-time.</summary>
static void WatchKeyItemPickup(ProcessMemory memory, IntPtr t1, IntPtr pistolsPtr)
{
    short prevCount = memory.ReadInt16(t1 + TR1RMemoryMap.KeysRingCount);
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\n  Watching Keys Ring... Current count: {prevCount}");
    Console.WriteLine("  Go pick up a key item! Press any key to stop watching.\n");
    Console.ResetColor();

    // Also snapshot the qtys area (wider scan to find the real offset)
    int scanStart = 0xF8F00; // scan a wide range around estimated qtys
    int scanSize = 0x800;
    byte[] baselineQtyArea = memory.ReadBytes(t1 + scanStart, scanSize);

    while (memory.IsAttached && !Console.KeyAvailable)
    {
        short currentCount = memory.ReadInt16(t1 + TR1RMemoryMap.KeysRingCount);

        if (currentCount != prevCount)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  >>> KEY ITEM DETECTED! Count: {prevCount} -> {currentCount}");
            Console.ResetColor();

            // Read the new item pointer
            int newIdx = currentCount - 1;
            if (newIdx >= 0 && currentCount > prevCount)
            {
                IntPtr newPtr = memory.ReadPointer(t1 + TR1RMemoryMap.KeysRingItems + newIdx * 8);
                short objectId = (newPtr != IntPtr.Zero) ? memory.ReadInt16(newPtr + TR1RMemoryMap.InvItem_ObjectId) : (short)-1;

                long offset = newPtr.ToInt64() - pistolsPtr.ToInt64();
                int relIdx = (offset % TR1RMemoryMap.InventoryItemStride == 0)
                    ? (int)(offset / TR1RMemoryMap.InventoryItemStride)
                    : 99999;

                string name = TR1RMemoryMap.ObjIdNames.GetValueOrDefault(objectId, "Unknown");

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  New item at ring index {newIdx}:");
                Console.WriteLine($"    Pointer:   0x{newPtr:X}");
                Console.WriteLine($"    Object ID: 0x{objectId:X4} ({name})");
                Console.WriteLine($"    RelIdx:    {relIdx} (from Pistols)");
                Console.WriteLine($"    Formula:   pistolsPtr + {relIdx} * 0x{TR1RMemoryMap.InventoryItemStride:X}");
                Console.ResetColor();

                // Scan for qty changes in the wider area
                byte[] currentQtyArea = memory.ReadBytes(t1 + scanStart, scanSize);
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"\n  Scanning for qty changes in tomb1.dll+0x{scanStart:X}..+0x{scanStart + scanSize:X}:");
                int changesFound = 0;
                for (int i = 0; i < scanSize; i++)
                {
                    if (baselineQtyArea[i] != currentQtyArea[i])
                    {
                        int absOffset = scanStart + i;
                        short oldI16 = (i + 1 < scanSize) ? BitConverter.ToInt16(baselineQtyArea, i) : (short)0;
                        short newI16 = (i + 1 < scanSize) ? BitConverter.ToInt16(currentQtyArea, i) : (short)0;
                        Console.WriteLine($"    +0x{absOffset:X6}: 0x{baselineQtyArea[i]:X2} -> 0x{currentQtyArea[i]:X2}" +
                                        $"  (Int16: {oldI16} -> {newI16})");
                        changesFound++;
                    }
                }
                if (changesFound == 0)
                    Console.WriteLine("    No changes in scanned range!");
                Console.ResetColor();

                baselineQtyArea = currentQtyArea;
            }

            prevCount = currentCount;
        }

        Thread.Sleep(50);
    }

    // Flush the key press
    if (Console.KeyAvailable) Console.ReadKey(true);
    Console.WriteLine("  Stopped watching.");
}

/// <summary>
/// Scans the INVENTORY_ITEM global table sequentially from Pistols.
/// Reads object_id at each stride to identify all items.
/// </summary>
static void ScanInventoryItemTable(ProcessMemory memory, IntPtr t1, IntPtr pistolsPtr)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\n  Scanning INVENTORY_ITEM table from Pistols (0x{pistolsPtr:X})...");
    Console.WriteLine($"  Stride: 0x{TR1RMemoryMap.InventoryItemStride:X} ({TR1RMemoryMap.InventoryItemStride} bytes)");
    Console.ResetColor();

    // Scan backwards (negative indices) and forwards
    int scanMin = -20;
    int scanMax = 30;

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"\n  {"RelIdx",7} | {"Address",18} | {"ObjId",8} | {"Name",-30}");
    Console.WriteLine($"  {new string('-', 7)}-+-{new string('-', 18)}-+-{new string('-', 8)}-+-{new string('-', 30)}");
    Console.ResetColor();

    for (int relIdx = scanMin; relIdx <= scanMax; relIdx++)
    {
        IntPtr itemAddr = pistolsPtr + relIdx * TR1RMemoryMap.InventoryItemStride;
        short objectId;

        try
        {
            objectId = memory.ReadInt16(itemAddr + TR1RMemoryMap.InvItem_ObjectId);
        }
        catch
        {
            continue; // unreadable memory
        }

        // Skip if clearly invalid (0 or very high)
        if (objectId == 0 && relIdx != 0) continue;

        string name = TR1RMemoryMap.ObjIdNames.GetValueOrDefault(objectId, $"?");
        bool isKnown = TR1RMemoryMap.ObjIdNames.ContainsKey(objectId);
        bool isKeyItem = objectId >= TR1RMemoryMap.ObjId.Key1 && objectId <= TR1RMemoryMap.ObjId.LeadBar;

        if (isKeyItem)
            Console.ForegroundColor = ConsoleColor.Yellow;
        else if (isKnown)
            Console.ForegroundColor = ConsoleColor.Green;
        else if (objectId != 0)
            Console.ForegroundColor = ConsoleColor.DarkGray;
        else
            Console.ForegroundColor = ConsoleColor.White;

        string marker = relIdx == 0 ? " <-- PISTOLS" : isKeyItem ? " <-- KEY ITEM" : "";
        Console.WriteLine($"  {relIdx,7} | 0x{itemAddr:X} | 0x{objectId:X4}   | {name,-30}{marker}");
        Console.ResetColor();
    }

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n  Key items found above can be used for injection.");
    Console.WriteLine("  Add their relIdx to InvItemRelIndex in TR1RMemoryMap.cs.");
    Console.ResetColor();
}

/// <summary>
/// Scans memory around the estimated Keys Ring qtys offset
/// to find where qty values change when a key item is picked up.
/// </summary>
static void ScanKeysRingQtys(ProcessMemory memory, IntPtr t1, IntPtr pistolsPtr)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n  === Keys Ring Qtys Scanner ===");
    Console.WriteLine("  This will snapshot memory, then detect changes when you pick up a key item.");
    Console.WriteLine("  Scanning range: tomb1.dll+0xF8E00..+0xFD800 (broad search)");
    Console.ResetColor();

    // Take multiple scan regions
    var scanRegions = new (int offset, int size)[]
    {
        (0xF8E00, 0x1000),  // near main ring qtys
        (0xF9600, 0x200),   // near estimated keys ring qtys
        (0xFD600, 0x200),   // near keys ring count
    };

    Console.Write("  Press ENTER to take baseline snapshot...");
    Console.ReadLine();

    short baseCount = memory.ReadInt16(t1 + TR1RMemoryMap.KeysRingCount);
    var baselines = new Dictionary<int, byte[]>();
    foreach (var (offset, size) in scanRegions)
        baselines[offset] = memory.ReadBytes(t1 + offset, size);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  Baseline captured. Keys Ring count = {baseCount}");
    Console.ResetColor();
    Console.Write("  Now PICK UP a key item, then press ENTER...");
    Console.ReadLine();

    short newCount = memory.ReadInt16(t1 + TR1RMemoryMap.KeysRingCount);
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"\n  Keys Ring count: {baseCount} -> {newCount}");
    Console.ResetColor();

    foreach (var (offset, size) in scanRegions)
    {
        byte[] current = memory.ReadBytes(t1 + offset, size);
        byte[] baseline = baselines[offset];

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"\n  Region tomb1.dll+0x{offset:X}..+0x{offset + size:X}:");
        Console.ResetColor();

        int changes = 0;
        for (int i = 0; i < size; i++)
        {
            if (baseline[i] != current[i])
            {
                int absOffset = offset + i;
                string interp = "";
                if (i + 1 < size)
                {
                    short oldI16 = BitConverter.ToInt16(baseline, i);
                    short newI16 = BitConverter.ToInt16(current, i);
                    interp = $"  Int16: {oldI16} -> {newI16}";
                    // Highlight if this looks like a qty (0->1 or increment)
                    if (oldI16 == 0 && newI16 == 1)
                        interp += "  <<< LIKELY QTY!";
                }
                Console.Write($"    +0x{absOffset:X6}: ");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"0x{baseline[i]:X2}");
                Console.ResetColor();
                Console.Write(" -> ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"0x{current[i]:X2}");
                Console.ResetColor();
                Console.WriteLine(interp);
                changes++;
            }
        }

        if (changes == 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("    (no changes)");
            Console.ResetColor();
        }
    }
}

/// <summary>
/// Injects a key item into the Keys Ring by appending it.
/// </summary>
static void InjectKeyItem(ProcessMemory memory, IntPtr t1, IntPtr pistolsPtr)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n  === Inject Key Item ===");
    Console.ResetColor();

    // Show current Keys Ring
    ShowKeysRingState(memory, t1, pistolsPtr);

    Console.Write("\n  Enter the relIdx of the key item to inject (from table scan): ");
    string? input = Console.ReadLine()?.Trim();
    if (!int.TryParse(input, out int relIdx))
    {
        Console.WriteLine("  Invalid number.");
        return;
    }

    IntPtr targetPtr = pistolsPtr + relIdx * TR1RMemoryMap.InventoryItemStride;
    short objectId = memory.ReadInt16(targetPtr + TR1RMemoryMap.InvItem_ObjectId);
    string name = TR1RMemoryMap.ObjIdNames.GetValueOrDefault(objectId, "Unknown");

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  Target: relIdx={relIdx}, ptr=0x{targetPtr:X}, objId=0x{objectId:X4} ({name})");
    Console.ResetColor();

    Console.Write("  Confirm injection? (y/n): ");
    if (Console.ReadLine()?.Trim().ToLower() != "y")
    {
        Console.WriteLine("  Cancelled.");
        return;
    }

    short keyCount = memory.ReadInt16(t1 + TR1RMemoryMap.KeysRingCount);

    // Check if already in ring
    for (int i = 0; i < keyCount; i++)
    {
        IntPtr existingPtr = memory.ReadPointer(t1 + TR1RMemoryMap.KeysRingItems + i * 8);
        if (existingPtr == targetPtr)
        {
            // Increment qty
            IntPtr qtyAddr = t1 + TR1RMemoryMap.KeysRingQtys + i * 2;
            short currentQty = memory.ReadInt16(qtyAddr);
            short newQty = (short)(currentQty + 1);
            memory.Write(qtyAddr, newQty);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  >>> {name} already in ring at [{i}], qty: {currentQty} -> {newQty}");
            Console.ResetColor();
            return;
        }
    }

    if (keyCount >= TR1RMemoryMap.MaxRingItems)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  Keys Ring is full!");
        Console.ResetColor();
        return;
    }

    // Append: write pointer, qty, increment count
    IntPtr newItemSlot = t1 + TR1RMemoryMap.KeysRingItems + keyCount * 8;
    memory.Write(newItemSlot, targetPtr.ToInt64());

    IntPtr newQtySlot = t1 + TR1RMemoryMap.KeysRingQtys + keyCount * 2;
    memory.Write(newQtySlot, (short)1);

    short newCount = (short)(keyCount + 1);
    memory.Write(t1 + TR1RMemoryMap.KeysRingCount, newCount);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  >>> Injected {name} at ring index {keyCount}, qty=1");
    Console.WriteLine($"  >>> Keys Ring count: {keyCount} -> {newCount}");
    Console.ResetColor();

    // Verify
    IntPtr verifyPtr = memory.ReadPointer(newItemSlot);
    short verifyQty = memory.ReadInt16(newQtySlot);
    short verifyCount = memory.ReadInt16(t1 + TR1RMemoryMap.KeysRingCount);
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  Verify: ptr=0x{verifyPtr:X} qty={verifyQty} count={verifyCount}");
    Console.ResetColor();

    // Show updated state
    ShowKeysRingState(memory, t1, pistolsPtr);
}
