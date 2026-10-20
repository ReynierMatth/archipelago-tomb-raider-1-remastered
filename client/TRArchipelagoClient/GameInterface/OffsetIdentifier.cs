using TRArchipelagoClient.UI;

namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Validates the compiled-in offset map against the live game and, when an
/// offset group no longer describes a coherent state, re-derives that group by
/// scanning memory and applying its internal invariants.
///
/// Nothing here blocks on user interaction — every discovery routine is
/// automatic so it can run on attach and again after a game patch.
///
/// Extension notes (what already existed and is being reused, not rebuilt):
///   - ProcessMemory.AOBScan / ReadBytes        — snapshot + pattern primitives
///   - Program.FindInventoryAddress             — INV_RING layout hypothesis
///   - Program.FindPistolsPointer               — Compass invariant (now shared)
///   - MemoryTest.ScanInventoryItemTable        — INVENTORY_ITEM stride walk
///   - InventoryScanner                         — value-space correlation
/// </summary>
public static class OffsetIdentifier
{
    /// <summary>Where the INVENTORY_ITEM table and ring structs live (tomb1.dll).</summary>
    private const int ScanStart = 0x1000;
    private const int ScanSize = 0x800000 - ScanStart;

    private const int Stride = TR1RMemoryMap.InventoryItemStride;

    // relIdx -> INVENTORY_ITEM object_id, verified as a group signature.
    private static readonly (int Rel, int ObjId)[] TableSignature =
    {
        (-8, TR1RMemoryMap.InvObjId.ShotgunAmmo),
        (-6, TR1RMemoryMap.InvObjId.SmallMedipack),
        (-2, TR1RMemoryMap.InvObjId.LargeMedipack),
        (0, TR1RMemoryMap.InvObjId.Pistols),
        (1, TR1RMemoryMap.InvObjId.Shotgun),
        (4, TR1RMemoryMap.InvObjId.MagnumAmmo),
        (6, TR1RMemoryMap.InvObjId.Compass),
        (9, TR1RMemoryMap.InvObjId.Uzis),
        (10, TR1RMemoryMap.InvObjId.UziAmmo),
        (12, TR1RMemoryMap.InvObjId.Magnums),
    };

    private const int RequiredSignatureHits = 8;

    /// <summary>
    /// Authoritative ring length. The ring arrays are NULL-terminated (the game
    /// itself compacts them and clears the tail), so we count populated slots.
    /// This is used instead of an unverified count-field offset everywhere the
    /// client reads or appends ring entries.
    /// </summary>
    public static short RingWalk(ProcessMemory m, int itemsOff)
    {
        IntPtr t1 = m.Tomb1Base;
        int n = 0;
        while (n < TR1RMemoryMap.MaxRingItems && m.ReadPointer(t1 + itemsOff + n * 8) != IntPtr.Zero)
            n++;
        return (short)n;
    }

    // =================================================================
    // SHARED HELPERS
    // =================================================================

    /// <summary>
    /// Locates the Pistols INVENTORY_ITEM by cross-referencing Main Ring entries:
    /// Compass sits at Pistols + Compass * stride. Returns Zero when the ring
    /// offsets cannot describe that relationship.
    /// </summary>
    public static IntPtr FindPistols(ProcessMemory m, IntPtr t1)
    {
        short ringCount = RingWalk(m, TR1RMemoryMap.MainRingItems);
        if (ringCount < 2 || ringCount > TR1RMemoryMap.MaxRingItems)
            return IntPtr.Zero;

        IntPtr item0 = m.ReadPointer(t1 + TR1RMemoryMap.MainRingItems);

        for (int i = 1; i < ringCount; i++)
        {
            IntPtr itemPtr = m.ReadPointer(t1 + TR1RMemoryMap.MainRingItems + i * 8);
            if (itemPtr == IntPtr.Zero) continue;

            long diff = item0.ToInt64() - itemPtr.ToInt64();
            if (diff == (long)TR1RMemoryMap.InvItemRelIndex.Compass * Stride)
                return itemPtr;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Evaluates a candidate main-ring layout against every invariant the client
    /// depends on. Used both to accept the compiled-in offsets and to accept a
    /// freshly scanned layout.
    /// </summary>
    public static bool TryEvaluateMainRing(ProcessMemory m, int countOff, int itemsOff, int qtysOff, out string reason)
    {
        reason = "";
        IntPtr t1 = m.Tomb1Base;
        if (t1 == IntPtr.Zero) { reason = "tomb1.dll not loaded"; return false; }

        short count = m.ReadInt16(t1 + countOff);
        if (count < 1) { reason = $"count at +0x{countOff:X} reads {count} (expected >= 1)"; return false; }
        if (count > TR1RMemoryMap.MaxRingItems) { reason = $"count at +0x{countOff:X} reads {count} (max {TR1RMemoryMap.MaxRingItems})"; return false; }

        // Resolve Pistols through the ring itself, then require every entry to
        // be a stride-aligned member of the global INVENTORY_ITEM table.
        IntPtr item0 = m.ReadPointer(t1 + itemsOff);
        if (item0 == IntPtr.Zero) { reason = $"items[0] at +0x{itemsOff:X} is NULL"; return false; }

        IntPtr pistols = IntPtr.Zero;
        for (int i = 1; i < count; i++)
        {
            IntPtr candidate = m.ReadPointer(t1 + itemsOff + i * 8);
            if (candidate != IntPtr.Zero && item0.ToInt64() - candidate.ToInt64() == (long)TR1RMemoryMap.InvItemRelIndex.Compass * Stride)
            {
                pistols = candidate;
                break;
            }
        }

        if (pistols == IntPtr.Zero)
        {
            reason = $"no entry satisfies items[0]-items[i] == {TR1RMemoryMap.InvItemRelIndex.Compass}*{Stride} (Compass invariant)";
            return false;
        }

        short pistolsId = m.ReadInt16(pistols + TR1RMemoryMap.InvItem_ObjectId);
        if (pistolsId != TR1RMemoryMap.InvObjId.Pistols)
        {
            reason = $"candidate Pistols object_id = 0x{pistolsId:X}, expected 0x{TR1RMemoryMap.InvObjId.Pistols:X}";
            return false;
        }

        short compassId = m.ReadInt16(item0 + TR1RMemoryMap.InvItem_ObjectId);
        if (compassId != TR1RMemoryMap.InvObjId.Compass)
        {
            reason = $"items[0] object_id = 0x{compassId:X}, expected Compass 0x{TR1RMemoryMap.InvObjId.Compass:X}";
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            IntPtr ptr = m.ReadPointer(t1 + itemsOff + i * 8);
            if (ptr == IntPtr.Zero) { reason = $"items[{i}] is NULL while count={count}"; return false; }

            long delta = ptr.ToInt64() - pistols.ToInt64();
            if (delta % Stride != 0) { reason = $"items[{i}] is not stride-aligned from Pistols"; return false; }

            int rel = (int)(delta / Stride);
            if (rel is < -40 or > 60) { reason = $"items[{i}] relIdx={rel} outside table window"; return false; }

            short qty = m.ReadInt16(t1 + qtysOff + i * 2);
            if (qty is < 0 or > 10_000) { reason = $"qtys[{i}] = {qty} (implausible) — qtys offset +0x{qtysOff:X} rejected"; return false; }
        }

        return true;
    }

    // =================================================================
    // GROUP IDENTIFICATION
    // =================================================================

    /// <summary>
    /// Attach-time pass: validates every group that can be checked before the
    /// player enters a level. The Ring group needs live gameplay state, so it is
    /// deferred until IdentifyRing is called from the monitor loop.
    /// </summary>
    public static void IdentifyOnAttach(ProcessMemory m, string? saveFile = null)
    {
        var report = OffsetModel.Report(OffsetGroup.Ring);
        report.State = OffsetState.Pending;

        ConsoleUI.Info($"[OFFSET] Build {OffsetModel.BuildHash}");

        foreach (var group in new[]
        {
            OffsetGroup.Exe, OffsetGroup.Core, OffsetGroup.Entities,
            OffsetGroup.Camera, OffsetGroup.Wsb, OffsetGroup.SaveSlot,
        })
        {
            ValidateOrQuarantine(m, group, saveFile, attemptResolve: false);
        }

        // Ring offsets are validated once gameplay state exists (see IdentifyRing).
        ConsoleUI.Info("[OFFSET] Ring group deferred until a level is loaded");
    }

    /// <summary>
    /// Validates (and if needed re-derives) the Main Ring / Keys Ring offsets.
    /// Safe to call on every level transition — a verified group only costs one
    /// invariant pass.
    /// </summary>
    public static bool IdentifyRing(ProcessMemory m, bool forceResolve = false)
    {
        var report = OffsetModel.Report(OffsetGroup.Ring);
        report.Attempts++;

        var failures = OffsetModel.Validate(m, OffsetGroup.Ring);
        if (failures.Count == 0)
        {
            if (report.State != OffsetState.Resolved)
                report.Reset(OffsetState.Verified);
            else
                report.Notes.Clear();
            return true;
        }

        if (report.State != OffsetState.Quarantined || forceResolve)
            ReportVersionDiff(OffsetGroup.Ring, failures);

        if (forceResolve || report.State != OffsetState.Quarantined)
        {
            if (TryResolveRing(m, out var applied, out var log))
            {
                report.Reset(OffsetState.Resolved);
                foreach (var line in log) report.Notes.Add(line);

                ConsoleUI.Success($"[OFFSET] Ring layout re-derived and verified (items was 0x{applied.Original:X}):");
                foreach (var (name, value) in OffsetModel.Snapshot(OffsetGroup.Ring))
                    ConsoleUI.Success($"          {name} = 0x{value:X}");
                if (!TR1RMemoryMap.MainRingCountConfirmed)
                    ConsoleUI.Info("[OFFSET] ring length is read by NULL-walk; the count field is not confirmed and is never written");

                OffsetModel.PrintManifestBlock(OffsetGroup.Ring);
                return true;
            }

            report.State = OffsetState.Quarantined;
            report.LastError = log.FirstOrDefault() ?? "ring scan produced no candidates";
            Quarantine(OffsetGroup.Ring, report.LastError);
            return false;
        }

        return false;
    }

    /// <summary>
    /// Re-runs the invariants for a non-Ring group (e.g. after WriteGate
    /// quarantined it, or while it is still Pending). A pass returns the group
    /// to Verified; a failure leaves it Pending for the next settle pass.
    /// </summary>
    public static bool Revalidate(ProcessMemory m, OffsetGroup group, string? saveFile = null)
    {
        ValidateOrQuarantine(m, group, saveFile, attemptResolve: false);
        return OffsetModel.Usable(group);
    }

    private static void ValidateOrQuarantine(ProcessMemory m, OffsetGroup group, string? saveFile, bool attemptResolve)
    {
        var report = OffsetModel.Report(group);
        report.Attempts++;

        var failures = OffsetModel.Validate(m, group, saveFile);
        if (failures.Count == 0)
        {
            report.Reset(OffsetState.Verified);
            ConsoleUI.Success($"[OFFSET] {group}: OK");
            return;
        }

        ReportVersionDiff(group, failures);
        report.LastError = failures[0];

        if (attemptResolve)
        {
            report.State = OffsetState.Quarantined;
            Quarantine(group, failures[0]);
        }
        else
        {
            report.State = OffsetState.Pending;
        }
    }

    // =================================================================
    // RING RESOLUTION
    // =================================================================

    private readonly record struct AppliedLayout(int Original);

    private static bool TryResolveRing(ProcessMemory m, out AppliedLayout applied, out List<string> log)
    {
        applied = new AppliedLayout(TR1RMemoryMap.MainRingItems);
        log = new List<string>();

        IntPtr t1 = m.Tomb1Base;
        if (t1 == IntPtr.Zero)
        {
            log.Add("tomb1.dll not loaded");
            return false;
        }

        ConsoleUI.Info("[OFFSET] Scanning tomb1.dll for the INVENTORY_ITEM table...");

        byte[] image = m.ReadBytes(t1 + ScanStart, ScanSize);
        if (image.Length < 0x10000)
        {
            log.Add("module snapshot failed");
            return false;
        }

        int pistolsIdx = FindTableAnchor(image);
        if (pistolsIdx < 0)
        {
            log.Add("INVENTORY_ITEM table not found (object_id signature mismatch) — game build likely changed");
            return false;
        }

        long pistolsAbs = t1.ToInt64() + ScanStart + pistolsIdx;
        long compassAbs = pistolsAbs + (long)TR1RMemoryMap.InvItemRelIndex.Compass * Stride;
        ConsoleUI.Info($"[OFFSET] Pistols anchor at tomb1.dll+0x{pistolsIdx + ScanStart:X}");

        if (!TryFindItemsArray(image, pistolsAbs, compassAbs, out int itemsRel, out int pistolsIdxInRing, out string itemsError))
        {
            log.Add(itemsError);
            return false;
        }

        int itemsOff = itemsRel + ScanStart;
        int count = CountRingEntries(image, itemsRel, pistolsAbs, out int countValid);
        if (count < 1)
        {
            log.Add($"items array at +0x{itemsOff:X} does not hold a coherent ring ({itemsError})");
            return false;
        }

        if (!TryFindCountOffset(m, itemsOff, count, out int countOff, out string countError))
        {
            log.Add(countError);
            return false;
        }

        if (!TryFindQtysOffset(m, itemsOff, count, out int qtysOff, out string qtysError))
        {
            log.Add(qtysError);
            return false;
        }

        // Validate the candidate as a whole before applying it.
        if (!TryEvaluateMainRing(m, countOff, itemsOff, qtysOff, out string reason))
        {
            log.Add($"scanned layout rejected: {reason}");
            return false;
        }

        log.Add($"main ring: count@+0x{countOff:X} items@+0x{itemsOff:X} qtys@+0x{qtysOff:X} (count={count}, pistols at ring index {pistolsIdxInRing})");

        TR1RMemoryMap.MainRingCount = countOff;
        TR1RMemoryMap.MainRingItems = itemsOff;
        TR1RMemoryMap.MainRingQtys = qtysOff;

        ResolveKeysRing(m, t1, pistolsAbs, itemsOff, count, log);
        return true;
    }

    /// <summary>
    /// Finds the Pistols INVENTORY_ITEM inside the module snapshot by matching a
    /// multi-object_id signature spread across the global table.
    /// </summary>
    private static int FindTableAnchor(byte[] image)
    {
        int limit = image.Length - (Math.Abs(TR1RMemoryMap.InvItemRelIndex.ShotgunAmmo) + 16) * Stride;
        for (int i = 0; i < limit; i++)
        {
            if (ReadI16(image, i + TR1RMemoryMap.InvItem_ObjectId) != TR1RMemoryMap.InvObjId.Pistols)
                continue;

            int hits = 0;
            foreach (var (rel, objId) in TableSignature)
            {
                int off = i + rel * Stride + TR1RMemoryMap.InvItem_ObjectId;
                if (off < 0 || off + 1 >= image.Length) continue;
                if (ReadI16(image, off) == objId) hits++;
            }

            if (hits >= RequiredSignatureHits)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Finds the ring's items[] array: the slot that holds the Pistols pointer
    /// with the Compass pointer at index 0.
    /// </summary>
    private static bool TryFindItemsArray(byte[] image, long pistolsAbs, long compassAbs, out int startRel, out int pistolsIndex, out string error)
    {
        startRel = -1;
        pistolsIndex = -1;
        error = "";

        int first = -1, firstAny = -1;

        for (int p = 0; p + 8 <= image.Length; p++)
        {
            if (ReadI64(image, p) != pistolsAbs) continue;

            for (int j = 0; j <= 8; j++)
            {
                int start = p - j * 8;
                if (start < 0) break;
                if (ReadI64(image, start) != compassAbs) continue;

                if (j == 1)
                {
                    startRel = start;
                    pistolsIndex = j;
                    return true;
                }
                if (first == -1) { first = start; firstAny = j; }
                break;
            }
        }

        if (first >= 0)
        {
            startRel = first;
            pistolsIndex = firstAny;
            return true;
        }

        error = "no items[] array containing Pistols with Compass at index 0";
        return false;
    }

    /// <summary>Walks the ring from index 0 and returns the first incoherent slot.</summary>
    private static int CountRingEntries(byte[] image, int itemsRel, long pistolsAbs, out int valid)
    {
        valid = 0;
        for (int i = 0; i < TR1RMemoryMap.MaxRingItems; i++)
        {
            int p = itemsRel + i * 8;
            if (p + 8 > image.Length) break;

            long ptr = ReadI64(image, p);
            if (ptr == 0) return i;

            long delta = ptr - pistolsAbs;
            if (delta % Stride != 0) return i;

            int rel = (int)(delta / Stride);
            if (rel is < -40 or > 60) return i;

            valid++;
        }
        return TR1RMemoryMap.MaxRingItems;
    }

    /// <summary>
    /// Locates the Int16 holding the ring count. Candidates are restricted to the
    /// ring struct's neighbourhood and scored by proximity to items[].
    /// </summary>
    private static bool TryFindCountOffset(ProcessMemory m, int itemsOff, int count, out int countOff, out string error)
    {
        countOff = -1;
        error = "";
        IntPtr t1 = m.Tomb1Base;

        // Structural first: a ring struct puts its header right before items[].
        int[] preferred =
        {
            itemsOff - 2, itemsOff - 4, itemsOff - 6, itemsOff - 8,
            itemsOff - 0x68, itemsOff - 0x64, itemsOff - 0x100,
        };

        foreach (int candidate in preferred)
        {
            if (candidate < 0) continue;
            if (m.ReadInt16(t1 + candidate) != count) continue;
            countOff = candidate;
            return true;
        }

        var hits = new List<int>();
        int windowStart = Math.Max(0, itemsOff - 0x4000);
        int windowEnd = itemsOff + 0x4000;
        for (int o = windowStart; o <= windowEnd; o += 2)
        {
            // Never treat the pointer array itself as the count field.
            if (o >= itemsOff && o < itemsOff + TR1RMemoryMap.MaxRingItems * 8) continue;
            if (m.ReadInt16(t1 + o) == count) hits.Add(o);
        }

        if (hits.Count == 0)
        {
            error = $"no Int16 holding {count} found near items[] +0x{itemsOff:X}";
            return false;
        }

        countOff = hits.OrderBy(o => Math.Abs(o - itemsOff)).First();
        if (hits.Count > 1)
            ConsoleUI.Warning($"[OFFSET] {hits.Count} count candidates near +0x{itemsOff:X}; picked +0x{countOff:X}");
        return true;
    }

    /// <summary>
    /// Locates the qtys[] array that belongs to the ring, using the semantic
    /// constraint that stacked ammo/weapon slots hold small counts and every
    /// entry inside the ring is bounded.
    /// </summary>
    private static bool TryFindQtysOffset(ProcessMemory m, int itemsOff, int count, out int qtysOff, out string error)
    {
        qtysOff = -1;
        error = "";
        IntPtr t1 = m.Tomb1Base;

        // Ring index of the small medipack, if present — its qty is the live stack.
        int medIdx = -1;
        var weaponIdx = new List<int>();
        IntPtr pistols = FindPistols(m, t1);
        if (pistols == IntPtr.Zero) pistols = m.ReadPointer(t1 + itemsOff + 8);

        for (int i = 0; i < count; i++)
        {
            IntPtr ptr = m.ReadPointer(t1 + itemsOff + i * 8);
            if (ptr == IntPtr.Zero || pistols == IntPtr.Zero) continue;
            int rel = (int)((ptr.ToInt64() - pistols.ToInt64()) / Stride);
            if (rel == TR1RMemoryMap.InvItemRelIndex.SmallMedipack) medIdx = i;
            if (rel is TR1RMemoryMap.InvItemRelIndex.Pistols or TR1RMemoryMap.InvItemRelIndex.Shotgun
                or TR1RMemoryMap.InvItemRelIndex.Magnums or TR1RMemoryMap.InvItemRelIndex.Uzis
                or TR1RMemoryMap.InvItemRelIndex.Compass)
            {
                weaponIdx.Add(i);
            }
        }

        var candidates = new List<int>();
        int windowStart = Math.Max(0, itemsOff - 0x2000);
        int windowEnd = itemsOff + 0x2000;

        for (int o = windowStart; o <= windowEnd; o += 2)
        {
            if (o >= itemsOff && o < itemsOff + TR1RMemoryMap.MaxRingItems * 8) continue;

            bool ok = true;
            int score = 0;

            for (int i = 0; i < count; i++)
            {
                short qty = m.ReadInt16(t1 + o + i * 2);
                if (qty is < 0 or > 255) { ok = false; break; }
                if (qty > 0) score++;
            }
            if (!ok) continue;

            // Weapon/compass slots stack at exactly 1.
            bool weaponOk = true;
            foreach (int i in weaponIdx)
            {
                if (m.ReadInt16(t1 + o + i * 2) != 1) { weaponOk = false; break; }
            }
            if (!weaponOk) continue;

            // A medipack slot, when present, must be non-zero (the sentinel
            // removal path only runs once at least one medipack exists).
            if (medIdx >= 0 && m.ReadInt16(t1 + o + medIdx * 2) == 0) continue;

            // Slots past the ring end should be cleared.
            for (int i = count; i < TR1RMemoryMap.MaxRingItems; i++)
            {
                if (m.ReadInt16(t1 + o + i * 2) == 0) score++;
            }

            candidates.Add(o);
        }

        if (candidates.Count == 0)
        {
            error = $"no qtys[] candidate found for {count} ring entries near +0x{itemsOff:X}";
            return false;
        }

        qtysOff = candidates.OrderBy(o => Math.Abs(o - itemsOff)).First();
        if (candidates.Count > 1)
            ConsoleUI.Warning($"[OFFSET] {candidates.Count} qtys candidates for count={count}; picked +0x{qtysOff:X}");
        return true;
    }

    /// <summary>
    /// Attempts to locate the Keys Ring. It is only observable once the player
    /// actually holds a key, so a miss is reported as pending rather than a
    /// version diff — IdentifyRing runs again on every level transition.
    /// </summary>
    private static void ResolveKeysRing(ProcessMemory m, IntPtr t1, long pistolsAbs, int mainItemsOff, int mainCount, List<string> log)
    {
        short keysCount = RingWalk(m, TR1RMemoryMap.KeysRingItems);
        log.Add($"keys ring: walked length {keysCount} @ +0x{TR1RMemoryMap.KeysRingItems:X} " +
                "(count field unverified, never written)");
    }

    // =================================================================
    // REPORTING
    // =================================================================

    /// <summary>
    /// The console banner printed whenever a group's compiled-in offsets stop
    /// describing the live game. Every subsequent recalc/retry attempt is logged
    /// with the same tag so a patch-induced regression is easy to find in a log.
    /// </summary>
    public static void ReportVersionDiff(OffsetGroup group, IReadOnlyList<string> failures)
    {
        var report = OffsetModel.Report(group);
        ConsoleUI.Error($"!!! VERSION DIFF DETECTED — {group} group (attempt {report.Attempts}) !!!");
        ConsoleUI.Error($"    build: {OffsetModel.BuildHash}");
        foreach (var failure in failures)
            ConsoleUI.Error($"    - {failure}");

        ConsoleUI.Error("    offsets under suspicion:");
        foreach (var name in OffsetModel.Members[group])
            ConsoleUI.Error($"      {name}");

        report.Notes.Add($"version diff: {failures[0]}");
    }

    public static void Quarantine(OffsetGroup group, string reason)
    {
        var report = OffsetModel.Report(group);
        report.State = OffsetState.Quarantined;
        report.LastError = reason;

        ConsoleUI.Error($"!!! {group} QUARANTINED — offsets could not be recalculated !!!");
        ConsoleUI.Error($"    {reason}");
        ConsoleUI.Error($"    quarantined groups: {OffsetModel.QuarantinedGroups()}");
    }

    private static short ReadI16(byte[] b, int i) => (short)(b[i] | (b[i + 1] << 8));

    private static long ReadI64(byte[] b, int i) =>
        (long)b[i] | ((long)b[i + 1] << 8) | ((long)b[i + 2] << 16) | ((long)b[i + 3] << 24)
        | ((long)b[i + 4] << 32) | ((long)b[i + 5] << 40) | ((long)b[i + 6] << 48) | ((long)b[i + 7] << 56);
}
