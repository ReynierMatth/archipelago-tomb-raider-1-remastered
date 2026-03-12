using TRArchipelagoClient.Core;

namespace TRArchipelagoClient.Patching;

/// <summary>
/// Manages backup and restoration of original level files.
/// Creates backups before patching, restores on session end.
/// </summary>
public class BackupManager
{
    private const string BackupSuffix = ".apbak";

    private readonly string _gameDir;
    private readonly GameConfig _config;

    public BackupManager(string gameDir, GameConfig config)
    {
        _gameDir = gameDir;
        _config = config;
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
        foreach (string levelBase in _config.LevelBaseNames)
        {
            foreach (string ext in _config.LevelExtensions)
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
        foreach (string levelBase in _config.LevelBaseNames)
        {
            foreach (string ext in _config.LevelExtensions)
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

        return _config.LevelBaseNames.Any(levelBase =>
            _config.LevelExtensions.Any(ext =>
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
    /// Uses config DataSubDir and SentinelFile for detection.
    /// </summary>
    private string FindDataDir()
    {
        string[] possiblePaths =
        {
            Path.Combine(_gameDir, _config.DataSubDir, "DATA"),
            Path.Combine(_gameDir, _config.DataSubDir),
            Path.Combine(_gameDir, "data", _config.DataSubDir),
            _gameDir,
        };

        foreach (string path in possiblePaths)
        {
            if (Directory.Exists(path) &&
                File.Exists(Path.Combine(path, _config.SentinelFile)))
            {
                return path;
            }
        }

        return null;
    }
}
