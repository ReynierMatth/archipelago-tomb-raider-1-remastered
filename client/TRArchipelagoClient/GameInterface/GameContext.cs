namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Holds the runtime state for the currently active game.
/// Passed to GameStateWatcher, InventoryManager, KeyItemMonitor, etc.
/// Swapped when the player switches between TR1/TR2/TR3.
/// </summary>
public class GameContext
{
    public IGameMemoryMap Map { get; set; } = null!;
    public IntPtr DllBase { get; set; }
    public int GameVersion { get; set; } // 0=TR1, 1=TR2, 2=TR3

    /// <summary>
    /// Updates the context for a new active game.
    /// </summary>
    public void SwitchGame(int gameVersion, IGameMemoryMap map, IntPtr dllBase)
    {
        GameVersion = gameVersion;
        Map = map;
        DllBase = dllBase;
    }
}
