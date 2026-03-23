using TRArchipelagoClient.UI;

namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Automatically finds the live inventory heap address by correlating
/// WorldStateBackup medipack count changes with full process memory scans.
///
/// Runs passively during gameplay. When the WSB small medipack count changes
/// (indicating a pickup), it scans/intersects heap memory to pinpoint the
/// live inventory counter. After 2 pickups, the address is found.
///
/// Once found, the address can be used for writing (injecting items).
/// The address is stable within a level but may shift on level load,
/// so the scanner re-runs automatically when a level change is detected.
/// </summary>
public class InventoryScanner
{
    private readonly ProcessMemory _memory;
    private readonly GameContext _context;

    // WSB offset for small medipack count (relative to WSB start).
    // TR1: 0x626, TR2: 0x626 (same layout within WSB for medipacks).
    private const int WSB_SmallMedOffset = 0x626;

    // Scan state
    private byte _lastWsbSmallMed;
    private List<(IntPtr regionAddr, int regionSize)>? _regions;
    private HashSet<long>? _candidates;
    private IntPtr _foundAddress = IntPtr.Zero;
    private bool _scanning;
    private int _scanPhase; // 0=idle, 1=baseline taken, 2=found

    public InventoryScanner(ProcessMemory memory, GameContext context)
    {
        _memory = memory;
        _context = context;
    }

    /// <summary>True when the live inventory address has been found.</summary>
    public bool IsReady => _foundAddress != IntPtr.Zero;

    /// <summary>The live small medipack counter address (heap).</summary>
    public IntPtr SmallMedAddress => _foundAddress;

    /// <summary>
    /// Call this on level change to reset the scanner (heap addresses shift).
    /// </summary>
    public void Reset()
    {
        _foundAddress = IntPtr.Zero;
        _candidates = null;
        _scanPhase = 0;
        _scanning = false;
    }

    /// <summary>
    /// Called every poll cycle. Monitors WSB and triggers scans when needed.
    /// Returns true if a new address was just found this cycle.
    /// </summary>
    public bool Poll()
    {
        if (_scanning || _scanPhase >= 2) return false;

        var map = _context.Map;
        IntPtr dllBase = _context.DllBase;

        byte wsbSmallMed = _memory.ReadByte(
            dllBase + map.WorldStateBackup + WSB_SmallMedOffset);

        if (_scanPhase == 0)
        {
            // Phase 0: Take baseline — enumerate regions and find all bytes matching current WSB value
            _lastWsbSmallMed = wsbSmallMed;
            _scanning = true;

            Task.Run(() =>
            {
                try
                {
                    _regions = _memory.GetReadableRegions();
                    _candidates = ScanForValue(_lastWsbSmallMed);
                    _scanPhase = 1;
                    ConsoleUI.Info($"[Scanner] Baseline: {_candidates.Count} candidates for value {_lastWsbSmallMed}");
                }
                catch (Exception ex)
                {
                    ConsoleUI.Error($"[Scanner] Baseline scan failed: {ex.Message}");
                }
                finally
                {
                    _scanning = false;
                }
            });

            return false;
        }

        if (_scanPhase == 1 && wsbSmallMed != _lastWsbSmallMed)
        {
            // WSB value changed — a pickup happened! Intersect candidates.
            byte newVal = wsbSmallMed;
            byte oldVal = _lastWsbSmallMed;
            _lastWsbSmallMed = newVal;
            _scanning = true;

            Task.Run(() =>
            {
                try
                {
                    var newCandidates = IntersectCandidates(newVal);
                    ConsoleUI.Info($"[Scanner] Intersect: {_candidates!.Count} -> {newCandidates.Count} candidates (value {oldVal} -> {newVal})");
                    _candidates = newCandidates;

                    if (_candidates.Count <= 10 && _candidates.Count > 0)
                    {
                        // Try write-testing the candidates
                        foreach (long addr in _candidates)
                        {
                            IntPtr ptr = new IntPtr(addr);
                            byte before = _memory.ReadByte(ptr);
                            _memory.Write(ptr, (byte)(before + 5));
                            // Read WSB to see if it was affected (if so, it's just a WSB mirror)
                            byte wsbCheck = _memory.ReadByte(
                                dllBase + map.WorldStateBackup + WSB_SmallMedOffset);
                            _memory.Write(ptr, before); // restore

                            // Skip if this address is within the DLL static region
                            long dllStart = (long)dllBase;
                            if (addr >= dllStart && addr < dllStart + 0x600000)
                                continue;

                            // Accept the first non-DLL candidate
                            _foundAddress = ptr;
                            _scanPhase = 2;
                            ConsoleUI.Success($"[Scanner] Found live inventory at 0x{addr:X}!");
                            return;
                        }

                        // If all were in-DLL, pick the first heap one anyway
                        if (_foundAddress == IntPtr.Zero && _candidates.Count > 0)
                        {
                            long first = _candidates.First();
                            _foundAddress = new IntPtr(first);
                            _scanPhase = 2;
                            ConsoleUI.Success($"[Scanner] Best candidate: 0x{first:X}");
                        }
                    }
                    // else: still too many, will intersect again on next change
                }
                catch (Exception ex)
                {
                    ConsoleUI.Error($"[Scanner] Intersect failed: {ex.Message}");
                }
                finally
                {
                    _scanning = false;
                }
            });

            return false;
        }

        return _scanPhase == 2 && _foundAddress != IntPtr.Zero;
    }

    /// <summary>Scans all readable memory for bytes matching the given value.</summary>
    private HashSet<long> ScanForValue(byte value)
    {
        var results = new HashSet<long>();
        long dllStart = (long)_context.DllBase;
        long dllEnd = dllStart + 0x600000;

        foreach (var (regionAddr, regionSize) in _regions!)
        {
            // Skip DLL static region (we know WSB is there, not live inventory)
            long rStart = (long)regionAddr;
            if (rStart >= dllStart && rStart < dllEnd)
                continue;

            try
            {
                byte[] data = _memory.ReadBytes(regionAddr, regionSize);
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] == value)
                        results.Add(rStart + i);
                }
            }
            catch { /* skip unreadable */ }
        }

        return results;
    }

    /// <summary>
    /// Re-reads candidate addresses and keeps only those matching the new value.
    /// Much faster than a full scan since we only check known candidates.
    /// </summary>
    private HashSet<long> IntersectCandidates(byte expectedValue)
    {
        var survivors = new HashSet<long>();

        // Group candidates by region for batch reading
        foreach (var (regionAddr, regionSize) in _regions!)
        {
            long rStart = (long)regionAddr;
            long rEnd = rStart + regionSize;

            // Check if any candidates fall in this region
            var regionCandidates = _candidates!.Where(c => c >= rStart && c < rEnd).ToList();
            if (regionCandidates.Count == 0) continue;

            try
            {
                byte[] data = _memory.ReadBytes(regionAddr, regionSize);
                foreach (long addr in regionCandidates)
                {
                    int offset = (int)(addr - rStart);
                    if (offset >= 0 && offset < data.Length && data[offset] == expectedValue)
                        survivors.Add(addr);
                }
            }
            catch { /* skip */ }
        }

        return survivors;
    }

    /// <summary>
    /// Writes a byte value to the found live inventory address.
    /// </summary>
    public void WriteSmallMedCount(byte count)
    {
        if (_foundAddress != IntPtr.Zero)
            _memory.Write(_foundAddress, count);
    }

    /// <summary>
    /// Reads the current small medipack count from the live address.
    /// </summary>
    public byte ReadSmallMedCount()
    {
        if (_foundAddress != IntPtr.Zero)
            return _memory.ReadByte(_foundAddress);
        return 0;
    }
}
