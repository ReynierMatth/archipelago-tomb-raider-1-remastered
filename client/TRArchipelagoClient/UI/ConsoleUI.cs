namespace TRArchipelagoClient.UI;

/// <summary>
/// Console-based user interface for the TR Archipelago client.
/// Handles colored output, prompts, and status display.
/// </summary>
public static class ConsoleUI
{
    public static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
  ╔══════════════════════════════════════════════╗
  ║    Tomb Raider 1 Remastered                  ║
  ║    Archipelago Multiworld Client              ║
  ╚══════════════════════════════════════════════╝
");
        Console.ResetColor();
    }

    public static void Info(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"  [INFO] {message}");
        Console.ResetColor();
    }

    public static void Success(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  [OK]   {message}");
        Console.ResetColor();
    }

    public static void Warning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [WARN] {message}");
        Console.ResetColor();
    }

    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [ERR]  {message}");
        Console.ResetColor();
    }

    public static void ItemReceived(string itemName, string fromPlayer)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.Write($"  [RECV] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(itemName);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(" from ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(fromPlayer);
        Console.ResetColor();
    }

    public static void ItemSent(string itemName, string toPlayer)
    {
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.Write($"  [SENT] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(itemName);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(" to ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(toPlayer);
        Console.ResetColor();
    }

    public static void LevelChange(string levelName)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n  ═══ Entering: {levelName} ═══\n");
        Console.ResetColor();
    }

    public static void SecretFound(int secretNumber, string levelName)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  [SECRET] Found secret #{secretNumber} in {levelName}!");
        Console.ResetColor();
    }

    public static string Prompt(string label)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"  {label}: ");
        Console.ResetColor();
        return Console.ReadLine()?.Trim() ?? "";
    }

    public static bool Confirm(string question)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"  {question} (y/n): ");
        Console.ResetColor();
        string response = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";
        return response == "y" || response == "yes";
    }

    public static void PrintProgress(int locationsChecked, int totalLocations, int itemsReceived)
    {
        double pct = totalLocations > 0 ? (locationsChecked * 100.0 / totalLocations) : 0;
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"\r  Progress: {locationsChecked}/{totalLocations} ({pct:F1}%) | Items: {itemsReceived}  ");
        Console.ResetColor();
    }
}
