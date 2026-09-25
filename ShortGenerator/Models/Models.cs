using System.Text.Json.Serialization;

namespace ShortGenerator.Models;

public enum VideoSource { YouTube, Instagram, TikTok, Other, LocalFile }

public sealed class VideoInfo
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Uploader { get; set; } = "";
    public double DurationSeconds { get; set; }
    public string FilePath { get; set; } = "";
    public VideoSource Source { get; set; } = VideoSource.Other;
    public int Width { get; set; }
    public int Height { get; set; }

    public bool IsVertical => Height > Width;
}

public sealed class TranscriptSegment
{
    public double Start { get; set; }
    public double End { get; set; }
    public string Text { get; set; } = "";
    /// <summary>Peak loudness of this line above the speaker's normal level, in dB (set when reactions are detected).</summary>
    public double? Loudness { get; set; }

    public double Duration => End - Start;
}

public sealed class Transcript
{
    public string Language { get; set; } = "";
    public List<TranscriptSegment> Segments { get; set; } = new();

    public string ToPlainText() => string.Join(" ", Segments.Select(s => s.Text.Trim()));

    /// <summary>Segments overlapping the given window, trimmed and re-based so the window starts at 0.</summary>
    public List<TranscriptSegment> Slice(double start, double end)
    {
        var result = new List<TranscriptSegment>();
        foreach (var s in Segments)
        {
            if (s.End <= start || s.Start >= end) continue;
            result.Add(new TranscriptSegment
            {
                Start = Math.Max(s.Start, start) - start,
                End = Math.Min(s.End, end) - start,
                Text = s.Text.Trim()
            });
        }
        return result;
    }

    public string ToSrt()
    {
        var sb = new System.Text.StringBuilder();
        int i = 1;
        foreach (var s in Segments)
        {
            sb.AppendLine(i++.ToString());
            sb.AppendLine($"{Fmt(s.Start)} --> {Fmt(s.End)}");
            sb.AppendLine(s.Text.Trim());
            sb.AppendLine();
        }
        return sb.ToString();

        static string Fmt(double t)
        {
            var ts = TimeSpan.FromSeconds(t);
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}";
        }
    }
}

public sealed class ShortSuggestion
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("start_seconds")] public double StartSeconds { get; set; }
    [JsonPropertyName("end_seconds")] public double EndSeconds { get; set; }
    [JsonPropertyName("hook")] public string Hook { get; set; } = "";
    [JsonPropertyName("why_viral")] public string WhyViral { get; set; } = "";
    [JsonPropertyName("virality_score")] public int ViralityScore { get; set; }
    [JsonPropertyName("emotion")] public string Emotion { get; set; } = "";
    [JsonPropertyName("suggested_caption")] public string SuggestedCaption { get; set; } = "";
    [JsonPropertyName("hashtags")] public List<string> Hashtags { get; set; } = new();

    /// <summary>Custom caption anchor as percent of the frame (0-100 from left / top). Null = style default.</summary>
    [JsonPropertyName("caption_x")] public double? CaptionX { get; set; }
    [JsonPropertyName("caption_y")] public double? CaptionY { get; set; }

    [JsonIgnore] public double Duration => EndSeconds - StartSeconds;
    [JsonIgnore] public bool Selected { get; set; } = true;
}

public sealed class SuggestionResponse
{
    [JsonPropertyName("video_summary")] public string VideoSummary { get; set; } = "";
    [JsonPropertyName("shorts")] public List<ShortSuggestion> Shorts { get; set; } = new();
}

public enum CropMode
{
    /// <summary>Center-crop to 9:16.</summary>
    VerticalCrop,
    /// <summary>Fit the whole frame on a blurred 9:16 background.</summary>
    VerticalBlurredBackground,
    /// <summary>Keep the original aspect ratio.</summary>
    Original
}

public sealed class GenerateOptions
{
    public bool AddCaptions { get; set; } = true;
    public string CaptionStyleId { get; set; } = "bold-pop";
    public int WordsPerCaption { get; set; } = 3;
    /// <summary>0 = use the style's default size.</summary>
    public int FontSize { get; set; } = 0;
    public CropMode CropMode { get; set; } = CropMode.VerticalCrop;
    public string OutputFolder { get; set; } = "";
    public bool BurnTitleHook { get; set; } = false;
    /// <summary>Show reaction tags like "[laughs]" in the burned captions.</summary>
    public bool IncludeReactions { get; set; } = false;
}

public sealed class ShortResult
{
    public ShortSuggestion Suggestion { get; set; } = new();
    public string OutputPath { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
}
