namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Queues AP-received items and writes them to the save file.
/// After the game saves, pending items are injected into the save slot.
/// The player must reload the save to receive the items.
///
/// Handles: weapons (set flag), ammo (increment), medipacks (increment),
/// and traps (reduce health, drain ammo/meds).
/// </summary>
public class SaveFileInventoryWriter
{
    private readonly string _saveFilePath;
    private readonly Queue<PendingItem> _pendingItems = new();

    public SaveFileInventoryWriter(string saveFilePath)
    {
        _saveFilePath = saveFilePath;
    }

    public bool HasPendingItems => _pendingItems.Count > 0;
    public int PendingCount => _pendingItems.Count;

    /// <summary>
    /// Queues an item to be written to the save file after the next game save.
    /// </summary>
    public void QueueItem(PendingItem item)
    {
        _pendingItems.Enqueue(item);
    }

    /// <summary>
    /// Writes all pending items to the specified save slot.
    /// Should be called immediately after detecting a new game save.
    /// Returns the number of items successfully written.
    /// </summary>
    public int WritePendingItems(int slotIndex)
    {
        if (_pendingItems.Count == 0) return 0;

        try
        {
            using var fs = new FileStream(_saveFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            int slotBase = TR1RMemoryMap.SaveFileBaseOffset + (slotIndex * TR1RMemoryMap.SaveSlotSize);

            // Read current level for per-level offsets
            fs.Seek(slotBase + TR1RMemoryMap.Save_LevelIndex, SeekOrigin.Begin);
            byte levelIndex = (byte)fs.ReadByte();

            int written = 0;
            while (_pendingItems.TryDequeue(out var item))
            {
                ApplyItem(fs, slotBase, levelIndex, item);
                written++;
            }

            return written;
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[SaveWriter] Failed to write items: {ex.Message}");
            return 0;
        }
    }

    private static void ApplyItem(FileStream fs, int slotBase, byte levelIndex, PendingItem item)
    {
        switch (item.Type)
        {
            case PendingItemType.SmallMedipack:
                IncrementByte(fs, slotBase + TR1RMemoryMap.Save_SmallMedipacks, (byte)item.Amount);
                break;

            case PendingItemType.LargeMedipack:
                IncrementByte(fs, slotBase + TR1RMemoryMap.Save_LargeMedipacks, (byte)item.Amount);
                break;

            case PendingItemType.MagnumAmmo:
                IncrementUInt16(fs, slotBase + TR1RMemoryMap.Save_MagnumAmmo, (ushort)item.Amount);
                break;

            case PendingItemType.UziAmmo:
                IncrementUInt16(fs, slotBase + TR1RMemoryMap.Save_UziAmmo, (ushort)item.Amount);
                break;

            case PendingItemType.ShotgunAmmo:
                // Save file stores raw value (shells * 6 for shotgun)
                IncrementUInt16(fs, slotBase + TR1RMemoryMap.Save_ShotgunAmmo, (ushort)(item.Amount * 6));
                break;

            case PendingItemType.Weapon:
                SetWeaponFlag(fs, slotBase + TR1RMemoryMap.Save_WeaponsConfig, (byte)item.Amount);
                break;

            case PendingItemType.DamageTrap:
                int healthOffset = TR1RMemoryMap.GetSaveHealthOffset(levelIndex);
                if (healthOffset > 0)
                    ReduceHealth(fs, slotBase + healthOffset, 0.25);
                break;

            case PendingItemType.AmmoDrain:
                HalveUInt16(fs, slotBase + TR1RMemoryMap.Save_MagnumAmmo);
                HalveUInt16(fs, slotBase + TR1RMemoryMap.Save_UziAmmo);
                HalveUInt16(fs, slotBase + TR1RMemoryMap.Save_ShotgunAmmo);
                break;

            case PendingItemType.MedipackDrain:
                DecrementByte(fs, slotBase + TR1RMemoryMap.Save_SmallMedipacks);
                break;
        }
    }

    private static void IncrementByte(FileStream fs, int offset, byte amount)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        byte current = (byte)fs.ReadByte();
        byte newVal = (byte)Math.Min(current + amount, 255);
        fs.Seek(offset, SeekOrigin.Begin);
        fs.WriteByte(newVal);
    }

    private static void DecrementByte(FileStream fs, int offset)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        byte current = (byte)fs.ReadByte();
        if (current > 0)
        {
            fs.Seek(offset, SeekOrigin.Begin);
            fs.WriteByte((byte)(current - 1));
        }
    }

    private static void IncrementUInt16(FileStream fs, int offset, ushort amount)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        byte[] buf = new byte[2];
        fs.Read(buf, 0, 2);
        ushort current = BitConverter.ToUInt16(buf);
        ushort newVal = (ushort)Math.Min(current + amount, ushort.MaxValue);
        fs.Seek(offset, SeekOrigin.Begin);
        fs.Write(BitConverter.GetBytes(newVal));
    }

    private static void HalveUInt16(FileStream fs, int offset)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        byte[] buf = new byte[2];
        fs.Read(buf, 0, 2);
        ushort current = BitConverter.ToUInt16(buf);
        if (current > 0)
        {
            fs.Seek(offset, SeekOrigin.Begin);
            fs.Write(BitConverter.GetBytes((ushort)(current / 2)));
        }
    }

    private static void SetWeaponFlag(FileStream fs, int offset, byte flag)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        byte current = (byte)fs.ReadByte();
        fs.Seek(offset, SeekOrigin.Begin);
        fs.WriteByte((byte)(current | flag));
    }

    private static void ReduceHealth(FileStream fs, int offset, double fraction)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        byte[] buf = new byte[2];
        fs.Read(buf, 0, 2);
        ushort health = BitConverter.ToUInt16(buf);
        ushort damage = (ushort)(health * fraction);
        ushort newHealth = (ushort)Math.Max(health - damage, TR1RMemoryMap.MinHealth);
        fs.Seek(offset, SeekOrigin.Begin);
        fs.Write(BitConverter.GetBytes(newHealth));
    }
}

public enum PendingItemType
{
    SmallMedipack,
    LargeMedipack,
    MagnumAmmo,
    UziAmmo,
    ShotgunAmmo,
    Weapon,
    DamageTrap,
    AmmoDrain,
    MedipackDrain,
}

public class PendingItem
{
    public required PendingItemType Type { get; init; }
    public int Amount { get; init; }
    public string? DisplayName { get; init; }
}
