using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>
/// Manual workflow with an external chat assistant (ChatGPT, etc.): build a prompt the user pastes into
/// the chat, then parse the assistant's JSON answer back into short suggestions.
/// </summary>
public static class ChatGptExchange
{
    public static string BuildPrompt(VideoInfo video, Transcript transcript, int count, int minSeconds, int maxSeconds)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a senior short-form video editor and growth strategist who has produced hundreds of viral clips for TikTok, Instagram Reels and YouTube Shorts.");
        sb.AppendLine("You judge moments by: a strong hook in the first 2 seconds, one clear idea, an emotional peak or surprising payoff, quotability, and a natural ending.");
        sb.AppendLine("Be honest: if the material is weak, say so in the scores and reasoning instead of hyping it.");
        sb.AppendLine();
        sb.AppendLine("# Video");
        sb.AppendLine($"Title: {video.Title}");
        if (!string.IsNullOrWhiteSpace(video.Uploader)) sb.AppendLine($"Creator: {video.Uploader}");
        sb.AppendLine($"Duration: {video.DurationSeconds:F0} seconds");
        if (!string.IsNullOrWhiteSpace(transcript.Language)) sb.AppendLine($"Language: {transcript.Language}");
        sb.AppendLine();
        sb.AppendLine("# Task");
        sb.AppendLine($"Propose up to {count} short-form clips from the transcript below.");
        sb.AppendLine($"Rules:");
        sb.AppendLine($"- Each clip must be between {minSeconds} and {maxSeconds} seconds long and self-contained (understandable without the rest of the video).");
        sb.AppendLine("- start_seconds and end_seconds MUST be taken from the timestamps in the transcript, so the clip starts and ends on natural sentence boundaries. Never cut mid-sentence.");
        sb.AppendLine("- Clips must not overlap. Order them from most to least viral potential.");
        sb.AppendLine("- For every clip explain concretely WHY it could go viral (hook strength, emotion, curiosity gap, controversy, relatability, payoff, quotability, pattern interrupt...). Quote the transcript where useful.");
        sb.AppendLine("- Rate each clip's viral potential from 1 to 10 (10 = exceptional). Use the whole scale.");
        sb.AppendLine("- Write title, hook, caption and hashtags in the same language as the transcript.");
        if (transcript.Segments.Any(s => TranscriptEvents.ContainsReaction(s.Text)))
            sb.AppendLine("- Non-speech reactions are marked in brackets, e.g. [laughs], [applause], [cheering]. Frequent laughter or applause around a passage is a strong signal that the moment lands with an audience: weigh it heavily.");
        if (transcript.Segments.Any(s => TranscriptEvents.ContainsIntense(s.Text)))
            sb.AppendLine("- Intense moments detected from the audio are marked [big laugh], [loud cheering] or [shouting] (anger, screams, excitement). These are the emotional peaks of the video: clips built around them, with a few seconds of setup before, are usually the strongest.");
        bool hasLoudness = transcript.Segments.Any(s => s.Loudness is not null);
        if (hasLoudness)
            sb.AppendLine("- Each line starts with its audio energy, e.g. (+7 dB): how far the loudest moment of that line rises above the speaker's normal speaking level. " +
                          "0 to +3 dB is ordinary speech, +4 to +6 dB is animated, +7 dB and above is a burst (laugh, shout, excitement, someone talking over another). " +
                          "Use it as a sensor for emotional intensity and pacing: clusters of high-energy lines are candidate climaxes, and a clip should usually end shortly after its energy peak.");
        sb.AppendLine();
        sb.AppendLine("# Output format");
        sb.AppendLine("Reply with ONLY a JSON object, no markdown, no commentary before or after, exactly in this shape:");
        sb.AppendLine("""
{
  "video_summary": "one paragraph about what the video is about",
  "shorts": [
    {
      "title": "short punchy working title",
      "start_seconds": 123.4,
      "end_seconds": 168.9,
      "hook": "the first line the viewer hears or reads",
      "why_viral": "2-4 sentences explaining the viral mechanics of this moment",
      "virality_score": 8,
      "emotion": "surprise | humor | inspiration | outrage | curiosity | ...",
      "suggested_caption": "post caption to publish with the clip",
      "hashtags": ["tag1", "tag2", "tag3"]
    }
  ]
}
""");
        sb.AppendLine();
        sb.AppendLine(hasLoudness ? "# Transcript (start - end in seconds, then audio energy above normal speech)" : "# Transcript (start - end in seconds)");
        foreach (var s in transcript.Segments)
            sb.AppendLine(FormatLine(s, hasLoudness));
        return sb.ToString();
    }

    /// <summary>One transcript line for a prompt: "[12.4 - 17.8] (+7 dB) text". Shared with the Claude prompt.</summary>
    public static string FormatLine(TranscriptSegment s, bool withLoudness)
    {
        var range = $"[{s.Start.ToString("F1", CultureInfo.InvariantCulture)} - {s.End.ToString("F1", CultureInfo.InvariantCulture)}]";
        var energy = withLoudness ? $" ({(s.Loudness is { } l ? $"{(l >= 0 ? "+" : "")}{Math.Round(l)} dB" : "n/a")})" : "";
        return $"{range}{energy} {s.Text.Trim()}";
    }

    /// <summary>
    /// Parses the assistant's reply. Tolerates markdown code fences, text around the JSON,
    /// times given as "mm:ss" / "hh:mm:ss" strings, and hashtags with a leading '#'.
    /// </summary>
    public static SuggestionResponse ParseResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new FormatException("The pasted text is empty.");

        var text = raw.Trim();
        // strip ```json fences
        text = Regex.Replace(text, @"^```[a-zA-Z]*\s*", "", RegexOptions.Multiline);
        text = text.Replace("```", "");
        int first = text.IndexOf('{');
        int last = text.LastIndexOf('}');
        if (first < 0 || last <= first) throw new FormatException("Could not find a JSON object in the pasted text. Ask ChatGPT to reply with only the JSON.");
        text = text[first..(last + 1)];

        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        var result = new SuggestionResponse { VideoSummary = Str(root, "video_summary", "summary") };

        JsonElement shorts;
        if (!root.TryGetProperty("shorts", out shorts) && !root.TryGetProperty("clips", out shorts) && !root.TryGetProperty("suggestions", out shorts))
        {
            if (root.ValueKind == JsonValueKind.Array) shorts = root;
            else throw new FormatException("The JSON has no \"shorts\" array.");
        }
        if (shorts.ValueKind != JsonValueKind.Array) throw new FormatException("\"shorts\" is not an array.");

        foreach (var e in shorts.EnumerateArray())
        {
            var s = new ShortSuggestion
            {
                Title = Str(e, "title", "name"),
                StartSeconds = Seconds(e, "start_seconds", "start", "start_time"),
                EndSeconds = Seconds(e, "end_seconds", "end", "end_time"),
                Hook = Str(e, "hook"),
                WhyViral = Str(e, "why_viral", "why", "reason", "reasoning"),
                ViralityScore = Score(e, "virality_score", "score", "rating", "viral_score"),
                Emotion = Str(e, "emotion"),
                SuggestedCaption = Str(e, "suggested_caption", "caption"),
            };
            if (e.TryGetProperty("hashtags", out var tags))
            {
                if (tags.ValueKind == JsonValueKind.Array)
                    s.Hashtags = tags.EnumerateArray().Select(t => t.ToString().Trim().TrimStart('#')).Where(t => t.Length > 0).ToList();
                else if (tags.ValueKind == JsonValueKind.String)
                    s.Hashtags = tags.GetString()!.Split(new[] { ' ', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.TrimStart('#')).ToList();
            }
            if (string.IsNullOrWhiteSpace(s.Title)) s.Title = $"Clip {result.Shorts.Count + 1}";
            if (s.EndSeconds > s.StartSeconds) result.Shorts.Add(s);
        }
        if (result.Shorts.Count == 0) throw new FormatException("No usable clips were found in the pasted text.");
        return result;
    }

    private static string Str(JsonElement e, params string[] names)
    {
        foreach (var n in names)
            if (e.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null) return v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString();
        return "";
    }

    private static int Score(JsonElement e, params string[] names)
    {
        foreach (var n in names)
        {
            if (!e.TryGetProperty(n, out var v)) continue;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return (int)Math.Clamp(Math.Round(d), 1, 10);
            if (v.ValueKind == JsonValueKind.String)
            {
                var m = Regex.Match(v.GetString() ?? "", @"\d+(\.\d+)?");
                if (m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sd)) return (int)Math.Clamp(Math.Round(sd), 1, 10);
            }
        }
        return 0;
    }

    private static double Seconds(JsonElement e, params string[] names)
    {
        foreach (var n in names)
        {
            if (!e.TryGetProperty(n, out var v)) continue;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
            if (v.ValueKind == JsonValueKind.String && TryParseTime(v.GetString() ?? "", out var t)) return t;
        }
        return 0;
    }

    /// <summary>Accepts "83", "83.5", "1:23", "01:23.5", "0:01:23".</summary>
    public static bool TryParseTime(string s, out double seconds)
    {
        seconds = 0;
        s = s.Trim();
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain)) { seconds = plain; return true; }
        var parts = s.Split(':');
        if (parts.Length is < 2 or > 3) return false;
        double total = 0;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return false;
            total = total * 60 + v;
        }
        seconds = total;
        return true;
    }
}
