namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Creates the appropriate IGameMemoryMap for the active game version.
/// </summary>
public static class GameMemoryMapFactory
{
    public static IGameMemoryMap Create(int gameVersion) => gameVersion switch
    {
        0 => new TR1GameMemoryMap(),
        1 => new TR2GameMemoryMap(),
        2 => new TR3GameMemoryMap(),
        _ => throw new ArgumentException($"Unsupported game version: {gameVersion}")
    };
}
