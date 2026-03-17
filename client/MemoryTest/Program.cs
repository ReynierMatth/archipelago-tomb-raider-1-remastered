using TRArchipelagoClient.GameInterface;

Console.Title = "TRR Memory Test";
Console.WriteLine("=== TR Remastered - Memory Reader Test ===");
Console.WriteLine("Launch tomb123.exe and start playing.\n");

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

Console.WriteLine("\nChoose mode:");
Console.WriteLine("  --- TR1 (tomb1.dll) ---");
Console.WriteLine("  1 = Live monitoring (HP, pos, level, entities)");
Console.WriteLine("  2 = Inventory scanner (find live ammo/weapon offsets)");
Console.WriteLine("  3 = Entity flag watcher (test pickup detection)");
Console.WriteLine("  4 = Entity struct dumper (find real struct layout)");
Console.WriteLine("  5 = Inventory ring tester (add items to inventory)");
Console.WriteLine("  6 = Keys ring explorer (discover key item offsets)");
Console.WriteLine("  --- TR2 (tomb2.dll) ---");
Console.WriteLine("  8 = TR2 Inventory ring tester (add items to TR2 inventory)");
Console.WriteLine("  --- Multi-Game ---");
Console.WriteLine("  7 = Inventory item table scanner (TR1/TR2/TR3)");
Console.Write("\nMode: ");
string mode = Console.ReadLine()?.Trim() ?? "1";

if (mode == "7")
{
    RunMultiGameInventoryScanner(memory);
}
else if (mode == "8")
{
    // TR2 mode: wait for tomb2.dll
    Console.Write("Waiting for tomb2.dll...");
    while (memory.Tomb2Base == IntPtr.Zero)
    {
        memory.RefreshTomb1Base(); // also refreshes tomb2/tomb3
        if (memory.Tomb2Base == IntPtr.Zero)
        {
            Console.Write(".");
            Thread.Sleep(1000);
        }
    }
    IntPtr t2 = memory.Tomb2Base;
    Console.WriteLine($"\n  tomb2.dll base: 0x{t2:X}");
    RunTR2InventoryRingTester(memory, t2);
}
else
{
    // Modes 1-6: wait for tomb1.dll
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
            ushort secrets = memory.ReadUInt16(t1 + TR1RMemoryMap.WorldStateBackup + TR1RMemoryMap.Runtime_SecretsFound);

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

            int secretCount = 0; ushort sv = secrets;
            while (sv != 0) { secretCount += sv & 1; sv >>= 1; }

            if (secrets != lastSecrets)
            {
                int prevCount = 0; ushort pv = lastSecrets;
                while (pv != 0) { prevCount += pv & 1; pv >>= 1; }
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  *** SECRET FOUND! {prevCount} -> {secretCount} secrets ***");
                Console.ResetColor();
            }

            Console.Write($"\r  [{state}] {levelName,-25} HP:{health,5}/{TR1RMemoryMap.MaxHealth}" +
                          $"  Pos:({posX,7},{posY,7},{posZ,7}) Room:{room,3}" +
                          $"  Spd:{speed,4}  Ent:{entityCount,3}" +
                          $"  Secrets:{secretCount}" +
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
    // We'll scan the LARA_INFO region around tomb1.dll+0x32CE70
    // and also a wider area to find inventory data
    int laraInfoBase = 0x32CE70;
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

    // Item definitions: name, relative index from Compass, default qty
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

        // Find Compass pointer (always at index 0 in the ring)
        IntPtr compassPtr = FindCompassPointer(memory, t1);

        for (int i = 0; i < ringCount; i++)
        {
            IntPtr itemPtr = memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + i * 8);
            short qty = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingQtys + i * 2);

            // Read object_id from the INVENTORY_ITEM struct
            short objectId = (itemPtr != IntPtr.Zero) ? memory.ReadInt16(itemPtr + TR1RMemoryMap.InvItem_ObjectId) : (short)-1;

            // Identify the item by checking if its pointer matches known relative offsets
            string name = $"Unknown (objId=0x{objectId:X})";
            if (compassPtr != IntPtr.Zero)
            {
                long offset = itemPtr.ToInt64() - compassPtr.ToInt64();
                int relIdx = (int)(offset / TR1RMemoryMap.InventoryItemStride);
                if (offset % TR1RMemoryMap.InventoryItemStride == 0)
                    name = IdentifyItem(relIdx);
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

        if (compassPtr == IntPtr.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Cannot find Compass pointer! Make sure you're in a level.");
            Console.ResetColor();
            Console.WriteLine("Press ENTER to retry...");
            Console.ReadLine();
            continue;
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n  Compass reference: 0x{compassPtr:X}");
        Console.ResetColor();

        // Check which items already exist in ring
        var existingItems = new Dictionary<int, int>(); // relIndex -> ring position
        for (int i = 0; i < ringCount; i++)
        {
            IntPtr itemPtr = memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + i * 8);
            long diff = itemPtr.ToInt64() - compassPtr.ToInt64();
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
        Console.WriteLine($"  8 = Give ammo item (into ring, no weapon needed)");
        Console.WriteLine($"  9 = Give weapon + convert ammo items to LARA_INFO");
        Console.WriteLine($"  10 = Give weapon only (no ammo conversion)");
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
            GiveAmmoToRing(memory, t1, compassPtr);
            continue;
        }

        if (input == "9")
        {
            GiveWeaponWithConversion(memory, t1, compassPtr);
            continue;
        }

        if (input == "10")
        {
            GiveWeaponOnly(memory, t1, compassPtr);
            continue;
        }

        if (!int.TryParse(input, out int choice) || choice < 1 || choice > items.Length)
        {
            Console.WriteLine("Invalid choice.");
            continue;
        }

        var selected = items[choice - 1];
        IntPtr targetPtr = compassPtr + selected.RelIndex * TR1RMemoryMap.InventoryItemStride;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n  Target: {selected.Name}");
        Console.WriteLine($"  Pointer: 0x{targetPtr:X} (Compass + {selected.RelIndex} * 0x{TR1RMemoryMap.InventoryItemStride:X})");

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
        TR1RMemoryMap.InvItemRelIndex.ShotgunAmmo => "Shotgun Ammo",
        TR1RMemoryMap.InvItemRelIndex.MagnumAmmo => "Magnum Ammo",
        TR1RMemoryMap.InvItemRelIndex.UziAmmo => "Uzi Ammo",
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

static void GiveAmmoToRing(ProcessMemory memory, IntPtr t1, IntPtr compassPtr)
{
    Console.WriteLine("\n--- Give Ammo (inject into ring) ---");
    Console.WriteLine("  1 = Shotgun Ammo");
    Console.WriteLine("  2 = Magnum Ammo");
    Console.WriteLine("  3 = Uzi Ammo");
    Console.Write("Choice: ");
    string? input = Console.ReadLine()?.Trim();

    (int relIdx, string name) = input switch
    {
        "1" => (TR1RMemoryMap.InvItemRelIndex.ShotgunAmmo, "Shotgun Ammo"),
        "2" => (TR1RMemoryMap.InvItemRelIndex.MagnumAmmo, "Magnum Ammo"),
        "3" => (TR1RMemoryMap.InvItemRelIndex.UziAmmo, "Uzi Ammo"),
        _ => (int.MinValue, ""),
    };

    if (relIdx == int.MinValue) { Console.WriteLine("Invalid."); return; }

    IntPtr targetPtr = compassPtr + relIdx * TR1RMemoryMap.InventoryItemStride;
    short ringCount = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingCount);

    // Check if already in ring → increment qty
    for (int i = 0; i < ringCount; i++)
    {
        if (memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + i * 8) == targetPtr)
        {
            IntPtr qtyAddr = t1 + TR1RMemoryMap.MainRingQtys + i * 2;
            short qty = memory.ReadInt16(qtyAddr);
            memory.Write(qtyAddr, (short)(qty + 1));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  >>> {name} qty: {qty} -> {qty + 1}");
            Console.ResetColor();
            return;
        }
    }

    // Not in ring → append
    if (ringCount >= TR1RMemoryMap.MaxRingItems) { Console.WriteLine("  Ring full!"); return; }
    memory.Write(t1 + TR1RMemoryMap.MainRingItems + ringCount * 8, targetPtr.ToInt64());
    memory.Write(t1 + TR1RMemoryMap.MainRingQtys + ringCount * 2, (short)1);
    memory.Write(t1 + TR1RMemoryMap.MainRingCount, (short)(ringCount + 1));

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  >>> {name} injected at ring index {ringCount}, qty=1");
    Console.ResetColor();
}

static void GiveWeaponOnly(ProcessMemory memory, IntPtr t1, IntPtr compassPtr)
{
    Console.WriteLine("\n--- Give Weapon Only ---");
    Console.WriteLine("  1 = Shotgun");
    Console.WriteLine("  2 = Magnums");
    Console.WriteLine("  3 = Uzis");
    Console.Write("Choice: ");
    string? input = Console.ReadLine()?.Trim();

    (int relIdx, string name) = input switch
    {
        "1" => (TR1RMemoryMap.InvItemRelIndex.Shotgun, "Shotgun"),
        "2" => (TR1RMemoryMap.InvItemRelIndex.Magnums, "Magnums"),
        "3" => (TR1RMemoryMap.InvItemRelIndex.Uzis, "Uzis"),
        _ => (int.MinValue, ""),
    };

    if (relIdx == int.MinValue) { Console.WriteLine("Invalid."); return; }

    IntPtr weaponPtr = compassPtr + relIdx * TR1RMemoryMap.InventoryItemStride;
    short ringCount = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingCount);

    // Check if already in ring
    for (int i = 0; i < ringCount; i++)
    {
        if (memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + i * 8) == weaponPtr)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  {name} already in ring.");
            Console.ResetColor();
            return;
        }
    }

    if (ringCount >= TR1RMemoryMap.MaxRingItems) { Console.WriteLine("  Ring full!"); return; }
    memory.Write(t1 + TR1RMemoryMap.MainRingItems + ringCount * 8, weaponPtr.ToInt64());
    memory.Write(t1 + TR1RMemoryMap.MainRingQtys + ringCount * 2, (short)1);
    memory.Write(t1 + TR1RMemoryMap.MainRingCount, (short)(ringCount + 1));

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  >>> {name} injected at ring index {ringCount}");
    Console.ResetColor();
}

static void GiveWeaponWithConversion(ProcessMemory memory, IntPtr t1, IntPtr compassPtr)
{
    Console.WriteLine("\n--- Give Weapon (with ammo conversion) ---");
    Console.WriteLine("  1 = Shotgun");
    Console.WriteLine("  2 = Magnums");
    Console.WriteLine("  3 = Uzis");
    Console.Write("Choice: ");
    string? input = Console.ReadLine()?.Trim();

    (int weaponRelIdx, int ammoRelIdx, int laraAmmoOffset, int ammoPerPickup, string name) = input switch
    {
        "1" => (TR1RMemoryMap.InvItemRelIndex.Shotgun, TR1RMemoryMap.InvItemRelIndex.ShotgunAmmo,
            TR1RMemoryMap.Lara_ShotgunAmmo, 2 * TR1RMemoryMap.ShotgunAmmoMultiplier, "Shotgun"),
        "2" => (TR1RMemoryMap.InvItemRelIndex.Magnums, TR1RMemoryMap.InvItemRelIndex.MagnumAmmo,
            TR1RMemoryMap.Lara_MagnumAmmo, 50, "Magnums"),
        "3" => (TR1RMemoryMap.InvItemRelIndex.Uzis, TR1RMemoryMap.InvItemRelIndex.UziAmmo,
            TR1RMemoryMap.Lara_UziAmmo, 100, "Uzis"),
        _ => (int.MinValue, int.MinValue, -1, 0, ""),
    };

    if (laraAmmoOffset < 0) { Console.WriteLine("Invalid."); return; }

    // Step 1: Check if weapon already in ring
    IntPtr weaponPtr = compassPtr + weaponRelIdx * TR1RMemoryMap.InventoryItemStride;
    short ringCount = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingCount);
    bool hasWeapon = false;
    for (int i = 0; i < ringCount; i++)
    {
        if (memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + i * 8) == weaponPtr)
        { hasWeapon = true; break; }
    }

    if (hasWeapon)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  {name} already in ring, skipping weapon inject.");
        Console.ResetColor();
    }
    else
    {
        // Inject weapon
        ringCount = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingCount);
        memory.Write(t1 + TR1RMemoryMap.MainRingItems + ringCount * 8, weaponPtr.ToInt64());
        memory.Write(t1 + TR1RMemoryMap.MainRingQtys + ringCount * 2, (short)1);
        memory.Write(t1 + TR1RMemoryMap.MainRingCount, (short)(ringCount + 1));
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  >>> {name} injected at ring index {ringCount}");
        Console.ResetColor();
    }

    // Step 2: Find and remove ammo item from ring
    IntPtr ammoPtr = compassPtr + ammoRelIdx * TR1RMemoryMap.InventoryItemStride;
    ringCount = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingCount);
    int foundIdx = -1;
    short foundQty = 0;
    for (int i = 0; i < ringCount; i++)
    {
        if (memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + i * 8) == ammoPtr)
        {
            foundIdx = i;
            foundQty = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingQtys + i * 2);
            break;
        }
    }

    if (foundIdx >= 0)
    {
        // Shift items down
        for (int i = foundIdx; i < ringCount - 1; i++)
        {
            long nextPtr = memory.ReadInt64(t1 + TR1RMemoryMap.MainRingItems + (i + 1) * 8);
            memory.Write(t1 + TR1RMemoryMap.MainRingItems + i * 8, nextPtr);
            short nextQty = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingQtys + (i + 1) * 2);
            memory.Write(t1 + TR1RMemoryMap.MainRingQtys + i * 2, nextQty);
        }
        // Clear last slot, decrement count
        memory.Write(t1 + TR1RMemoryMap.MainRingItems + (ringCount - 1) * 8, 0L);
        memory.Write(t1 + TR1RMemoryMap.MainRingQtys + (ringCount - 1) * 2, (short)0);
        memory.Write(t1 + TR1RMemoryMap.MainRingCount, (short)(ringCount - 1));

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  >>> Removed ammo item from ring (was at index {foundIdx}, qty={foundQty})");
        Console.ResetColor();
    }
    else
    {
        foundQty = 0;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  No ammo item found in ring (nothing to convert).");
        Console.ResetColor();
    }

    // Step 3: Write LARA_INFO ammo = converted pickups + starting ammo
    IntPtr ammoAddr = t1 + laraAmmoOffset;
    int current = memory.ReadInt32(ammoAddr);
    int toAdd = (foundQty * ammoPerPickup) + ammoPerPickup;
    int newVal = Math.Min(current + toAdd, 999999);
    memory.Write(ammoAddr, newVal);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  >>> Ammo: {current} -> {newVal} ({foundQty} converted + starting ammo)");
    Console.ResetColor();
}

// ====================================================================
// MODE 6: Keys Ring Explorer - discover key item offsets
// ====================================================================
static void RunKeysRingExplorer(ProcessMemory memory, IntPtr t1)
{
    Console.WriteLine("\n=== KEYS RING EXPLORER ===");
    Console.WriteLine("Explore the Keys Ring and discover key item INVENTORY_ITEM offsets.\n");

    // First, find Compass pointer (reference for all relIdx calculations)
    IntPtr compassPtr = FindCompassPointer(memory, t1);
    if (compassPtr == IntPtr.Zero)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Cannot find Compass pointer! Make sure you're in a level.");
        Console.ResetColor();
        return;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  Compass reference: 0x{compassPtr:X}");
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

        // Re-find Compass in case of level change
        compassPtr = FindCompassPointer(memory, t1);
        if (compassPtr == IntPtr.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Lost Compass pointer. Are you still in a level?");
            Console.ResetColor();
            continue;
        }

        switch (input)
        {
            case "1":
                ShowKeysRingState(memory, t1, compassPtr);
                break;
            case "2":
                WatchKeyItemPickup(memory, t1, compassPtr);
                break;
            case "3":
                ScanInventoryItemTable(memory, t1, compassPtr);
                break;
            case "4":
                ScanKeysRingQtys(memory, t1, compassPtr);
                break;
            case "5":
                InjectKeyItem(memory, t1, compassPtr);
                break;
        }
    }
}

/// <summary>Finds the Compass INVENTORY_ITEM pointer from Main Ring items[0].</summary>
static IntPtr FindCompassPointer(ProcessMemory memory, IntPtr t1)
{
    short ringCount = memory.ReadInt16(t1 + TR1RMemoryMap.MainRingCount);
    if (ringCount < 1) return IntPtr.Zero;

    // Compass is always at index 0 in the Main Ring (lowest inv_pos)
    IntPtr item0 = memory.ReadPointer(t1 + TR1RMemoryMap.MainRingItems);
    if (item0 == IntPtr.Zero) return IntPtr.Zero;

    // Verify it's actually the Compass by checking its object_id
    short objId = memory.ReadInt16(item0 + TR1RMemoryMap.InvItem_ObjectId);
    if (objId == TR1RMemoryMap.InvObjId.Compass)
        return item0;

    return IntPtr.Zero;
}

/// <summary>Shows the current state of the Keys Ring.</summary>
static void ShowKeysRingState(ProcessMemory memory, IntPtr t1, IntPtr compassPtr)
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

        // Calculate relIdx from Compass
        long offset = itemPtr.ToInt64() - compassPtr.ToInt64();
        int relIdx = (offset % TR1RMemoryMap.InventoryItemStride == 0)
            ? (int)(offset / TR1RMemoryMap.InventoryItemStride)
            : 99999;

        string name = TR1RMemoryMap.InvObjIdNames.GetValueOrDefault(objectId, $"Unknown");

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
static void WatchKeyItemPickup(ProcessMemory memory, IntPtr t1, IntPtr compassPtr)
{
    short prevCount = memory.ReadInt16(t1 + TR1RMemoryMap.KeysRingCount);
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\n  Watching Keys Ring... Current count: {prevCount}");
    Console.WriteLine("  Go pick up a key item! Press any key to stop watching.\n");
    Console.ResetColor();

    // Also snapshot the qtys area (wider scan to find the real offset)
    int scanStart = 0x114EF0; // scan a wide range around estimated qtys
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

                long offset = newPtr.ToInt64() - compassPtr.ToInt64();
                int relIdx = (offset % TR1RMemoryMap.InventoryItemStride == 0)
                    ? (int)(offset / TR1RMemoryMap.InventoryItemStride)
                    : 99999;

                string name = TR1RMemoryMap.InvObjIdNames.GetValueOrDefault(objectId, "Unknown");

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  New item at ring index {newIdx}:");
                Console.WriteLine($"    Pointer:   0x{newPtr:X}");
                Console.WriteLine($"    Object ID: 0x{objectId:X4} ({name})");
                Console.WriteLine($"    RelIdx:    {relIdx} (from Compass)");
                Console.WriteLine($"    Formula:   compassPtr + {relIdx} * 0x{TR1RMemoryMap.InventoryItemStride:X}");
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
/// Scans the INVENTORY_ITEM global table sequentially from Compass.
/// Reads object_id at each stride to identify all items.
/// </summary>
static void ScanInventoryItemTable(ProcessMemory memory, IntPtr t1, IntPtr compassPtr)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\n  Scanning INVENTORY_ITEM table from Compass (0x{compassPtr:X})...");
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
        IntPtr itemAddr = compassPtr + relIdx * TR1RMemoryMap.InventoryItemStride;
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

        string name = TR1RMemoryMap.InvObjIdNames.GetValueOrDefault(objectId, $"?");
        bool isKnown = TR1RMemoryMap.InvObjIdNames.ContainsKey(objectId);
        bool isKeyItem = objectId == TR1RMemoryMap.InvObjId.Key1 || objectId == TR1RMemoryMap.InvObjId.Key2
            || objectId == TR1RMemoryMap.InvObjId.Key3 || objectId == TR1RMemoryMap.InvObjId.Key4
            || objectId == TR1RMemoryMap.InvObjId.Puzzle1 || objectId == TR1RMemoryMap.InvObjId.Puzzle2
            || objectId == TR1RMemoryMap.InvObjId.Puzzle3 || objectId == TR1RMemoryMap.InvObjId.Puzzle4;

        if (isKeyItem)
            Console.ForegroundColor = ConsoleColor.Yellow;
        else if (isKnown)
            Console.ForegroundColor = ConsoleColor.Green;
        else if (objectId != 0)
            Console.ForegroundColor = ConsoleColor.DarkGray;
        else
            Console.ForegroundColor = ConsoleColor.White;

        string marker = relIdx == 0 ? " <-- COMPASS" : isKeyItem ? " <-- KEY ITEM" : "";
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
static void ScanKeysRingQtys(ProcessMemory memory, IntPtr t1, IntPtr compassPtr)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n  === Keys Ring Qtys Scanner ===");
    Console.WriteLine("  This will snapshot memory, then detect changes when you pick up a key item.");
    Console.WriteLine("  Scanning range: tomb1.dll+0x114DF0..+0x1197F0 (broad search)");
    Console.ResetColor();

    // Take multiple scan regions
    var scanRegions = new (int offset, int size)[]
    {
        (0x114DF0, 0x1000),  // near main ring qtys
        (0x1155F0, 0x200),   // near estimated keys ring qtys
        (0x1195F0, 0x200),   // near keys ring count
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
static void InjectKeyItem(ProcessMemory memory, IntPtr t1, IntPtr compassPtr)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n  === Inject Key Item ===");
    Console.ResetColor();

    // Show current Keys Ring
    ShowKeysRingState(memory, t1, compassPtr);

    Console.Write("\n  Enter the relIdx of the key item to inject (from table scan): ");
    string? input = Console.ReadLine()?.Trim();
    if (!int.TryParse(input, out int relIdx))
    {
        Console.WriteLine("  Invalid number.");
        return;
    }

    IntPtr targetPtr = compassPtr + relIdx * TR1RMemoryMap.InventoryItemStride;
    short objectId = memory.ReadInt16(targetPtr + TR1RMemoryMap.InvItem_ObjectId);
    string name = TR1RMemoryMap.InvObjIdNames.GetValueOrDefault(objectId, "Unknown");

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
    ShowKeysRingState(memory, t1, compassPtr);
}

// ====================================================================
// MODE 7: Multi-Game Inventory Item Table Scanner
// Detects which game DLL is loaded (tomb1/2/3), reads the Main Ring,
// uses the first item (Statistiques/Compass) as anchor, and scans the
// INVENTORY_ITEM table at 0xCD0 stride to identify all items by object_id.
// ====================================================================
static void RunMultiGameInventoryScanner(ProcessMemory memory)
{
    Console.WriteLine("\n=== MULTI-GAME INVENTORY ITEM TABLE SCANNER ===");
    Console.WriteLine("Detects active game DLL and scans all INVENTORY_ITEM structs.\n");

    while (memory.IsAttached)
    {
        // Detect which game is ACTIVE by checking IsInGameScene > 0
        // All 3 DLLs are loaded simultaneously; only one has IsInGameScene > 0.
        // Fallback: manual selection if auto-detection picks wrong game.
        memory.RefreshModuleBases();

        string gameName;
        IntPtr dllBase;
        int mainRingCountOffset, mainRingItemsOffset, mainRingQtysOffset;
        int invItemStride, invItemObjIdOffset;
        Dictionary<int, string>? objIdNames = null;

        bool isT1Active = memory.Tomb1Base != IntPtr.Zero
            && memory.ReadInt32(memory.Tomb1Base + TR1RMemoryMap.IsInGameScene) > 0;
        bool isT2Active = memory.Tomb2Base != IntPtr.Zero
            && memory.ReadInt32(memory.Tomb2Base + TR2RMemoryMap.IsInGameScene) > 0;
        bool isT3Active = memory.Tomb3Base != IntPtr.Zero
            && memory.ReadInt32(memory.Tomb3Base + TR3RMemoryMap.IsInGameScene) > 0;

        int detected = isT2Active ? 2 : isT1Active ? 1 : isT3Active ? 3 : 0;

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [Auto-detect: T1={isT1Active}, T2={isT2Active}, T3={isT3Active}]");
        Console.ResetColor();

        if (detected == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  No active game detected. Manual selection:");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine($"  Auto-detected: TR{detected}. Press ENTER to confirm, or type 1/2/3 to override:");
        }
        Console.Write("  Game [" + (detected == 0 ? "?" : detected.ToString()) + "]: ");
        string? gameInput = Console.ReadLine()?.Trim();
        int gameChoice = string.IsNullOrEmpty(gameInput) ? detected : int.TryParse(gameInput, out int g) ? g : detected;

        if (gameChoice == 1 && memory.Tomb1Base != IntPtr.Zero)
        {
            gameName = "TR1 (tomb1.dll)";
            dllBase = memory.Tomb1Base;
            mainRingCountOffset = TR1RMemoryMap.MainRingCount;
            mainRingItemsOffset = TR1RMemoryMap.MainRingItems;
            mainRingQtysOffset = TR1RMemoryMap.MainRingQtys;
            invItemStride = TR1RMemoryMap.InventoryItemStride;
            invItemObjIdOffset = TR1RMemoryMap.InvItem_ObjectId;
            objIdNames = TR1RMemoryMap.InvObjIdNames;
        }
        else if (gameChoice == 2 && memory.Tomb2Base != IntPtr.Zero)
        {
            gameName = "TR2 (tomb2.dll)";
            dllBase = memory.Tomb2Base;
            mainRingCountOffset = TR2RMemoryMap.MainRingCount;
            mainRingItemsOffset = TR2RMemoryMap.MainRingItems;
            mainRingQtysOffset = TR2RMemoryMap.MainRingQtys;
            invItemStride = TR2RMemoryMap.InventoryItemStride;
            invItemObjIdOffset = TR2RMemoryMap.InvItem_ObjectId;
            objIdNames = TR2RMemoryMap.InvObjIdNames;
        }
        else if (gameChoice == 3 && memory.Tomb3Base != IntPtr.Zero)
        {
            gameName = "TR3 (tomb3.dll)";
            dllBase = memory.Tomb3Base;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  TR3 Main Ring offsets are not yet mapped.");
            Console.ResetColor();
            Console.Write("\nPress ENTER to re-scan...");
            Console.ReadLine();
            continue;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Invalid selection or DLL not loaded.");
            Console.ResetColor();
            continue;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  Detected: {gameName}");
        Console.WriteLine($"  DLL base: 0x{dllBase:X}");
        Console.ResetColor();

        // Read ring state
        short ringCount = memory.ReadInt16(dllBase + mainRingCountOffset);
        Console.WriteLine($"  Main Ring count: {ringCount}");

        if (ringCount <= 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Ring is empty. Get into a level first.");
            Console.ResetColor();
            Console.Write("\nPress ENTER to re-scan...");
            Console.ReadLine();
            continue;
        }

        // Display current ring contents
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n  --- Current Main Ring ({ringCount} items) ---");
        Console.ResetColor();

        IntPtr anchorPtr = IntPtr.Zero; // first item in ring = Statistiques/Compass
        var ringPointers = new IntPtr[ringCount];

        for (int i = 0; i < ringCount; i++)
        {
            IntPtr itemPtr = memory.ReadPointer(dllBase + mainRingItemsOffset + i * 8);
            short qty = memory.ReadInt16(dllBase + mainRingQtysOffset + i * 2);
            short objectId = (itemPtr != IntPtr.Zero) ? memory.ReadInt16(itemPtr + invItemObjIdOffset) : (short)-1;

            ringPointers[i] = itemPtr;
            if (i == 0) anchorPtr = itemPtr;

            string relInfo = "";
            if (anchorPtr != IntPtr.Zero && i > 0)
            {
                long diff = itemPtr.ToInt64() - anchorPtr.ToInt64();
                if (diff % invItemStride == 0)
                    relInfo = $"  N={diff / invItemStride}";
                else
                    relInfo = $"  offset=0x{diff:X} (NOT stride-aligned!)";
            }
            else if (i == 0)
            {
                relInfo = "  <-- ANCHOR (ring[0])";
            }

            string itemName = objIdNames?.GetValueOrDefault(objectId, "") ?? "";
            Console.Write($"  [{i,2}] ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"0x{itemPtr:X}");
            Console.ResetColor();
            Console.Write($"  objId=0x{objectId:X4}  qty={qty,3}");
            if (!string.IsNullOrEmpty(itemName))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"  {itemName}");
            }
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(relInfo);
            Console.ResetColor();
        }

        if (anchorPtr == IntPtr.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Cannot read anchor pointer (ring[0] is null).");
            Console.ResetColor();
            Console.Write("\nPress ENTER to re-scan...");
            Console.ReadLine();
            continue;
        }

        // Menu
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n  --- Actions ---");
        Console.ResetColor();
        Console.WriteLine("  1 = Full table scan (scan INVENTORY_ITEM table around anchor)");
        Console.WriteLine("  2 = Inject item by N offset (anchor + N * stride)");
        Console.WriteLine("  3 = Change qty of existing ring item");
        Console.WriteLine("  4 = Live ring monitor (watch for changes)");
        Console.WriteLine("  5 = Inject item by name");
        Console.WriteLine("  0 = Refresh / re-read");
        Console.WriteLine("  q = Quit");
        Console.Write("\n  Choice: ");
        string? input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input) || input == "0") continue;
        if (input.ToLower() == "q") break;

        switch (input)
        {
            case "1":
                ScanItemTable(memory, anchorPtr, invItemStride, invItemObjIdOffset, objIdNames);
                break;
            case "2":
                InjectByOffset(memory, dllBase, anchorPtr, invItemStride, invItemObjIdOffset,
                    mainRingCountOffset, mainRingItemsOffset, mainRingQtysOffset);
                break;
            case "3":
                ChangeRingQty(memory, dllBase, ringCount, mainRingItemsOffset, mainRingQtysOffset,
                    invItemStride, invItemObjIdOffset, anchorPtr);
                break;
            case "4":
                LiveRingMonitor(memory, dllBase, mainRingCountOffset, mainRingItemsOffset,
                    mainRingQtysOffset, invItemStride, invItemObjIdOffset);
                break;
            case "5":
                InjectByName(memory, dllBase, anchorPtr, invItemStride, invItemObjIdOffset,
                    mainRingCountOffset, mainRingItemsOffset, mainRingQtysOffset, objIdNames);
                break;
        }
    }
}

static void InjectByName(ProcessMemory memory, IntPtr dllBase, IntPtr anchorPtr,
    int stride, int objIdOffset,
    int ringCountOffset, int ringItemsOffset, int ringQtysOffset,
    Dictionary<int, string>? objIdNames)
{
    if (objIdNames == null || objIdNames.Count == 0)
    {
        Console.WriteLine("  No item names available for this game.");
        return;
    }

    // List available items
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n  --- Available Items ---");
    Console.ResetColor();

    var itemList = objIdNames.OrderBy(kv => kv.Key).ToList();
    for (int i = 0; i < itemList.Count; i++)
    {
        Console.WriteLine($"  {i + 1,3} = {itemList[i].Value} (0x{itemList[i].Key:X2})");
    }

    Console.Write($"\n  Choose item (1-{itemList.Count}): ");
    if (!int.TryParse(Console.ReadLine()?.Trim(), out int choice) || choice < 1 || choice > itemList.Count)
    {
        Console.WriteLine("  Invalid choice.");
        return;
    }

    int targetObjId = itemList[choice - 1].Key;
    string targetName = itemList[choice - 1].Value;

    Console.Write("  Qty (default 1): ");
    string? qtyInput = Console.ReadLine()?.Trim();
    short qty = 1;
    if (!string.IsNullOrEmpty(qtyInput)) short.TryParse(qtyInput, out qty);

    // First try stride-aligned
    for (int n = -30; n <= 30; n++)
    {
        IntPtr itemAddr = anchorPtr + n * stride;
        try
        {
            short objId = memory.ReadInt16(itemAddr + objIdOffset);
            if (objId == targetObjId)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  Found {targetName} at N={n} (stride-aligned)");
                Console.ResetColor();
                InjectPtrToRing(memory, dllBase, itemAddr, qty, ringCountOffset, ringItemsOffset, ringQtysOffset);
                return;
            }
        }
        catch { }
    }

    // Check known non-stride byte offsets (TR2 specific)
    if (TR2RMemoryMap.NonStrideByteOffsets.TryGetValue(targetObjId, out int byteOffset))
    {
        IntPtr itemAddr = anchorPtr + byteOffset;
        short verifyId = memory.ReadInt16(itemAddr + objIdOffset);
        if (verifyId == targetObjId)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Found {targetName} at known offset +0x{byteOffset:X}");
            Console.ResetColor();
            InjectPtrToRing(memory, dllBase, itemAddr, qty, ringCountOffset, ringItemsOffset, ringQtysOffset);
            return;
        }
    }

    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"  {targetName} not found in memory.");
    Console.ResetColor();
}

static void InjectPtrToRing(ProcessMemory memory, IntPtr dllBase, IntPtr targetPtr, short qty,
    int ringCountOffset, int ringItemsOffset, int ringQtysOffset)
{
    short ringCount = memory.ReadInt16(dllBase + ringCountOffset);

    // Check if already in ring
    for (int i = 0; i < ringCount; i++)
    {
        if (memory.ReadPointer(dllBase + ringItemsOffset + i * 8) == targetPtr)
        {
            IntPtr qtyAddr = dllBase + ringQtysOffset + i * 2;
            short currentQty = memory.ReadInt16(qtyAddr);
            memory.Write(qtyAddr, (short)(currentQty + qty));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  >>> Already in ring [{i}], qty: {currentQty} -> {currentQty + qty}");
            Console.ResetColor();
            return;
        }
    }

    if (ringCount >= 24)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  Ring full!");
        Console.ResetColor();
        return;
    }

    memory.Write(dllBase + ringItemsOffset + ringCount * 8, targetPtr.ToInt64());
    memory.Write(dllBase + ringQtysOffset + ringCount * 2, qty);
    memory.Write(dllBase + ringCountOffset, (short)(ringCount + 1));

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  >>> Injected at ring index {ringCount}, qty={qty}");
    Console.ResetColor();
}

/// <summary>
/// Scans the INVENTORY_ITEM static table in DLL memory.
/// Uses the anchor pointer (ring[0], typically Statistiques/Compass) as reference.
/// Reads object_id at each stride position to build the full item table.
/// </summary>
static void ScanItemTable(ProcessMemory memory, IntPtr anchorPtr, int stride, int objIdOffset, Dictionary<int, string>? objIdNames = null)
{
    Console.Write("\n  Scan range (negative to positive N)? Default: -30 to +30\n");
    Console.Write("  Min N [-30]: ");
    string? minInput = Console.ReadLine()?.Trim();
    int scanMin = string.IsNullOrEmpty(minInput) ? -30 : int.Parse(minInput);

    Console.Write("  Max N [+30]: ");
    string? maxInput = Console.ReadLine()?.Trim();
    int scanMax = string.IsNullOrEmpty(maxInput) ? 30 : int.Parse(maxInput);

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\n  Scanning INVENTORY_ITEM table from anchor (0x{anchorPtr:X})...");
    Console.WriteLine($"  Stride: 0x{stride:X} ({stride} bytes), range N={scanMin}..{scanMax}");
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"\n  {"N",5} | {"Address",18} | {"ObjId",8} | {"Dec",5}");
    Console.WriteLine($"  {new string('-', 5)}-+-{new string('-', 18)}-+-{new string('-', 8)}-+-{new string('-', 5)}");
    Console.ResetColor();

    int validCount = 0;
    for (int n = scanMin; n <= scanMax; n++)
    {
        IntPtr itemAddr = anchorPtr + n * stride;
        short objectId;

        try
        {
            byte[] test = memory.ReadBytes(itemAddr, 2);
            objectId = memory.ReadInt16(itemAddr + objIdOffset);
        }
        catch
        {
            continue;
        }

        // Show all entries, highlight interesting ones
        bool isZero = objectId == 0;
        bool isNegative = objectId < 0;
        bool isHighValue = objectId > 1000;

        if (n == 0)
            Console.ForegroundColor = ConsoleColor.Cyan;
        else if (isZero)
            Console.ForegroundColor = ConsoleColor.DarkGray;
        else if (isNegative || isHighValue)
            Console.ForegroundColor = ConsoleColor.DarkGray;
        else
            Console.ForegroundColor = ConsoleColor.Green;

        string scanItemName = objIdNames?.GetValueOrDefault(objectId, "") ?? "";
        string marker = n == 0 ? " <-- ANCHOR" : !string.IsNullOrEmpty(scanItemName) ? $"  {scanItemName}" : "";
        Console.WriteLine($"  {n,5} | 0x{itemAddr:X} | 0x{objectId:X4}   | {objectId,5}{marker}");
        Console.ResetColor();

        if (!isZero && !isNegative && objectId < 1000)
            validCount++;
    }

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\n  Found {validCount} entries with plausible object_ids (1-999).");
    Console.WriteLine("  Use option 2 to inject items by their N offset.");
    Console.ResetColor();
}

/// <summary>
/// Injects an item into the Main Ring by specifying its N offset from anchor.
/// target_ptr = anchor_ptr + N * stride
/// </summary>
static void InjectByOffset(ProcessMemory memory, IntPtr dllBase, IntPtr anchorPtr,
    int stride, int objIdOffset, int countOffset, int itemsOffset, int qtysOffset)
{
    Console.Write("\n  Enter N offset (from anchor): ");
    if (!int.TryParse(Console.ReadLine()?.Trim(), out int n))
    {
        Console.WriteLine("  Invalid number.");
        return;
    }

    IntPtr targetPtr = anchorPtr + n * stride;
    short objectId = memory.ReadInt16(targetPtr + objIdOffset);

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  Target: N={n}, ptr=0x{targetPtr:X}, objId=0x{objectId:X4} ({objectId})");
    Console.ResetColor();

    // Check if already in ring
    short ringCount = memory.ReadInt16(dllBase + countOffset);
    for (int i = 0; i < ringCount; i++)
    {
        IntPtr existingPtr = memory.ReadPointer(dllBase + itemsOffset + i * 8);
        if (existingPtr == targetPtr)
        {
            // Increment qty
            IntPtr qtyAddr = dllBase + qtysOffset + i * 2;
            short currentQty = memory.ReadInt16(qtyAddr);
            short newQty = (short)(currentQty + 1);
            memory.Write(qtyAddr, newQty);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  >>> Already in ring at [{i}], qty: {currentQty} -> {newQty}");
            Console.ResetColor();
            return;
        }
    }

    if (ringCount >= 24)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  Ring is full (24 items)!");
        Console.ResetColor();
        return;
    }

    Console.Write("  Qty [1]: ");
    string? qtyInput = Console.ReadLine()?.Trim();
    short qty = string.IsNullOrEmpty(qtyInput) ? (short)1 : short.Parse(qtyInput);

    // Write pointer, qty, increment count
    memory.Write(dllBase + itemsOffset + ringCount * 8, targetPtr.ToInt64());
    memory.Write(dllBase + qtysOffset + ringCount * 2, qty);
    memory.Write(dllBase + countOffset, (short)(ringCount + 1));

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  >>> Injected at ring index {ringCount}, qty={qty}");
    Console.WriteLine($"  >>> Ring count: {ringCount} -> {ringCount + 1}");
    Console.ResetColor();
}

/// <summary>
/// Changes the quantity of an existing item in the ring.
/// </summary>
static void ChangeRingQty(ProcessMemory memory, IntPtr dllBase, short ringCount,
    int itemsOffset, int qtysOffset, int stride, int objIdOffset, IntPtr anchorPtr)
{
    Console.Write($"\n  Ring index to modify [0-{ringCount - 1}]: ");
    if (!int.TryParse(Console.ReadLine()?.Trim(), out int idx) || idx < 0 || idx >= ringCount)
    {
        Console.WriteLine("  Invalid index.");
        return;
    }

    IntPtr itemPtr = memory.ReadPointer(dllBase + itemsOffset + idx * 8);
    short objectId = memory.ReadInt16(itemPtr + objIdOffset);
    short currentQty = memory.ReadInt16(dllBase + qtysOffset + idx * 2);

    long diff = itemPtr.ToInt64() - anchorPtr.ToInt64();
    int n = (diff % stride == 0) ? (int)(diff / stride) : 99999;

    Console.WriteLine($"  Item [{idx}]: objId=0x{objectId:X4}, N={n}, current qty={currentQty}");
    Console.Write("  New qty: ");
    if (!short.TryParse(Console.ReadLine()?.Trim(), out short newQty))
    {
        Console.WriteLine("  Invalid.");
        return;
    }

    memory.Write(dllBase + qtysOffset + idx * 2, newQty);
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  >>> Qty: {currentQty} -> {newQty}");
    Console.ResetColor();
}

/// <summary>
/// Continuously monitors the Main Ring for changes (count, items, qtys).
/// Press any key to stop.
/// </summary>
static void LiveRingMonitor(ProcessMemory memory, IntPtr dllBase, int countOffset,
    int itemsOffset, int qtysOffset, int stride, int objIdOffset)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n  Live monitoring Main Ring... Press any key to stop.\n");
    Console.ResetColor();

    short prevCount = memory.ReadInt16(dllBase + countOffset);
    var prevItems = new long[24];
    var prevQtys = new short[24];
    for (int i = 0; i < prevCount; i++)
    {
        prevItems[i] = memory.ReadInt64(dllBase + itemsOffset + i * 8);
        prevQtys[i] = memory.ReadInt16(dllBase + qtysOffset + i * 2);
    }

    IntPtr anchor = (prevCount > 0) ? new IntPtr(prevItems[0]) : IntPtr.Zero;

    while (memory.IsAttached && !Console.KeyAvailable)
    {
        short count = memory.ReadInt16(dllBase + countOffset);

        if (count != prevCount)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] Ring count: {prevCount} -> {count}");
            Console.ResetColor();

            // Show new item if count increased
            if (count > prevCount && count <= 24)
            {
                for (int i = prevCount; i < count; i++)
                {
                    IntPtr ptr = memory.ReadPointer(dllBase + itemsOffset + i * 8);
                    short qty = memory.ReadInt16(dllBase + qtysOffset + i * 2);
                    short objId = (ptr != IntPtr.Zero) ? memory.ReadInt16(ptr + objIdOffset) : (short)-1;

                    string nInfo = "";
                    if (anchor != IntPtr.Zero)
                    {
                        long diff = ptr.ToInt64() - anchor.ToInt64();
                        if (diff % stride == 0) nInfo = $"N={diff / stride}";
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"    NEW [{i}] 0x{ptr:X}  objId=0x{objId:X4}  qty={qty}  {nInfo}");
                    Console.ResetColor();
                }
            }

            prevCount = count;
        }

        // Check qty changes
        for (int i = 0; i < Math.Min((int)count, 24); i++)
        {
            short qty = memory.ReadInt16(dllBase + qtysOffset + i * 2);
            if (i < prevQtys.Length && qty != prevQtys[i])
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] Ring[{i}] qty: {prevQtys[i]} -> {qty}");
                Console.ResetColor();
            }
            prevQtys[i] = qty;
        }

        // Update items snapshot
        for (int i = 0; i < Math.Min((int)count, 24); i++)
            prevItems[i] = memory.ReadInt64(dllBase + itemsOffset + i * 8);

        Thread.Sleep(100);
    }

    if (Console.KeyAvailable) Console.ReadKey(true);
    Console.WriteLine("  Stopped monitoring.");
}

// ====================================================================
// MODE 8: TR2 Inventory Ring Tester
// ====================================================================
static void RunTR2InventoryRingTester(ProcessMemory memory, IntPtr t2)
{
    Console.WriteLine("\n=== TR2 INVENTORY RING TESTER ===");
    Console.WriteLine("Add items directly to the TR2 main inventory ring via memory.\n");

    while (memory.IsAttached)
    {
        short ringCount = memory.ReadInt16(t2 + TR2RMemoryMap.MainRingCount);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n--- Current TR2 Main Ring ({ringCount} items) ---");
        Console.ResetColor();

        // Find Statistiques pointer (always at index 0, objId = 0x79)
        IntPtr statsPtr = IntPtr.Zero;
        if (ringCount >= 1)
        {
            IntPtr item0 = memory.ReadPointer(t2 + TR2RMemoryMap.MainRingItems);
            if (item0 != IntPtr.Zero)
            {
                short objId = memory.ReadInt16(item0 + TR2RMemoryMap.InvItem_ObjectId);
                if (objId == TR2RMemoryMap.InvObjId.Statistiques)
                    statsPtr = item0;
            }
        }

        // Display ring contents
        for (int i = 0; i < ringCount && i < TR2RMemoryMap.MaxRingItems; i++)
        {
            IntPtr itemPtr = memory.ReadPointer(t2 + TR2RMemoryMap.MainRingItems + i * 8);
            short qty = memory.ReadInt16(t2 + TR2RMemoryMap.MainRingQtys + i * 2);
            short objectId = (itemPtr != IntPtr.Zero) ? memory.ReadInt16(itemPtr + TR2RMemoryMap.InvItem_ObjectId) : (short)-1;

            string name = $"Unknown (objId=0x{objectId:X})";
            if (statsPtr != IntPtr.Zero)
            {
                long offset = itemPtr.ToInt64() - statsPtr.ToInt64();
                int relIdx = (offset % TR2RMemoryMap.InventoryItemStride == 0)
                    ? (int)(offset / TR2RMemoryMap.InventoryItemStride) : 99999;
                name = TR2IdentifyItem(objectId, relIdx);
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

        if (statsPtr == IntPtr.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Cannot find Statistiques pointer! Make sure you're in a TR2 level.");
            Console.ResetColor();
            Console.WriteLine("Press ENTER to retry...");
            Console.ReadLine();
            continue;
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n  Statistiques reference: 0x{statsPtr:X}");
        Console.ResetColor();

        // Menu
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n--- Actions ---");
        Console.ResetColor();
        Console.WriteLine("  1 = Inject item by relIdx");
        Console.WriteLine("  2 = Scan INVENTORY_ITEM table (find all items by objId)");
        Console.WriteLine("  3 = Inject item by objId (scan table first)");
        Console.WriteLine("  0 = Refresh / re-read ring");
        Console.WriteLine("  q = Quit");

        Console.Write("\nChoice: ");
        string? input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input) || input == "0")
            continue;
        if (input.ToLower() == "q")
            break;

        // Re-find Statistiques
        statsPtr = IntPtr.Zero;
        if (ringCount >= 1)
        {
            IntPtr item0 = memory.ReadPointer(t2 + TR2RMemoryMap.MainRingItems);
            if (item0 != IntPtr.Zero)
            {
                short objId = memory.ReadInt16(item0 + TR2RMemoryMap.InvItem_ObjectId);
                if (objId == TR2RMemoryMap.InvObjId.Statistiques)
                    statsPtr = item0;
            }
        }

        switch (input)
        {
            case "1":
                TR2InjectByRelIdx(memory, t2, statsPtr);
                break;
            case "2":
                TR2ScanInventoryTable(memory, t2, statsPtr);
                break;
            case "3":
                TR2InjectByObjId(memory, t2, statsPtr);
                break;
        }
    }
}

static string TR2IdentifyItem(short objectId, int relIdx)
{
    if (TR2RMemoryMap.InvObjIdNames.TryGetValue(objectId, out string? name))
        return name;
    return $"objId=0x{objectId:X} relIdx={relIdx}";
}

static void TR2InjectByRelIdx(ProcessMemory memory, IntPtr t2, IntPtr statsPtr)
{
    Console.Write("\n  Enter relIdx (from Statistiques): ");
    if (!int.TryParse(Console.ReadLine()?.Trim(), out int relIdx))
    {
        Console.WriteLine("  Invalid number.");
        return;
    }

    IntPtr targetPtr = statsPtr + relIdx * TR2RMemoryMap.InventoryItemStride;
    short objectId = memory.ReadInt16(targetPtr + TR2RMemoryMap.InvItem_ObjectId);

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  Target: relIdx={relIdx}, ptr=0x{targetPtr:X}, objId=0x{objectId:X4}");
    Console.ResetColor();

    Console.Write("  Qty to set (default 1): ");
    string? qtyInput = Console.ReadLine()?.Trim();
    short qty = 1;
    if (!string.IsNullOrEmpty(qtyInput)) short.TryParse(qtyInput, out qty);

    TR2InjectToRing(memory, t2, targetPtr, qty);
}

static void TR2InjectByObjId(ProcessMemory memory, IntPtr t2, IntPtr statsPtr)
{
    Console.Write("\n  Enter objId (hex, e.g. 9D for Pistols): ");
    string? input = Console.ReadLine()?.Trim();
    if (!int.TryParse(input, System.Globalization.NumberStyles.HexNumber, null, out int targetObjId))
    {
        Console.WriteLine("  Invalid hex number.");
        return;
    }

    // First try stride-aligned scan
    for (int relIdx = -30; relIdx <= 30; relIdx++)
    {
        IntPtr itemAddr = statsPtr + relIdx * TR2RMemoryMap.InventoryItemStride;
        try
        {
            short objId = memory.ReadInt16(itemAddr + TR2RMemoryMap.InvItem_ObjectId);
            if (objId == targetObjId)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  Found objId=0x{targetObjId:X} at relIdx={relIdx} (stride-aligned), ptr=0x{itemAddr:X}");
                Console.ResetColor();

                Console.Write("  Qty to set (default 1): ");
                string? qtyInput = Console.ReadLine()?.Trim();
                short qty = 1;
                if (!string.IsNullOrEmpty(qtyInput)) short.TryParse(qtyInput, out qty);

                TR2InjectToRing(memory, t2, itemAddr, qty);
                return;
            }
        }
        catch { /* unreadable memory */ }
    }

    // Check known non-stride byte offsets
    if (TR2RMemoryMap.NonStrideByteOffsets.TryGetValue(targetObjId, out int byteOffset))
    {
        IntPtr itemAddr = statsPtr + byteOffset;
        short verifyId = memory.ReadInt16(itemAddr + TR2RMemoryMap.InvItem_ObjectId);
        if (verifyId == targetObjId)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Found objId=0x{targetObjId:X} at known offset +0x{byteOffset:X}");
            Console.ResetColor();

            Console.Write("  Qty to set (default 1): ");
            string? qtyInput = Console.ReadLine()?.Trim();
            short qty = 1;
            if (!string.IsNullOrEmpty(qtyInput)) short.TryParse(qtyInput, out qty);

            TR2InjectToRing(memory, t2, itemAddr, qty);
            return;
        }
    }

    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"  objId=0x{targetObjId:X} not found.");
    Console.ResetColor();
}

static void TR2InjectToRing(ProcessMemory memory, IntPtr t2, IntPtr targetPtr, short qty)
{
    short ringCount = memory.ReadInt16(t2 + TR2RMemoryMap.MainRingCount);

    // Check if already in ring
    for (int i = 0; i < ringCount; i++)
    {
        if (memory.ReadPointer(t2 + TR2RMemoryMap.MainRingItems + i * 8) == targetPtr)
        {
            IntPtr qtyAddr = t2 + TR2RMemoryMap.MainRingQtys + i * 2;
            short currentQty = memory.ReadInt16(qtyAddr);
            memory.Write(qtyAddr, (short)(currentQty + qty));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  >>> Already in ring at [{i}], qty: {currentQty} -> {currentQty + qty}");
            Console.ResetColor();
            return;
        }
    }

    if (ringCount >= TR2RMemoryMap.MaxRingItems)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  Ring full!");
        Console.ResetColor();
        return;
    }

    memory.Write(t2 + TR2RMemoryMap.MainRingItems + ringCount * 8, targetPtr.ToInt64());
    memory.Write(t2 + TR2RMemoryMap.MainRingQtys + ringCount * 2, qty);
    memory.Write(t2 + TR2RMemoryMap.MainRingCount, (short)(ringCount + 1));

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  >>> Injected at ring index {ringCount}, qty={qty}");
    Console.ResetColor();
}

static void TR2ScanInventoryTable(ProcessMemory memory, IntPtr t2, IntPtr statsPtr)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\n  Scanning INVENTORY_ITEM table from Statistiques (0x{statsPtr:X})...");
    Console.WriteLine($"  Stride: 0x{TR2RMemoryMap.InventoryItemStride:X}");
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"\n  {"RelIdx",7} | {"Address",18} | {"ObjId",8} | {"Hex",6}");
    Console.WriteLine($"  {new string('-', 7)}-+-{new string('-', 18)}-+-{new string('-', 8)}-+-{new string('-', 6)}");
    Console.ResetColor();

    for (int relIdx = -20; relIdx <= 30; relIdx++)
    {
        IntPtr itemAddr = statsPtr + relIdx * TR2RMemoryMap.InventoryItemStride;
        short objectId;
        try
        {
            objectId = memory.ReadInt16(itemAddr + TR2RMemoryMap.InvItem_ObjectId);
        }
        catch { continue; }

        if (objectId <= 0 && relIdx != 0) continue;

        string marker = relIdx == 0 ? " <-- STATISTIQUES" : "";
        bool isKnown = objectId == TR2RMemoryMap.InvObjId.Statistiques
            || objectId == TR2RMemoryMap.InvObjId.Pistols
            || objectId == TR2RMemoryMap.InvObjId.Shotgun;

        Console.ForegroundColor = isKnown ? ConsoleColor.Green
            : objectId > 0 ? ConsoleColor.Yellow
            : ConsoleColor.White;

        Console.WriteLine($"  {relIdx,7} | 0x{itemAddr:X} | {objectId,5}   | 0x{objectId:X4}{marker}");
        Console.ResetColor();
    }
}
