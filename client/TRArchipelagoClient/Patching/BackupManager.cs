namespace TRArchipelagoClient.Patching;

/// <summary>
/// Manages backup and restoration of original level files.
/// Creates backups before patching, restores on session end.
/// </summary>
public class BackupManager
{
    private const string BackupSuffix = ".apbak";

    private readonly string _gameDir;

    // TR1 level file extensions to backup
    private static readonly string[] LevelExtensions = { ".PHD", ".PDP", ".MAP" };

    // TR1 level files
    private static readonly string[] LevelFiles =
    {
        "LEVEL1", "LEVEL2", "LEVEL3A", "LEVEL3B",
        "LEVEL4", "LEVEL5", "LEVEL6", "LEVEL7A", "LEVEL7B",
        "LEVEL8A", "LEVEL8B", "LEVEL8C",
        "LEVEL10A", "LEVEL10B", "LEVEL10C",
    };

    public BackupManager(string gameDir)
    {
        _gameDir = gameDir;
    }

    /// <summary>
    /// Backup all level files if backups don't already exist.
    /// </summary>
    public void BackupAll()
    {
        string dataDir = FindDataDir();
        if (dataDir == null)
        {
            Console.WriteLine("[Backup] WARNING: Could not find level data directory.");
            return;
        }

        int count = 0;
        foreach (string levelBase in LevelFiles)
        {
            foreach (string ext in LevelExtensions)
            {
                string original = Path.Combine(dataDir, levelBase + ext);
                string backup = original + BackupSuffix;

                if (File.Exists(original) && !File.Exists(backup))
                {
                    File.Copy(original, backup, overwrite: false);
                    count++;
                }
            }
        }

        Console.WriteLine($"[Backup] Backed up {count} files.");
    }

    /// <summary>
    /// Restore all level files from backups.
    /// </summary>
    public void RestoreAll()
    {
        string dataDir = FindDataDir();
        if (dataDir == null) return;

        int count = 0;
        foreach (string levelBase in LevelFiles)
        {
            foreach (string ext in LevelExtensions)
            {
                string original = Path.Combine(dataDir, levelBase + ext);
                string backup = original + BackupSuffix;

                if (File.Exists(backup))
                {
                    File.Copy(backup, original, overwrite: true);
                    File.Delete(backup);
                    count++;
                }
            }
        }

        Console.WriteLine($"[Backup] Restored {count} files.");
    }

    /// <summary>
    /// Check if backups exist (indicates a previous patching session).
    /// </summary>
    public bool HasBackups()
    {
        string dataDir = FindDataDir();
        if (dataDir == null) return false;

        return LevelFiles.Any(levelBase =>
            LevelExtensions.Any(ext =>
                File.Exists(Path.Combine(dataDir, levelBase + ext + BackupSuffix))));
    }

    /// <summary>
    /// Get the path to a specific level file.
    /// </summary>
    public string GetLevelPath(string levelFile)
    {
        string dataDir = FindDataDir();
        if (dataDir == null) return null;
        string path = Path.Combine(dataDir, levelFile);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Find the data directory containing level files.
    /// TR1-3 Remastered stores levels in a subdirectory structure.
    /// </summary>
    private string FindDataDir()
    {
        // Common paths for TR1 Remastered level data
        string[] possiblePaths =
        {
            Path.Combine(_gameDir, "1"),              // TR1 subfolder
            Path.Combine(_gameDir, "data", "1"),
            Path.Combine(_gameDir, "TR1"),
            _gameDir,                                  // Flat structure
        };

        foreach (string path in possiblePaths)
        {
            if (Directory.Exists(path) &&
                File.Exists(Path.Combine(path, "LEVEL1.PHD")))
            {
                return path;
            }
        }

        return null;
    }
}
