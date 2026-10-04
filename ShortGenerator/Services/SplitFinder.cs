using System.Globalization;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>
/// Finds the second subject of a short: what is being shown, watched or played besides the person talking (the
/// chess board, the match on the pitch, the game on screen). Auto split shows it at the top of the short while the
/// main picture follows the speaker. Claude looks at three frames when an API key is set; otherwise the side of the
/// frame away from the faces is taken.
/// </summary>
public sealed class SplitFinder
{
    private readonly FfmpegRunner _ffmpeg;
    public SplitFinder(FfmpegRunner ffmpeg) => _ffmpeg = ffmpeg;

    /// <param name="Box">Where the subject is, in percent of the source frame.</param>
    /// <param name="Subject">What it is, as a short phrase ("the chess board").</param>
    /// <param name="Source">claude | faces</param>
    public sealed record Result(FocusArea Box, string Subject, string Source);

    public async Task<Result?> FindAsync(VideoInfo video, ShortSuggestion s, string? apiKey, string? model, FaceFramer.Analysis? faces, IProgress<string> log, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var workDir = Path.Combine(Path.GetTempPath(), $"shortgen_split_{Guid.NewGuid():N}");
            Directory.CreateDirectory(workDir);
            try
            {
                var frames = new List<string>();
                foreach (double f in new[] { 0.2, 0.5, 0.8 })
                {
                    var path = Path.Combine(workDir, $"f{frames.Count}.jpg");
                    await _ffmpeg.RunAsync(new[]
                    {
                        "-y", "-ss", (s.StartSeconds + f * s.Duration).ToString("0.###", CultureInfo.InvariantCulture), "-i", video.FilePath,
                        "-frames:v", "1", "-vf", "scale=960:-2", "-q:v", "4", path,
                    }, null, null, 0, ct);
                    if (File.Exists(path)) frames.Add(path);
                }
                if (frames.Count > 0)
                {
                    var found = await AskClaudeAsync(frames, apiKey, model, log, ct);
                    if (found is not null) return found;
                    log.Report("Auto split: Claude sees no second subject in these frames (people talking only).");
                    return null;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { log.Report($"Auto split: Claude could not look at the frames ({ex.Message}); using the faces instead."); }
            finally { try { Directory.Delete(workDir, true); } catch { } }
        }
        return FromFaces(faces);
    }

    private static async Task<Result?> AskClaudeAsync(List<string> frames, string apiKey, string? model, IProgress<string> log, CancellationToken ct)
    {
        var client = new AnthropicClient { ApiKey = apiKey };
        var content = new List<ContentBlockParam>();
        foreach (var f in frames)
            content.Add(new ImageBlockParam
            {
                Source = new Base64ImageSource { Data = Convert.ToBase64String(await File.ReadAllBytesAsync(f, ct)), MediaType = MediaType.ImageJpeg },
            });
        content.Add(new TextBlockParam
        {
            Text = "These are three frames from the same video, a few seconds apart. A vertical short is being cut from it: the main picture " +
                   "follows the person talking, and a second window at the top should show what they are talking about, watching or playing: " +
                   "a chess board, a football match on the pitch, a game being played on screen, a product on the table, slides on a screen. " +
                   "Find that second subject and give one box, in percent of the frame (x and y of its top-left corner, width and height), " +
                   "that contains it in all three frames, tight with a small margin. Keep the speaker's face out of the box when the subject " +
                   "is somewhere else. A face camera inset on a game stream is the speaker, not the subject. " +
                   "If the frames only show people talking and nothing else worth a second window, set found to false.",
        });
        var schema = new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "found", "subject", "x", "y", "w", "h" }),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                found = new { type = "boolean", description = "true when there is a second subject worth showing" },
                subject = new { type = "string", description = "what the box shows, 2-5 words, e.g. 'the chess board'" },
                x = new { type = "number", description = "left edge, percent of the frame width" },
                y = new { type = "number", description = "top edge, percent of the frame height" },
                w = new { type = "number", description = "width, percent" },
                h = new { type = "number", description = "height, percent" },
            }),
        };
        string m = string.IsNullOrWhiteSpace(model) ? "claude-opus-5" : model;
        log.Report($"Auto split: asking {m} what the frames show...");
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = m,
            MaxTokens = 2000,
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } },
            Messages = [new() { Role = Role.User, Content = content }],
        }, cancellationToken: ct);
        var json = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.GetProperty("found").GetBoolean()) return null;
        double x = Math.Clamp(r.GetProperty("x").GetDouble(), 0, 99), y = Math.Clamp(r.GetProperty("y").GetDouble(), 0, 99);
        double w = Math.Clamp(r.GetProperty("w").GetDouble(), 5, 100 - x), h = Math.Clamp(r.GetProperty("h").GetDouble(), 5, 100 - y);
        var subject = r.GetProperty("subject").GetString() is { Length: > 0 } sub ? sub : "the second subject";
        log.Report($"Auto split: {subject} at x={x:F0}% y={y:F0}% w={w:F0}% h={h:F0}%.");
        return new Result(new FocusArea { X = Math.Round(x, 1), Y = Math.Round(y, 1), W = Math.Round(w, 1), H = Math.Round(h, 1) }, subject, "claude");
    }

    /// <summary>Without Claude: the half of the frame the faces are not in (null when there are no faces to go by).</summary>
    private static Result? FromFaces(FaceFramer.Analysis? faces)
    {
        var all = faces?.Frames.SelectMany(f => f.Faces).ToList();
        if (all is null || all.Count == 0) return null;
        bool left = all.Average(f => f.X) > 50; // faces on the right: show the left side
        // half the width at 16:9, centred vertically, so the window really zooms into that half
        return new Result(new FocusArea { X = left ? 0 : 50, Y = 25, W = 50, H = 50 }, left ? "the left side of the frame" : "the right side of the frame", "faces");
    }

    /// <summary>
    /// The crop of a 16:9 split window (CropX, CropY, Zoom) that shows the box: the window is the largest 16:9 crop
    /// of the source divided by the zoom, so the zoom is the largest that still covers the box, never more than 4x.
    /// </summary>
    public static (double CropX, double CropY, double Zoom) WindowFor(FocusArea box, int srcW, int srcH)
    {
        const double aspect = 16.0 / 9;
        double fullW, fullH;
        if ((double)srcW / srcH > aspect) { fullH = srcH; fullW = srcH * aspect; } else { fullW = srcW; fullH = srcW / aspect; }
        double boxW = box.W / 100 * srcW * 1.06, boxH = box.H / 100 * srcH * 1.06; // a little air around the subject
        double zoom = Math.Clamp(Math.Min(fullW / Math.Max(1, boxW), fullH / Math.Max(1, boxH)), 1, 4);
        return (Math.Round(box.CenterX, 1), Math.Round(box.CenterY, 1), Math.Round(zoom, 2));
    }
}
