using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ShortGenerator.Services;

/// <summary>
/// Minimal client for the Higgsfield Cloud API (https://api.higgsfield.ai): upload a reference image, ask an
/// image model to redraw it from a prompt, poll until done, download the result. Developer-only for now: it is
/// active only when HF_API_KEY is set in the environment (optionally HF_API_SECRET), and the model path and the
/// reference-image field can be overridden with HF_IMAGE_MODEL / HF_IMAGE_REF_FIELD while the contract settles.
/// </summary>
public sealed class HiggsfieldClient : IDisposable
{
    public const string BaseUrl = "https://api.higgsfield.ai";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    public string Model { get; }
    private readonly string _refField;

    private HiggsfieldClient(string key, string model, string refField)
    {
        // HF_BASE_URL exists only so a test can point the client at a local stand-in
        var baseUrl = Environment.GetEnvironmentVariable("HF_BASE_URL");
        _http = new HttpClient { BaseAddress = new Uri((string.IsNullOrWhiteSpace(baseUrl) ? BaseUrl : baseUrl.TrimEnd('/')) + "/"), Timeout = TimeSpan.FromMinutes(2) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Key", key);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GalilunaShortGenerator/1.0");
        Model = model; _refField = refField;
    }

    /// <summary>True when the developer credentials are present in the environment.</summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HF_API_KEY"));

    public static HiggsfieldClient? FromEnvironment()
    {
        var key = Environment.GetEnvironmentVariable("HF_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return null;
        var secret = Environment.GetEnvironmentVariable("HF_API_SECRET");
        if (!string.IsNullOrWhiteSpace(secret) && !key.Contains(':')) key = key.Trim() + ":" + secret.Trim();
        var model = Environment.GetEnvironmentVariable("HF_IMAGE_MODEL");
        var field = Environment.GetEnvironmentVariable("HF_IMAGE_REF_FIELD");
        return new HiggsfieldClient(key.Trim(), string.IsNullOrWhiteSpace(model) ? "xai/grok-imagine-image-2.0" : model.Trim(), string.IsNullOrWhiteSpace(field) ? "input_images" : field.Trim());
    }

    /// <summary>Uploads a local image through a presigned URL and returns the public URL models can read.</summary>
    public async Task<string> UploadAsync(string path, CancellationToken ct)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var contentType = ext is ".png" ? "image/png" : ext is ".webp" ? "image/webp" : "image/jpeg";
        using var ask = new StringContent(JsonSerializer.Serialize(new { content_type = contentType }), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("files/generate-upload-url", ask, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Higgsfield upload URL failed ({(int)response.StatusCode}): {body}");
        using var doc = JsonDocument.Parse(body);
        var uploadUrl = doc.RootElement.GetProperty("upload_url").GetString()!;
        var publicUrl = doc.RootElement.GetProperty("public_url").GetString()!;
        using var put = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new ByteArrayContent(await File.ReadAllBytesAsync(path, ct)) };
        put.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        if (doc.RootElement.TryGetProperty("upload_headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
            foreach (var h in headers.EnumerateObject())
                if (!h.Name.Equals("content-type", StringComparison.OrdinalIgnoreCase)) put.Headers.TryAddWithoutValidation(h.Name, h.Value.GetString());
        using var bare = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        using var uploaded = await bare.SendAsync(put, ct);
        if (!uploaded.IsSuccessStatusCode) throw new InvalidOperationException($"Higgsfield upload failed ({(int)uploaded.StatusCode}).");
        return publicUrl;
    }

    /// <summary>Generates a 9:16 image from the prompt (and the reference image when given); returns the result URL.</summary>
    public async Task<string> GenerateImageAsync(string prompt, string? referenceUrl, IProgress<string>? log, CancellationToken ct)
    {
        var input = new Dictionary<string, object?>
        {
            ["prompt"] = prompt,
            ["aspect_ratio"] = "9:16",
            ["quality"] = "high",
            ["resolution"] = "1k",
        };
        if (referenceUrl is not null) input[_refField] = new[] { new { type = "image_url", image_url = referenceUrl } };
        using var content = new StringContent(JsonSerializer.Serialize(new { input }), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(Model, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Higgsfield {Model} refused the request ({(int)response.StatusCode}): {body}");
        string requestId, statusUrl;
        using (var doc = JsonDocument.Parse(body))
        {
            requestId = doc.RootElement.GetProperty("request_id").GetString()!;
            statusUrl = doc.RootElement.TryGetProperty("status_url", out var su) && su.ValueKind == JsonValueKind.String ? su.GetString()! : $"requests/{requestId}/status";
        }
        log?.Report($"Higgsfield: request {requestId} queued on {Model}.");

        var deadline = DateTime.UtcNow.AddMinutes(4);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            using var poll = await _http.GetAsync(statusUrl, ct);
            var status = await poll.Content.ReadAsStringAsync(ct);
            if (!poll.IsSuccessStatusCode) throw new InvalidOperationException($"Higgsfield status failed ({(int)poll.StatusCode}): {status}");
            using var doc = JsonDocument.Parse(status);
            var state = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
            switch (state)
            {
                case "completed":
                    if (doc.RootElement.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0
                        && images[0].TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                        return url.GetString()!;
                    throw new InvalidOperationException("Higgsfield finished but returned no image: " + status);
                case "failed":
                case "nsfw":
                case "canceled":
                case "cancelled":
                    throw new InvalidOperationException($"Higgsfield generation {state}: {status}");
                default:
                    log?.Report($"Higgsfield: {state}...");
                    break;
            }
        }
        throw new TimeoutException("Higgsfield took more than 4 minutes.");
    }

    /// <summary>Downloads the generated image to the given path.</summary>
    public async Task DownloadAsync(string url, string path, CancellationToken ct)
    {
        using var bare = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var bytes = await bare.GetByteArrayAsync(url, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    public void Dispose() => _http.Dispose();
}
