namespace ShortGenerator.Services;

/// <summary>
/// The Activity log and unexpected errors, also written to a daily file
/// (%AppData%\ShortGenerator\logs\yyyy-MM-dd.log), so an error that flashed by can be read back with its details.
/// Files older than 14 days are removed.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    public static string Folder => Path.Combine(SettingsStore.AppDataFolder, "logs");
    private static string Today => Path.Combine(Folder, $"{DateTime.Now:yyyy-MM-dd}.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Today, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch { /* logging never breaks the app */ }
    }

    /// <summary>An exception with its type and stack trace, for errors nobody expected.</summary>
    public static void Error(string where, Exception ex) => Write($"ERROR in {where}: {ex}");

    public static void Prune()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var f in Directory.EnumerateFiles(Folder, "*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14)) File.Delete(f);
        }
        catch { }
    }
}
