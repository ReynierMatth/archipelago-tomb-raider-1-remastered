namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Represents the state of a single save slot in savegame.dat.
/// All values are read directly from the documented save file format.
/// </summary>
public class SaveSlotState
{
    public int SlotIndex { get; init; }
    public int SaveNumber { get; init; }
    public byte GameMode { get; init; }

    /// <summary>1-based level index as stored in the save file.</summary>
    public byte LevelIndex { get; init; }

    /// <summary>0-based level index for use with LocationMapper.</summary>
    public int LocationMapperIndex => LevelIndex - 1;

    public ushort Health { get; init; }
    public ushort SecretsFound { get; init; }
    public byte Pickups { get; init; }
    public byte SmallMedipacks { get; init; }
    public byte LargeMedipacks { get; init; }
    public ushort MagnumAmmo { get; init; }
    public ushort UziAmmo { get; init; }
    public ushort ShotgunAmmo { get; init; }
    public byte WeaponsConfig { get; init; }
    public int Kills { get; init; }
    public int TimeTaken { get; init; }
    public int AmmoUsed { get; init; }
    public int Hits { get; init; }
    public uint Distance { get; init; }
    public byte MedipacksUsed { get; init; }

    /// <summary>Count of secrets found in the current level (bits set in SecretsFound).</summary>
    public int SecretCount
    {
        get
        {
            int count = 0;
            ushort v = SecretsFound;
            while (v != 0)
            {
                count += v & 1;
                v >>= 1;
            }
            return count;
        }
    }

    /// <summary>Check if a specific secret (0-based) was found in the current level.</summary>
    public bool HasSecret(int index) => (SecretsFound & (1 << index)) != 0;

    public bool HasPistols => (WeaponsConfig & TR1RMemoryMap.Weapon_Pistols) != 0;
    public bool HasMagnums => (WeaponsConfig & TR1RMemoryMap.Weapon_Magnums) != 0;
    public bool HasUzis => (WeaponsConfig & TR1RMemoryMap.Weapon_Uzis) != 0;
    public bool HasShotgun => (WeaponsConfig & TR1RMemoryMap.Weapon_Shotgun) != 0;

    public string LevelName => TR1RMemoryMap.LevelNames.GetValueOrDefault(LevelIndex, $"Level {LevelIndex}");
}

/// <summary>
/// Reads game state from the TR1 Remastered savegame.dat file.
/// Uses the documented save file format sourced from TRR-SaveMaster.
///
/// The savegame.dat file is shared across TR1/TR2/TR3 Remastered.
/// TR1 saves occupy offsets 0x2000 to 0x72000, with each slot being 0x3800 bytes.
/// </summary>
public class SaveFileReader
{
    private readonly string _saveFilePath;

    public SaveFileReader(string saveFilePath)
    {
        _saveFilePath = saveFilePath;
    }

    public bool SaveFileExists => File.Exists(_saveFilePath);
    public string SaveFilePath => _saveFilePath;

    /// <summary>
    /// Reads the most recent TR1 save slot (highest save number).
    /// Returns null if no occupied TR1 save slots are found.
    /// </summary>
    public SaveSlotState? ReadLatestSlot()
    {
        if (!File.Exists(_saveFilePath))
            return null;

        try
        {
            using var fs = new FileStream(_saveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(fs);

            if (fs.Length < TR1RMemoryMap.SaveFileBaseOffset)
                return null;

            SaveSlotState? latest = null;
            int highestSaveNum = -1;

            for (int i = 0; i < TR1RMemoryMap.MaxSaveSlots; i++)
            {
                int slotBase = TR1RMemoryMap.SaveFileBaseOffset + (i * TR1RMemoryMap.SaveSlotSize);

                if (slotBase + TR1RMemoryMap.SaveSlotSize > TR1RMemoryMap.SaveFileMaxOffset)
                    break;

                fs.Seek(slotBase + TR1RMemoryMap.Save_SlotStatus, SeekOrigin.Begin);
                byte status = reader.ReadByte();
                if (status != 1) continue;

                fs.Seek(slotBase + TR1RMemoryMap.Save_Number, SeekOrigin.Begin);
                int saveNum = reader.ReadInt32();

                if (saveNum > highestSaveNum)
                {
                    highestSaveNum = saveNum;
                    latest = ReadSlot(reader, fs, slotBase, i);
                }
            }

            return latest;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads all occupied TR1 save slots.
    /// </summary>
    public List<SaveSlotState> ReadAllSlots()
    {
        var slots = new List<SaveSlotState>();
        if (!File.Exists(_saveFilePath))
            return slots;

        try
        {
            using var fs = new FileStream(_saveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(fs);

            for (int i = 0; i < TR1RMemoryMap.MaxSaveSlots; i++)
            {
                int slotBase = TR1RMemoryMap.SaveFileBaseOffset + (i * TR1RMemoryMap.SaveSlotSize);
                if (slotBase + TR1RMemoryMap.SaveSlotSize > TR1RMemoryMap.SaveFileMaxOffset)
                    break;

                fs.Seek(slotBase + TR1RMemoryMap.Save_SlotStatus, SeekOrigin.Begin);
                byte status = reader.ReadByte();
                if (status != 1) continue;

                slots.Add(ReadSlot(reader, fs, slotBase, i));
            }
        }
        catch (IOException) { }

        return slots;
    }

    /// <summary>
    /// Reads a specific save slot by index (0-based).
    /// Returns null if the slot is empty or out of range.
    /// </summary>
    public SaveSlotState? ReadSlot(int slotIndex)
    {
        if (!File.Exists(_saveFilePath) || slotIndex < 0 || slotIndex >= TR1RMemoryMap.MaxSaveSlots)
            return null;

        try
        {
            using var fs = new FileStream(_saveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(fs);

            int slotBase = TR1RMemoryMap.SaveFileBaseOffset + (slotIndex * TR1RMemoryMap.SaveSlotSize);
            if (slotBase + TR1RMemoryMap.SaveSlotSize > TR1RMemoryMap.SaveFileMaxOffset)
                return null;

            fs.Seek(slotBase + TR1RMemoryMap.Save_SlotStatus, SeekOrigin.Begin);
            byte status = reader.ReadByte();
            if (status != 1) return null;

            return ReadSlot(reader, fs, slotBase, slotIndex);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static SaveSlotState ReadSlot(BinaryReader reader, FileStream fs, int slotBase, int slotIndex)
    {
        byte levelIndex = ReadByteAt(reader, fs, slotBase + TR1RMemoryMap.Save_LevelIndex);

        ushort health = 0;
        int healthOffset = TR1RMemoryMap.GetSaveHealthOffset(levelIndex);
        if (healthOffset > 0)
        {
            health = ReadUInt16At(reader, fs, slotBase + healthOffset);
        }

        return new SaveSlotState
        {
            SlotIndex = slotIndex,
            SaveNumber = ReadInt32At(reader, fs, slotBase + TR1RMemoryMap.Save_Number),
            GameMode = ReadByteAt(reader, fs, slotBase + TR1RMemoryMap.Save_GameMode),
            LevelIndex = levelIndex,
            Health = health,
            SecretsFound = ReadUInt16At(reader, fs, slotBase + TR1RMemoryMap.Save_SecretsFound),
            Pickups = ReadByteAt(reader, fs, slotBase + TR1RMemoryMap.Save_Pickups),
            SmallMedipacks = ReadByteAt(reader, fs, slotBase + TR1RMemoryMap.Save_SmallMedipacks),
            LargeMedipacks = ReadByteAt(reader, fs, slotBase + TR1RMemoryMap.Save_LargeMedipacks),
            MagnumAmmo = ReadUInt16At(reader, fs, slotBase + TR1RMemoryMap.Save_MagnumAmmo),
            UziAmmo = ReadUInt16At(reader, fs, slotBase + TR1RMemoryMap.Save_UziAmmo),
            ShotgunAmmo = ReadUInt16At(reader, fs, slotBase + TR1RMemoryMap.Save_ShotgunAmmo),
            WeaponsConfig = ReadByteAt(reader, fs, slotBase + TR1RMemoryMap.Save_WeaponsConfig),
            Kills = ReadInt32At(reader, fs, slotBase + TR1RMemoryMap.Save_Kills),
            TimeTaken = ReadInt32At(reader, fs, slotBase + TR1RMemoryMap.Save_TimeTaken),
            AmmoUsed = ReadInt32At(reader, fs, slotBase + TR1RMemoryMap.Save_AmmoUsed),
            Hits = ReadInt32At(reader, fs, slotBase + TR1RMemoryMap.Save_Hits),
            Distance = ReadUInt32At(reader, fs, slotBase + TR1RMemoryMap.Save_Distance),
            MedipacksUsed = ReadByteAt(reader, fs, slotBase + TR1RMemoryMap.Save_MedipacksUsed),
        };
    }

    private static byte ReadByteAt(BinaryReader reader, FileStream fs, int offset)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        return reader.ReadByte();
    }

    private static ushort ReadUInt16At(BinaryReader reader, FileStream fs, int offset)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        return reader.ReadUInt16();
    }

    private static int ReadInt32At(BinaryReader reader, FileStream fs, int offset)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        return reader.ReadInt32();
    }

    private static uint ReadUInt32At(BinaryReader reader, FileStream fs, int offset)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        return reader.ReadUInt32();
    }
}
