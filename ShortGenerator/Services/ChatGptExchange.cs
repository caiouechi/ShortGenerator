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
        => BuildPrompt(video, transcript, count, minSeconds, maxSeconds, default);

    /// <summary>Platform guidance shared by the ChatGPT prompt and the Claude prompt.</summary>
    public static IEnumerable<string> PlatformRules(PostTargets targets)
    {
        if (targets.YouTube)
            yield return "youtube: a searchable title (max 100 characters, main keyword first, no clickbait lies), a description of 2-4 short paragraphs where the first two lines carry the keywords and the promise (they show before 'more'), ending with a call to action, and 8-15 keyword tags (single words or short phrases, no #).";
        if (targets.TikTok)
            yield return "tiktok: a caption of at most 150 characters that reads like a native TikTok hook (casual, curiosity or bold claim, can include 1-2 emojis), and 3-6 hashtags without # mixing one broad (fyp-style), two niche and the topic.";
        if (targets.Instagram)
            yield return "instagram: a Reels caption with a first line that stops the scroll, 1-3 short lines of context, a question or call to action, and 5-10 hashtags without # (mix of niche and medium-size tags, no banned or spammy ones).";
    }

    public static string BuildPrompt(VideoInfo video, Transcript transcript, int count, int minSeconds, int maxSeconds, PostTargets targets)
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
        if (count > 0)
            sb.AppendLine($"Propose up to {count} short-form clips from the transcript below.");
        else
            sb.AppendLine("Propose the clips YOU judge worth cutting from the transcript below. Decide the number yourself: include every moment that would genuinely make a strong short, and leave out filler. There is no fixed count - it might be 3, it might be 15. Do not force a number.");
        sb.AppendLine($"Rules:");
        sb.AppendLine($"- Read the ENTIRE transcript from the first timestamp to the last BEFORE choosing. The strongest moments are often in the middle or the end, not the opening. Spread your picks across the whole video (early, middle AND late sections) and cover the full duration - do NOT just take the first few minutes.");
        sb.AppendLine($"- Each clip must be between {minSeconds} and {maxSeconds} seconds long and self-contained (understandable without the rest of the video).");
        sb.AppendLine("- start_seconds and end_seconds MUST be taken from the timestamps in the transcript, so the clip starts and ends on natural sentence boundaries. Never cut mid-sentence.");
        sb.AppendLine("- Clips must not overlap. Order them from most to least viral potential.");
        sb.AppendLine("- For every clip explain concretely WHY it could go viral (hook strength, emotion, curiosity gap, controversy, relatability, payoff, quotability, pattern interrupt...). Quote the transcript where useful.");
        sb.AppendLine("- Rate each clip's viral potential from 1 to 10 (10 = exceptional). Use the whole scale.");
        sb.AppendLine("- Write title, hook, caption and hashtags in the same language as the transcript.");
        if (targets.Any)
        {
            sb.AppendLine("- For EACH clip also write ready-to-publish post text per network, tailored to how that platform is searched and consumed:");
            foreach (var rule in PlatformRules(targets)) sb.AppendLine("  - " + rule);
        }
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
        sb.AppendLine("Reply with ONLY a JSON object, no markdown, no commentary before or after, no citation or source markers inside the strings, exactly in this shape:");
        sb.AppendLine(OutputShape(targets));
        sb.AppendLine();
        sb.AppendLine(hasLoudness ? "# Transcript (start - end in seconds, then audio energy above normal speech)" : "# Transcript (start - end in seconds)");
        foreach (var s in transcript.Segments)
            sb.AppendLine(FormatLine(s, hasLoudness));
        return sb.ToString();
    }

    /// <summary>The JSON shape shown to the assistant, with a block per requested network.</summary>
    public static string OutputShape(PostTargets targets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"video_summary\": \"one paragraph about what the video is about\",");
        sb.AppendLine("  \"shorts\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"title\": \"short punchy working title\",");
        sb.AppendLine("      \"start_seconds\": 123.4,");
        sb.AppendLine("      \"end_seconds\": 168.9,");
        sb.AppendLine("      \"hook\": \"the first line the viewer hears or reads\",");
        sb.AppendLine("      \"why_viral\": \"2-4 sentences explaining the viral mechanics of this moment\",");
        sb.AppendLine("      \"virality_score\": 8,");
        sb.AppendLine("      \"emotion\": \"surprise | humor | inspiration | outrage | curiosity | ...\",");
        sb.AppendLine("      \"suggested_caption\": \"generic post caption to publish with the clip\",");
        sb.Append("      \"hashtags\": [\"tag1\", \"tag2\", \"tag3\"]");
        if (targets.YouTube)
        {
            sb.AppendLine(",");
            sb.Append("      \"youtube\": { \"title\": \"SEO title, max 100 chars\", \"description\": \"2-4 short paragraphs, keywords in the first two lines, ends with a call to action\", \"tags\": [\"keyword one\", \"keyword two\"] }");
        }
        if (targets.TikTok)
        {
            sb.AppendLine(",");
            sb.Append("      \"tiktok\": { \"title\": \"\", \"description\": \"caption up to 150 chars, native TikTok tone\", \"tags\": [\"fyp\", \"niche1\", \"niche2\", \"topic\"] }");
        }
        if (targets.Instagram)
        {
            sb.AppendLine(",");
            sb.Append("      \"instagram\": { \"title\": \"\", \"description\": \"Reels caption: scroll-stopping first line, context, call to action\", \"tags\": [\"tag1\", \"tag2\", \"tag3\", \"tag4\", \"tag5\"] }");
        }
        sb.AppendLine();
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
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
    /// ChatGPT sometimes leaves source markers inside string values, e.g. <c>:chatgpt-content-reference{index="0"}</c>,
    /// <c>:contentReference[oaicite:0]{index=0}</c> or <c>【4†source】</c>. Their unescaped quotes and braces break the
    /// JSON, and they are never wanted in a caption, so they are removed before parsing.
    /// </summary>
    public static string StripCitations(string text)
    {
        text = Regex.Replace(text, @"\s*:chatgpt-content-reference\{[^{}]*\}", "");
        text = Regex.Replace(text, @"\s*:contentReference\[[^\]]*\](\{[^{}]*\})?", "");
        text = Regex.Replace(text, @"\s*\[oaicite:\d+\]", "");
        text = Regex.Replace(text, @"\s*【[^】]*】", "");
        return text;
    }

    /// <summary>
    /// Parses the assistant's reply. Tolerates markdown code fences, text around the JSON,
    /// times given as "mm:ss" / "hh:mm:ss" strings, and hashtags with a leading '#'.
    /// </summary>
    public static SuggestionResponse ParseResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new FormatException("The pasted text is empty.");

        var text = StripCitations(raw).Trim();
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
            s.Youtube = Network(e, "youtube");
            s.Tiktok = Network(e, "tiktok");
            s.Instagram = Network(e, "instagram");
            if (string.IsNullOrWhiteSpace(s.Title)) s.Title = $"Clip {result.Shorts.Count + 1}";
            if (s.EndSeconds > s.StartSeconds) result.Shorts.Add(s);
        }
        if (result.Shorts.Count == 0) throw new FormatException("No usable clips were found in the pasted text.");
        return result;
    }

    /// <summary>Reads a per-network block; tolerant of "caption"/"keywords"/"hashtags" naming and string tag lists.</summary>
    private static NetworkPost? Network(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var n) || n.ValueKind != JsonValueKind.Object) return null;
        var post = new NetworkPost
        {
            Title = Str(n, "title"),
            Description = Str(n, "description", "caption", "text"),
        };
        foreach (var key in new[] { "tags", "keywords", "hashtags" })
        {
            if (!n.TryGetProperty(key, out var tags)) continue;
            if (tags.ValueKind == JsonValueKind.Array)
                post.Tags.AddRange(tags.EnumerateArray().Select(t => t.ToString().Trim().TrimStart('#')).Where(t => t.Length > 0));
            else if (tags.ValueKind == JsonValueKind.String)
            {
                // "keyword one, keyword two" keeps phrases; "#canada #eu #news" splits on spaces
                var raw = tags.GetString()!;
                var separators = raw.Contains(',') || raw.Contains('\n') ? new[] { ',', '\n' } : new[] { ' ', '\t' };
                post.Tags.AddRange(raw.Split(separators, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().TrimStart('#')).Where(t => t.Length > 0));
            }
        }
        post.Tags = post.Tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return string.IsNullOrWhiteSpace(post.Title) && string.IsNullOrWhiteSpace(post.Description) && post.Tags.Count == 0 ? null : post;
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
