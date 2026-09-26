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

    /// <summary>Legacy single caption anchor (percent of frame). Migrated into <see cref="CaptionPositions"/> on load.</summary>
    [JsonPropertyName("caption_x")] public double? CaptionX { get; set; }
    [JsonPropertyName("caption_y")] public double? CaptionY { get; set; }

    /// <summary>Caption anchors over time (times relative to the clip start). Each one holds until the next.</summary>
    [JsonPropertyName("caption_positions")] public List<CaptionKeyframe> CaptionPositions { get; set; } = new();

    /// <summary>Camera cuts for the vertical crop (times relative to the clip start). Empty = centered.</summary>
    [JsonPropertyName("camera")] public List<CameraKeyframe> Camera { get; set; } = new();

    /// <summary>Sticker / image overlays shown on the clip (times relative to the clip start).</summary>
    [JsonPropertyName("overlays")] public List<OverlayItem> Overlays { get; set; } = new();

    /// <summary>Moves the legacy single caption position into the keyframe list.</summary>
    public void MigrateLegacyCaptionPosition()
    {
        if (CaptionX is { } x && CaptionY is { } y && CaptionPositions.Count == 0)
            CaptionPositions.Add(new CaptionKeyframe { Time = 0, X = x, Y = y });
        CaptionX = CaptionY = null;
    }

    public CaptionKeyframe? CaptionAt(double relativeTime) => Keyframes.ActiveAt(CaptionPositions, relativeTime);
    public CameraKeyframe? CameraAt(double relativeTime) => Keyframes.ActiveAt(Camera, relativeTime);

    [JsonIgnore] public double Duration => EndSeconds - StartSeconds;
    /// <summary>Ticked for generation / editing. Persisted so the app reopens in the same state.</summary>
    [JsonPropertyName("selected")] public bool Selected { get; set; } = true;
}

public interface IKeyframe
{
    /// <summary>Seconds from the start of the clip.</summary>
    double Time { get; set; }
}

/// <summary>Where the caption block is anchored, as percent of the output frame (x from left, y from top).</summary>
public sealed class CaptionKeyframe : IKeyframe
{
    [JsonPropertyName("t")] public double Time { get; set; }
    [JsonPropertyName("x")] public double X { get; set; } = 50;
    [JsonPropertyName("y")] public double Y { get; set; } = 80;
}

/// <summary>A camera cut for the 9:16 crop: center of the crop as percent of the source frame, and zoom (1 = full height).</summary>
public sealed class CameraKeyframe : IKeyframe
{
    [JsonPropertyName("t")] public double Time { get; set; }
    [JsonPropertyName("x")] public double X { get; set; } = 50;
    [JsonPropertyName("y")] public double Y { get; set; } = 50;
    [JsonPropertyName("zoom")] public double Zoom { get; set; } = 1.0;
    /// <summary>"auto" when produced by face detection, "manual" when dragged by the user.</summary>
    [JsonPropertyName("source")] public string Source { get; set; } = "manual";
}

/// <summary>A transparent PNG (sticker) placed on the frame for a while, with a small animation.</summary>
public sealed class OverlayItem : IKeyframe
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("t")] public double Time { get; set; }
    [JsonPropertyName("dur")] public double Duration { get; set; } = 2.5;
    /// <summary>Sticker file name inside the sticker library, or an absolute path.</summary>
    [JsonPropertyName("file")] public string File { get; set; } = "";
    /// <summary>Center of the sticker as percent of the output frame.</summary>
    [JsonPropertyName("x")] public double X { get; set; } = 78;
    [JsonPropertyName("y")] public double Y { get; set; } = 22;
    /// <summary>Width of the sticker as percent of the output frame width.</summary>
    [JsonPropertyName("size")] public double Size { get; set; } = 26;
    /// <summary>"pop" (bounce in), "float" (gentle bob), "shake", or "none" (fade only).</summary>
    [JsonPropertyName("anim")] public string Animation { get; set; } = "pop";

    [JsonIgnore] public double End => Time + Duration;
}

public static class Keyframes
{
    /// <summary>The keyframe in effect at <paramref name="time"/>: the latest one at or before it, else the first one.</summary>
    public static T? ActiveAt<T>(List<T> list, double time) where T : class, IKeyframe
    {
        if (list.Count == 0) return null;
        T? best = null;
        foreach (var k in list.OrderBy(k => k.Time))
        {
            if (k.Time <= time + 0.001) best = k;
            else break;
        }
        return best ?? list.OrderBy(k => k.Time).First();
    }

    /// <summary>Adds or replaces the keyframe at <paramref name="time"/> (within 0.25 s).</summary>
    public static void Upsert<T>(List<T> list, T keyframe) where T : class, IKeyframe
    {
        list.RemoveAll(k => Math.Abs(k.Time - keyframe.Time) < 0.25);
        list.Add(keyframe);
        list.Sort((a, b) => a.Time.CompareTo(b.Time));
    }
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
    /// <summary>Run face detection to place the vertical crop when a short has no camera cuts yet.</summary>
    public bool AutoCamera { get; set; } = true;
}

public sealed class ShortResult
{
    public ShortSuggestion Suggestion { get; set; } = new();
    public string OutputPath { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
}
