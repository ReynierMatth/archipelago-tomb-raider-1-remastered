using TRArchipelagoClient.UI;

namespace TRArchipelagoClient.Core;

/// <summary>
/// Manages save file isolation between normal TR1R saves and Archipelago saves.
/// Swaps savegame.dat at session start/end to prevent interference.
/// </summary>
public class SaveFileManager
{
    private const string SaveFileName = "savegame.dat";
    private const string NormalBackupSuffix = ".normal";
    private const string ApSaveSuffix = ".ap";

    public string SaveDirectory { get; }
    public string SaveFilePath => Path.Combine(SaveDirectory, SaveFileName);
    public bool IsSwapped { get; private set; }

    private string NormalBackupPath => SaveFilePath + NormalBackupSuffix;
    private string ApSavePath => SaveFilePath + ApSaveSuffix;

    public SaveFileManager(string saveDirectory)
    {
        SaveDirectory = saveDirectory;
    }

    /// <summary>
    /// Auto-detect the TRX save directory by scanning %APPDATA%\TRX\ for user folders containing savegame.dat.
    /// Returns null if the TRX folder doesn't exist.
    /// </summary>
    public static string? DetectSaveDirectory()
    {
        string trxRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TRX");

        if (!Directory.Exists(trxRoot))
        {
            ConsoleUI.Error($"TRX directory not found: {trxRoot}");
            return null;
        }

        var candidates = Directory.GetDirectories(trxRoot)
            .Where(dir => File.Exists(Path.Combine(dir, SaveFileName)))
            .ToList();

        switch (candidates.Count)
        {
            case 0:
                // No savegame.dat found — the game may never have been launched.
                // Look for any user directory instead (game will create the file on first save).
                var userDirs = Directory.GetDirectories(trxRoot);
                if (userDirs.Length == 1)
                {
                    ConsoleUI.Info($"[Save] No savegame.dat found, using directory: {userDirs[0]}");
                    return userDirs[0];
                }
                if (userDirs.Length > 1)
                {
                    ConsoleUI.Info("[Save] Multiple TRX user directories found (none have savegame.dat):");
                    return PromptChoice(userDirs);
                }
                ConsoleUI.Error("No user directories found in TRX folder.");
                return null;

            case 1:
                ConsoleUI.Info($"[Save] Detected save directory: {candidates[0]}");
                return candidates[0];

            default:
                ConsoleUI.Info("[Save] Multiple save directories found:");
                return PromptChoice(candidates.ToArray());
        }
    }

    /// <summary>
    /// Swap in AP saves: backup normal saves and restore AP saves.
    /// Call at session startup, before attaching to the game.
    /// </summary>
    public void SwapIn()
    {
        // Recovery: if .normal exists, a previous session didn't clean up.
        if (File.Exists(NormalBackupPath))
        {
            ConsoleUI.Info("[Save] Recovering from previous session...");
            ForceSwapOut();
        }

        bool hadSave = File.Exists(SaveFilePath);
        bool hadAp = File.Exists(ApSavePath);

        // 1. Backup normal saves out of the way (if any)
        if (hadSave)
        {
            File.Move(SaveFilePath, NormalBackupPath);
            ConsoleUI.Info("[Save] Normal saves backed up.");
        }

        // 2. Restore AP saves as active (if any)
        if (hadAp)
        {
            File.Move(ApSavePath, SaveFilePath);
            ConsoleUI.Info("[Save] AP saves restored.");
        }
        else
        {
            // No AP save yet — game starts fresh (no savegame.dat)
            ConsoleUI.Info("[Save] No previous AP saves — starting fresh.");
        }

        IsSwapped = true;
    }

    /// <summary>
    /// Swap out: persist AP saves and restore the exact initial state.
    /// If there was no savegame.dat before SwapIn, there won't be one after SwapOut.
    /// Call at session end, before restoring level files.
    /// </summary>
    public void SwapOut()
    {
        if (!IsSwapped)
            return;

        ForceSwapOut();
    }

    /// <summary>
    /// Unconditional swap out, used by SwapOut and crash recovery.
    /// </summary>
    private void ForceSwapOut()
    {
        // 1. Persist current savegame.dat as AP save (if any)
        if (File.Exists(SaveFilePath))
        {
            File.Move(SaveFilePath, ApSavePath, overwrite: true);
            ConsoleUI.Info("[Save] AP saves persisted.");
        }

        // 2. Restore normal saves (if they were backed up)
        //    If .normal doesn't exist, there was no savegame.dat before — leave it absent.
        if (File.Exists(NormalBackupPath))
        {
            File.Move(NormalBackupPath, SaveFilePath);
            ConsoleUI.Info("[Save] Normal saves restored.");
        }

        IsSwapped = false;
    }

    private static string PromptChoice(string[] directories)
    {
        for (int i = 0; i < directories.Length; i++)
        {
            Console.WriteLine($"  [{i + 1}] {Path.GetFileName(directories[i])}");
        }

        while (true)
        {
            string input = ConsoleUI.Prompt($"Choose (1-{directories.Length})");
            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= directories.Length)
            {
                return directories[choice - 1];
            }
        }
    }
}
