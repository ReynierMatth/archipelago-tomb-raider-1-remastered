using TRArchipelagoClient.UI;

namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Monitors the Keys Ring to detect when a key item disappears during normal gameplay.
/// A key item disappearing (without a save/load event) means it was used in a door/lock.
/// </summary>
public class KeyItemMonitor
{
    private readonly ProcessMemory _memory;
    private readonly GameContext _context;
    private bool _paused;

    // Last known state of the Keys Ring: pointer → qty
    private readonly Dictionary<long, short> _lastKeysRing = new();

    public KeyItemMonitor(ProcessMemory memory, GameContext context)
    {
        _memory = memory;
        _context = context;
    }

    /// <summary>
    /// Captures the current state of the Keys Ring as a baseline for comparison.
    /// Call after settle completes and after save/load reconciliation.
    /// </summary>
    public void SnapshotKeysRing()
    {
        _lastKeysRing.Clear();

        var map = _context.Map;
        IntPtr dllBase = _context.DllBase;
        short count = _memory.ReadInt16(dllBase + map.KeysRingCount);

        for (int i = 0; i < count; i++)
        {
            long ptr = _memory.ReadInt64(dllBase + map.KeysRingItems + i * 8);
            short qty = _memory.ReadInt16(dllBase + map.KeysRingQtys + i * 2);
            if (ptr != 0)
                _lastKeysRing[ptr] = qty;
        }
    }

    /// <summary>
    /// Compares the current Keys Ring with the last snapshot and returns pointers
    /// of items that disappeared or had their quantity reduced.
    /// Each returned entry is (pointer, qtyLost).
    /// </summary>
    public List<(long Pointer, short QtyLost)> DetectUsedKeys()
    {
        if (_paused)
            return new();

        var result = new List<(long, short)>();

        var map = _context.Map;
        IntPtr dllBase = _context.DllBase;
        short count = _memory.ReadInt16(dllBase + map.KeysRingCount);

        // Build current state
        var currentRing = new Dictionary<long, short>();
        for (int i = 0; i < count; i++)
        {
            long ptr = _memory.ReadInt64(dllBase + map.KeysRingItems + i * 8);
            short qty = _memory.ReadInt16(dllBase + map.KeysRingQtys + i * 2);
            if (ptr != 0)
                currentRing[ptr] = qty;
        }

        // Compare: find items that disappeared or lost quantity
        foreach (var (ptr, oldQty) in _lastKeysRing)
        {
            if (currentRing.TryGetValue(ptr, out short newQty))
            {
                if (newQty < oldQty)
                    result.Add((ptr, (short)(oldQty - newQty)));
            }
            else
            {
                // Item completely gone
                result.Add((ptr, oldQty));
            }
        }

        // Update snapshot to current state
        if (result.Count > 0)
        {
            _lastKeysRing.Clear();
            foreach (var (ptr, qty) in currentRing)
                _lastKeysRing[ptr] = qty;
        }

        return result;
    }

    /// <summary>
    /// Pause monitoring during settle/reload to avoid false detections.
    /// </summary>
    public void Pause() => _paused = true;

    /// <summary>
    /// Resume monitoring after settle/reload.
    /// </summary>
    public void Resume() => _paused = false;
}
