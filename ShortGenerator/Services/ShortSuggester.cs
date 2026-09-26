using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>Asks Claude to pick the most viral-worthy moments of the transcript and explain why.</summary>
public sealed class ShortSuggester
{
    private readonly string _apiKey;
    private readonly string _model;

    public ShortSuggester(string apiKey, string model)
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "claude-opus-5" : model;
    }

    public async Task<SuggestionResponse> SuggestAsync(VideoInfo video, Transcript transcript, int count, int minSeconds, int maxSeconds, IProgress<string> log, CancellationToken ct)
    {
        if (transcript.Segments.Count == 0)
            throw new InvalidOperationException("The transcript is empty. Transcribe the video first.");

        var client = new AnthropicClient { ApiKey = _apiKey };

        var sb = new StringBuilder();
        sb.AppendLine($"Video title: {video.Title}");
        if (!string.IsNullOrWhiteSpace(video.Uploader)) sb.AppendLine($"Creator: {video.Uploader}");
        sb.AppendLine($"Duration: {video.DurationSeconds:F0} seconds");
        if (!string.IsNullOrWhiteSpace(transcript.Language)) sb.AppendLine($"Language: {transcript.Language}");
        sb.AppendLine();
        bool hasLoudness = transcript.Segments.Any(s => s.Loudness is not null);
        sb.AppendLine(hasLoudness
            ? "Timestamped transcript (start - end in seconds, then audio energy above the speaker's normal level):"
            : "Timestamped transcript (start - end in seconds):");
        foreach (var s in transcript.Segments)
            sb.AppendLine(ChatGptExchange.FormatLine(s, hasLoudness));
        sb.AppendLine();
        sb.AppendLine($"Task: propose up to {count} short-form clips (Reels / TikTok / YouTube Shorts) from this video.");
        sb.AppendLine($"Each clip must be between {minSeconds} and {maxSeconds} seconds long, self-contained, and start/end on natural sentence boundaries taken from the timestamps above (do not cut mid-sentence).");
        sb.AppendLine("Clips should not overlap. Order them from most to least viral potential.");
        sb.AppendLine("For each clip explain concretely WHY it could go viral (hook strength, emotion, curiosity gap, controversy, relatability, payoff, quotability, pattern interrupt, etc.), quoting the transcript where useful.");
        sb.AppendLine("Write hooks, captions and hashtags in the same language as the transcript.");
        if (transcript.Segments.Any(s => TranscriptEvents.ContainsReaction(s.Text)))
            sb.AppendLine("Non-speech reactions are marked in brackets, e.g. [laughs], [applause]. Frequent laughter or applause around a passage is a strong signal the moment lands with an audience: weigh it heavily.");
        if (transcript.Segments.Any(s => TranscriptEvents.ContainsIntense(s.Text)))
            sb.AppendLine("Intense moments detected from the audio are marked [big laugh], [loud cheering] or [shouting]. These are the emotional peaks: clips built around them, with a few seconds of setup before, are usually the strongest.");
        if (hasLoudness)
            sb.AppendLine("Each line starts with its audio energy, e.g. (+7 dB): how far its loudest moment rises above the speaker's normal level. 0 to +3 dB is ordinary speech, +4 to +6 animated, +7 and above a burst (laugh, shout, excitement). Treat clusters of high-energy lines as candidate climaxes, and end clips shortly after their energy peak.");

        const string system =
            "You are a senior short-form video editor and growth strategist who has produced hundreds of viral clips for TikTok, Instagram Reels and YouTube Shorts. " +
            "You judge moments by: a strong hook in the first 2 seconds, one clear idea, emotional peak or surprising payoff, quotability, and a natural ending. " +
            "You are honest: if the material is weak you say so in the scores and reasoning rather than hyping it.";

        var schema = BuildSchema();

        log.Report($"Asking {_model} for short suggestions...");
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = _model,
            MaxTokens = 16000,
            System = system,
            Thinking = new ThinkingConfigAdaptive(),
            OutputConfig = new OutputConfig
            {
                Effort = Effort.High,
                Format = new JsonOutputFormat { Schema = schema }
            },
            Messages = [new() { Role = Role.User, Content = sb.ToString() }]
        }, cancellationToken: ct);

        if (response.StopReason?.ToString() == "refusal")
            throw new InvalidOperationException("Claude declined to analyze this content" +
                (response.StopDetails is { } d ? $": {d.Explanation}" : "."));

        var json = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Claude returned an empty response.");

        var parsed = JsonSerializer.Deserialize<SuggestionResponse>(json) ?? new SuggestionResponse();

        // Sanity: clamp to the video and snap to transcript boundaries so the cut never lands mid-word.
        foreach (var s in parsed.Shorts)
        {
            s.StartSeconds = Math.Clamp(s.StartSeconds, 0, Math.Max(0, video.DurationSeconds));
            s.EndSeconds = Math.Clamp(s.EndSeconds, s.StartSeconds + 1, video.DurationSeconds > 0 ? video.DurationSeconds : s.EndSeconds);
            SnapToSegments(s, transcript);
        }
        parsed.Shorts = parsed.Shorts.Where(s => s.Duration >= 3).OrderByDescending(s => s.ViralityScore).ToList();

        log.Report($"Got {parsed.Shorts.Count} suggestions (input {response.Usage.InputTokens} / output {response.Usage.OutputTokens} tokens).");
        return parsed;
    }

    /// <summary>Moves start/end to the nearest transcript boundary (within 1.5 s) so cuts never land mid-word.</summary>
    public static void SnapToSegments(ShortSuggestion s, Transcript t)
    {
        const double tolerance = 1.5;
        var starts = t.Segments.Select(x => x.Start);
        var ends = t.Segments.Select(x => x.End);
        var bestStart = starts.OrderBy(x => Math.Abs(x - s.StartSeconds)).FirstOrDefault(s.StartSeconds);
        var bestEnd = ends.OrderBy(x => Math.Abs(x - s.EndSeconds)).FirstOrDefault(s.EndSeconds);
        if (Math.Abs(bestStart - s.StartSeconds) <= tolerance) s.StartSeconds = Math.Max(0, bestStart - 0.15);
        if (Math.Abs(bestEnd - s.EndSeconds) <= tolerance) s.EndSeconds = bestEnd + 0.25;
    }

    private static Dictionary<string, JsonElement> BuildSchema()
    {
        var shortSchema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "title", "start_seconds", "end_seconds", "hook", "why_viral", "virality_score", "emotion", "suggested_caption", "hashtags" },
            properties = new
            {
                title = new { type = "string", description = "Short, punchy working title for the clip" },
                start_seconds = new { type = "number", description = "Clip start, in seconds, aligned to a transcript segment start" },
                end_seconds = new { type = "number", description = "Clip end, in seconds, aligned to a transcript segment end" },
                hook = new { type = "string", description = "The first line the viewer hears/reads; why they will keep watching" },
                why_viral = new { type = "string", description = "2-4 sentences explaining the viral mechanics of this moment" },
                virality_score = new { type = "integer", minimum = 1, maximum = 10, description = "Honest 1-10 estimate of viral potential" },
                emotion = new { type = "string", description = "Dominant emotion, e.g. surprise, humor, inspiration, outrage, curiosity" },
                suggested_caption = new { type = "string", description = "Post caption to publish with the clip" },
                hashtags = new { type = "array", items = new { type = "string" }, description = "3-6 hashtags without the # symbol" }
            }
        };

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "video_summary", "shorts" }),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                video_summary = new { type = "string", description = "One paragraph summary of what the video is about" },
                shorts = new { type = "array", items = shortSchema }
            })
        };
    }
}
