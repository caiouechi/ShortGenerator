using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShortGenerator.Services;

public sealed class AppSettings
{
    public string DownloadFolder { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string ToolsFolder { get; set; } = "";
    public string ClaudeModel { get; set; } = "claude-opus-5";
    public string WhisperModel { get; set; } = "Base";
    public string WhisperLanguage { get; set; } = "auto";
    /// <summary>Generate shorts: "original", "en" or "both".</summary>
    public string CaptionLanguage { get; set; } = "both";
    /// <summary>Publish: which videos each account receives, keyed "instagram:41" / "youtube:51" / "tiktok": "original", "en" or "both".</summary>
    public Dictionary<string, string> AccountLanguages { get; set; } = new();
    public bool DetectReactions { get; set; } = true;
    /// <summary>Networks the AI writes post text for (title / description / keywords per platform).</summary>
    public bool PostTextYouTube { get; set; } = true;
    public bool PostTextTikTok { get; set; } = true;
    public bool PostTextInstagram { get; set; } = true;
    /// <summary>Video that was open when the app was last used; reopened automatically at startup.</summary>
    public string? LastVideoPath { get; set; }
    public int SuggestionCount { get; set; } = 6;
    public int MinShortSeconds { get; set; } = 15;
    public int MaxShortSeconds { get; set; } = 60;
    /// <summary>DPAPI-protected, base64 encoded. Never stored in clear text.</summary>
    public string? ProtectedApiKey { get; set; }

    /// <summary>galiluna web app that publishes the shorts (Shorts API). DEV or PRD host, or a custom address.</summary>
    public string GaliLunaBaseUrl { get; set; } = GaliLunaClient.PrdBaseUrl;
    /// <summary>The personal API key from galiluna's Connected apps page, DPAPI-protected like the Anthropic key.</summary>
    public string? ProtectedGaliLunaKey { get; set; }
}

/// <summary>Persists settings under %AppData%\ShortGenerator. The API key is protected with Windows DPAPI.</summary>
public static class SettingsStore
{
    public static string AppDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShortGenerator");

    private static string SettingsPath => Path.Combine(AppDataFolder, "settings.json");

    public static AppSettings Load()
    {
        AppSettings s = new();
        try
        {
            if (File.Exists(SettingsPath))
                s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { /* corrupt settings: fall back to defaults */ }

        var videos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ShortGenerator");
        if (string.IsNullOrWhiteSpace(s.DownloadFolder)) s.DownloadFolder = Path.Combine(videos, "Downloads");
        if (string.IsNullOrWhiteSpace(s.OutputFolder)) s.OutputFolder = Path.Combine(videos, "Shorts");
        if (string.IsNullOrWhiteSpace(s.ToolsFolder)) s.ToolsFolder = Path.Combine(AppDataFolder, "tools");
        return s;
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataFolder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void SetApiKey(AppSettings settings, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) { settings.ProtectedApiKey = null; return; }
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey.Trim()), null, DataProtectionScope.CurrentUser);
        settings.ProtectedApiKey = Convert.ToBase64String(bytes);
    }

    public static void SetGaliLunaKey(AppSettings settings, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) { settings.ProtectedGaliLunaKey = null; return; }
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey.Trim()), null, DataProtectionScope.CurrentUser);
        settings.ProtectedGaliLunaKey = Convert.ToBase64String(bytes);
    }

    public static string? GetGaliLunaKey(AppSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ProtectedGaliLunaKey)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(settings.ProtectedGaliLunaKey), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return null; }
    }

    /// <summary>A ready client, or null when galiluna is not set up yet.</summary>
    public static GaliLunaClient? CreateGaliLunaClient(AppSettings settings)
    {
        var key = GetGaliLunaKey(settings);
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(settings.GaliLunaBaseUrl)) return null;
        return new GaliLunaClient(settings.GaliLunaBaseUrl, key);
    }

    /// <summary>Resolves the API key: stored key first, then the ANTHROPIC_API_KEY environment variable.</summary>
    public static string? GetApiKey(AppSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.ProtectedApiKey))
        {
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(settings.ProtectedApiKey), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { /* different user / machine: ignore */ }
        }
        var env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }
}
