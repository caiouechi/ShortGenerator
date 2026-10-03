using System.Text.Json;
using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>
/// Posts scheduled on the Publish step, saved so they survive closing the app. Everything the upload needs is kept
/// (file, cover, text, account, options), because the short may belong to a project that is not open when it is due.
/// Only this app sends them: it has to be running, and the computer awake, at the scheduled time.
/// </summary>
public static class ScheduleStore
{
    public sealed class ScheduledPost
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime At { get; set; }
        public string Path { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Language { get; set; }
        public string? CoverPath { get; set; }
        public double? CoverTimeSeconds { get; set; }
        public string Network { get; set; } = "";
        public string Account { get; set; } = "";
        public GaliLunaClient.SendOptions Options { get; set; } = null!;
        public NetworkPost Text { get; set; } = new();
    }

    private static string FilePath => System.IO.Path.Combine(SettingsStore.AppDataFolder, "scheduled.json");

    public static List<ScheduledPost> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<List<ScheduledPost>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { return new(); }
    }

    public static void Save(IEnumerable<ScheduledPost> posts)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.AppDataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(posts.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* a failed save only loses the schedule on restart; the jobs in memory still run */ }
    }
}
