using System.Text;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShortGenerator.Services;

/// <summary>
/// Client for galiluna's Shorts API: the way this app publishes a rendered short to the Instagram
/// and TikTok accounts the user connected on galiluna.com. Auth is a personal API key created on
/// galiluna's "Connected apps" page; no password ever passes through here. Contract:
/// C:\PersonalRepo\CampaignStudio\docs\shorts-api.md.
/// </summary>
public sealed class GaliLunaClient : IDisposable
{
    public const string DevBaseUrl = "https://landingpagebuilder.torontodeveloper.ca";
    public const string PrdBaseUrl = "https://galiluna.com";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public GaliLunaClient(string baseUrl, string apiKey)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(6) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GalilunaShortGenerator/1.0");
    }

    public string BaseUrl => _http.BaseAddress!.ToString().TrimEnd('/');

    // ------------------------------------------------------------------ models (API shapes)

    public sealed class Accounts
    {
        public UserInfo User { get; set; } = new();
        public OrgInfo Organization { get; set; } = new();
        public List<Account> Instagram { get; set; } = new();
        public TikTokAccount? Tiktok { get; set; }
        public YouTubeInfo Youtube { get; set; } = new();
        public Limits Limits { get; set; } = new();

        public string Summary =>
            $"Connected as {User.Email} at {Organization.Name}: " +
            $"{Instagram.Count} Instagram account{(Instagram.Count == 1 ? "" : "s")}, " +
            $"{Youtube.Channels.Count} YouTube channel{(Youtube.Channels.Count == 1 ? "" : "s")}, " +
            (Tiktok is null ? "TikTok not connected." : $"TikTok {Tiktok.Label}.");
    }
    public sealed class YouTubeInfo
    {
        public List<Account> Channels { get; set; } = new();
        public List<string> PrivacyLevels { get; set; } = new();
        public string? Note { get; set; }
    }
    public sealed class UserInfo { public int Id { get; set; } public string Email { get; set; } = ""; }
    public sealed class OrgInfo { public int Id { get; set; } public string Name { get; set; } = ""; }
    public sealed class Account { public int Id { get; set; } public string Label { get; set; } = ""; }
    public sealed class TikTokAccount
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
        public bool DirectPostingAvailable { get; set; }
        public List<string> Modes { get; set; } = new();
        public List<string> PrivacyLevels { get; set; } = new();
    }
    public sealed class Limits { public long MaxVideoBytes { get; set; } public int MaxCaptionLength { get; set; } public string VideoFormat { get; set; } = ""; }

    public sealed class ShortInfo
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Caption { get; set; } = "";
        public string VideoUrl { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public List<Publication> Publications { get; set; } = new();
        public bool AllPublished { get; set; }
        public bool AnyProcessing => Publications.Any(p => p.Status == "processing");
    }
    public sealed class Publication
    {
        public int Id { get; set; }
        public string Network { get; set; } = "";
        public int? AccountId { get; set; }
        public string? Account { get; set; }
        public string Status { get; set; } = "";
        public string? RemoteId { get; set; }
        public string? Permalink { get; set; }
        public string? Error { get; set; }
    }

    public sealed record SendOptions(
        IReadOnlyList<int> InstagramAccountIds,
        string TikTokMode,              // off | drafts | direct
        string? TikTokPrivacyLevel,
        bool TikTokDisableComment,
        IReadOnlyList<int>? YouTubeChannelIds = null,
        string YouTubePrivacy = "public");   // public | unlisted | private

    /// <summary>Thrown for any non-2xx answer, with the sentence galiluna wrote for the user.</summary>
    public sealed class GaliLunaException : Exception
    {
        public int StatusCode { get; }
        public GaliLunaException(int statusCode, string message) : base(message) { StatusCode = statusCode; }
    }

    // ------------------------------------------------------------------ calls

    /// <summary>GET /api/shorts/accounts. Also the "Test connection" call.</summary>
    public async Task<Accounts> GetAccountsAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("api/shorts/accounts", ct);
        return await ReadAsync<Accounts>(response, ct);
    }

    /// <summary>POST /api/shorts: uploads the MP4 and publishes it. Allow several minutes.</summary>
    public async Task<ShortInfo> SendAsync(string videoPath, string title, string caption, IReadOnlyList<string> hashtags,
        SendOptions options, IProgress<double>? progress, CancellationToken ct, string? coverPath = null, double? coverTimeSeconds = null)
    {
        await using var file = File.OpenRead(videoPath);
        using var form = new MultipartFormDataContent();
        var videoContent = new ProgressStreamContent(file, progress);
        videoContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(videoContent, "video", Path.GetFileName(videoPath));
        // Optional cover / thumbnail (JPEG). galiluna makes it the Instagram Reel cover and the YouTube
        // thumbnail; older servers ignore unknown parts.
        if (coverPath is not null && File.Exists(coverPath))
        {
            var cover = new ByteArrayContent(await File.ReadAllBytesAsync(coverPath, ct));
            cover.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(cover, "cover", Path.GetFileName(coverPath));
            // TikTok cannot take an image cover; when the cover is a frame, galiluna points TikTok at that frame
            if (coverTimeSeconds is { } t)
                form.Add(new StringContent(t.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)), "coverTimeSeconds");
        }
        form.Add(new StringContent(title), "title");
        form.Add(new StringContent(caption), "caption");
        foreach (var tag in hashtags.Where(t => !string.IsNullOrWhiteSpace(t)))
            form.Add(new StringContent(tag.Trim().TrimStart('#')), "hashtags");
        foreach (var id in options.InstagramAccountIds)
            form.Add(new StringContent(id.ToString()), "instagramAccountIds");
        form.Add(new StringContent(options.TikTokMode), "tiktok");
        if (options.TikTokMode == "direct" && !string.IsNullOrWhiteSpace(options.TikTokPrivacyLevel))
            form.Add(new StringContent(options.TikTokPrivacyLevel), "tiktokPrivacyLevel");
        form.Add(new StringContent(options.TikTokDisableComment ? "true" : "false"), "tiktokDisableComment");
        foreach (var id in options.YouTubeChannelIds ?? Array.Empty<int>())
            form.Add(new StringContent(id.ToString()), "youtubeChannelIds");
        form.Add(new StringContent(options.YouTubePrivacy), "youtubePrivacy");
        form.Add(new StringContent("short-generator"), "source");

        using var response = await _http.PostAsync("api/shorts", form, ct);
        return await ReadAsync<ShortInfo>(response, ct);
    }

    /// <summary>POST /api/shorts/signout: revokes the key making the call (204). A 401 means it was already gone.</summary>
    public async Task SignOutAsync(CancellationToken ct)
    {
        using var response = await _http.PostAsync("api/shorts/signout", new StringContent(""), ct);
        if (response.IsSuccessStatusCode || (int)response.StatusCode == 401) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new GaliLunaException((int)response.StatusCode, Describe((int)response.StatusCode, body));
    }

    // ------------------------------------------------------------------ device-code sign-in (anonymous)

    /// <summary>Answer of POST /api/connected-apps/device/start.</summary>
    public sealed record DeviceStart(string UserCode, string PollToken, string VerificationUrl, string VerificationUrlBase, int ExpiresInSeconds, int IntervalSeconds);

    /// <summary>Answer of POST /api/connected-apps/device/poll: status is pending, approved (with the key, once), denied, expired or claimed.</summary>
    public sealed record DevicePoll(string Status, string? Key);

    private static readonly HttpClient Anonymous = CreateAnonymous();

    private static HttpClient CreateAnonymous()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GalilunaShortGenerator/1.0");
        return http;
    }

    /// <summary>Starts a device-code sign-in: galiluna returns a short code the person approves in the browser.</summary>
    public static async Task<DeviceStart> DeviceStartAsync(string baseUrl, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { app = "short-generator", device = Environment.MachineName }, Json);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await Anonymous.PostAsync(baseUrl.TrimEnd('/') + "/api/connected-apps/device/start", content, ct);
        var start = await ReadAsync<DeviceStart>(response, ct);
        if (string.IsNullOrWhiteSpace(start.UserCode) || string.IsNullOrWhiteSpace(start.PollToken))
            throw new GaliLunaException((int)response.StatusCode, "galiluna did not return a sign-in code.");
        return start with { IntervalSeconds = Math.Max(1, start.IntervalSeconds), ExpiresInSeconds = Math.Max(30, start.ExpiresInSeconds) };
    }

    /// <summary>One poll of the device-code sign-in. The key comes back exactly once, on the first "approved" answer.</summary>
    public static async Task<DevicePoll> DevicePollAsync(string baseUrl, string pollToken, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { pollToken }, Json);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await Anonymous.PostAsync(baseUrl.TrimEnd('/') + "/api/connected-apps/device/poll", content, ct);
        var poll = await ReadAsync<DevicePoll>(response, ct);
        return poll with { Status = (poll.Status ?? "").Trim().ToLowerInvariant() };
    }

    /// <summary>GET /api/shorts/{id}: current state; TikTok rows still processing are re-checked by galiluna.</summary>
    public async Task<ShortInfo> GetShortAsync(int id, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"api/shorts/{id}", ct);
        return await ReadAsync<ShortInfo>(response, ct);
    }

    // ------------------------------------------------------------------ plumbing

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new GaliLunaException((int)response.StatusCode, Describe((int)response.StatusCode, body));
        return JsonSerializer.Deserialize<T>(body, Json) ?? throw new GaliLunaException((int)response.StatusCode, "galiluna returned an empty answer.");
    }

    /// <summary>The API answers RFC 7807 problem details; the customer-facing sentence is in detail (validation) or title.</summary>
    private static string Describe(int status, string body)
    {
        string? text = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(d.GetString())) text = d.GetString();
                else if (root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String) text = t.GetString();
            }
        }
        catch (JsonException) { }
        return status switch
        {
            401 => "galiluna did not accept the API key. Create a new one under Connected apps on galiluna and paste it in Settings.",
            403 => "This galiluna user has no role in an organization.",
            413 => text ?? "The video is too large for galiluna (48 MB limit).",
            429 => "galiluna is rate-limiting this computer. Wait a few minutes and try again.",
            _ => text ?? $"galiluna answered {status}.",
        };
    }

    /// <summary>StreamContent that reports upload progress, so a 40 MB send shows movement instead of a frozen bar.</summary>
    private sealed class ProgressStreamContent : HttpContent
    {
        private readonly Stream _source;
        private readonly IProgress<double>? _progress;
        public ProgressStreamContent(Stream source, IProgress<double>? progress) { _source = source; _progress = progress; }
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            var buffer = new byte[81920];
            long sent = 0, total = _source.Length;
            int read;
            while ((read = await _source.ReadAsync(buffer)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read));
                sent += read;
                _progress?.Report(total == 0 ? 1 : (double)sent / total);
            }
        }
        protected override bool TryComputeLength(out long length) { length = _source.Length; return true; }
    }

    public void Dispose() => _http.Dispose();
}
