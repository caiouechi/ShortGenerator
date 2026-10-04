using System.Text.Json.Serialization;

namespace ShortGenerator.Models;

public enum VideoSource { YouTube, Instagram, TikTok, Other, LocalFile }

public sealed class VideoInfo
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Uploader { get; set; } = "";
    /// <summary>The channel's display name (YouTube "channel"), when it differs from the uploader.</summary>
    public string? Channel { get; set; }
    /// <summary>The channel's handle, like "@DiarioAS", when the site has one.</summary>
    public string? Handle { get; set; }
    /// <summary>The channel's page, for a credit link.</summary>
    public string? ChannelUrl { get; set; }
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
    /// <summary>English version of this line for English captions (same timing as the original).</summary>
    public string? English { get; set; }
    /// <summary>The original text the English version was made from; when the original is edited later the English is out of date.</summary>
    public string? EnglishFrom { get; set; }
    /// <summary>No English yet, or the original changed after it was translated.</summary>
    [JsonIgnore] public bool EnglishIsStale => string.IsNullOrWhiteSpace(English) || EnglishFrom != Text;
    /// <summary>An English version exists but the original changed after it was translated.</summary>
    [JsonIgnore] public bool EnglishOutdated => !string.IsNullOrWhiteSpace(English) && EnglishFrom != Text;

    public double Duration => End - Start;
}

public sealed class Transcript
{
    public string Language { get; set; } = "";
    public List<TranscriptSegment> Segments { get; set; } = new();

    public string ToPlainText() => string.Join(" ", Segments.Select(s => s.Text.Trim()));

    /// <summary>Segments overlapping the given window, trimmed and re-based so the window starts at 0.</summary>
    /// <param name="english">Use each line's English version when it has one (English captions).</param>
    public List<TranscriptSegment> Slice(double start, double end, bool english = false)
    {
        var result = new List<TranscriptSegment>();
        foreach (var s in Segments)
        {
            if (s.End <= start || s.Start >= end) continue;
            result.Add(new TranscriptSegment
            {
                Start = Math.Max(s.Start, start) - start,
                End = Math.Min(s.End, end) - start,
                Text = (english && !string.IsNullOrWhiteSpace(s.English) ? s.English : s.Text).Trim()
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

    /// <summary>Platform-specific post text (only the networks that were requested are filled).</summary>
    [JsonPropertyName("youtube")] public NetworkPost? Youtube { get; set; }
    [JsonPropertyName("tiktok")] public NetworkPost? Tiktok { get; set; }
    [JsonPropertyName("instagram")] public NetworkPost? Instagram { get; set; }

    /// <summary>Cover / thumbnail frame, seconds from the clip start. Null = first second of the clip.</summary>
    [JsonPropertyName("cover_time")] public double? CoverTime { get; set; }
    /// <summary>Custom cover image chosen by the user (absolute path). Takes precedence over <see cref="CoverTime"/>.</summary>
    [JsonPropertyName("cover_image")] public string? CoverImage { get; set; }
    /// <summary>English version's cover: its own frame time or image. Both null = same cover as the original.</summary>
    [JsonPropertyName("cover_time_en")] public double? CoverTimeEn { get; set; }
    [JsonPropertyName("cover_image_en")] public string? CoverImageEn { get; set; }

    /// <summary>True when the English version has a cover of its own.</summary>
    [JsonIgnore] public bool HasEnglishCover => CoverTimeEn is not null || !string.IsNullOrWhiteSpace(CoverImageEn);

    /// <summary>The cover to use for a language: the English one when set, otherwise the original.</summary>
    public (double? Time, string? Image) CoverFor(string? language) =>
        language == "en" && HasEnglishCover ? (CoverTimeEn, CoverImageEn) : (CoverTime, CoverImage);

    /// <summary>Sets (or with both null, resets) the cover of one language.</summary>
    public void SetCover(string? language, double? time, string? image)
    {
        if (language == "en") { CoverTimeEn = time; CoverImageEn = image; }
        else { CoverTime = time; CoverImage = image; }
    }

    /// <summary>The post text for a network, falling back to the generic caption / hashtags.</summary>
    public NetworkPost PostFor(string network)
    {
        var p = network.ToLowerInvariant() switch { "youtube" => Youtube, "tiktok" => Tiktok, "instagram" => Instagram, _ => null };
        return new NetworkPost
        {
            Title = string.IsNullOrWhiteSpace(p?.Title) ? Title : p!.Title,
            Description = string.IsNullOrWhiteSpace(p?.Description) ? SuggestedCaption : p!.Description,
            Tags = p?.Tags is { Count: > 0 } ? p.Tags : Hashtags
        };
    }

    /// <summary>Legacy single caption anchor (percent of frame). Migrated into <see cref="CaptionPositions"/> on load.</summary>
    [JsonPropertyName("caption_x")] public double? CaptionX { get; set; }
    [JsonPropertyName("caption_y")] public double? CaptionY { get; set; }

    /// <summary>Caption anchors over time (times relative to the clip start). Each one holds until the next.</summary>
    [JsonPropertyName("caption_positions")] public List<CaptionKeyframe> CaptionPositions { get; set; } = new();

    /// <summary>Camera cuts for the vertical crop (times relative to the clip start). Empty = centered.</summary>
    [JsonPropertyName("camera")] public List<CameraKeyframe> Camera { get; set; } = new();
    /// <summary>How far the picture is moved down inside the frame, in percent of the frame height (negative = up).
    /// Dragged on the preview, so the speakers sit below a split or a clip banner at the top; the strip it uncovers is black.</summary>
    [JsonPropertyName("main_dy")] public double MainOffsetY { get; set; }
    /// <summary>Illustrations shown over the short (people, objects, places being talked about).</summary>
    [JsonPropertyName("overlays")] public List<ImageOverlay> Overlays { get; set; } = new();
    /// <summary>Make an English version of this short (captions + post text). Ticked by default.</summary>
    [JsonPropertyName("translate_en")] public bool TranslateEnglish { get; set; } = true;
    /// <summary>English post texts, filled when the short is translated.</summary>
    [JsonPropertyName("english")] public EnglishPost? English { get; set; }

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

/// <summary>Title, description / caption and keyword tags written for one social network.</summary>
/// <summary>English version of a short's post texts (the English captions live on the transcript lines).</summary>
public sealed class EnglishPost
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("caption")] public string Caption { get; set; } = "";
    [JsonPropertyName("hashtags")] public List<string> Hashtags { get; set; } = new();
    [JsonPropertyName("youtube")] public NetworkPost? Youtube { get; set; }
    [JsonPropertyName("tiktok")] public NetworkPost? Tiktok { get; set; }
    [JsonPropertyName("instagram")] public NetworkPost? Instagram { get; set; }
}

public sealed class NetworkPost
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    /// <summary>YouTube description, or the TikTok / Instagram caption.</summary>
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    /// <summary>Keywords for YouTube tags, hashtags (without #) for TikTok and Instagram.</summary>
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
}

/// <summary>Which networks the AI should write post text for.</summary>
public readonly record struct PostTargets(bool YouTube, bool TikTok, bool Instagram)
{
    public bool Any => YouTube || TikTok || Instagram;
    public static PostTargets All => new(true, true, true);
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
    public int WordsPerCaption { get; set; } = 6;
    /// <summary>0 = use the style's default size.</summary>
    public int FontSize { get; set; } = 0;
    public CropMode CropMode { get; set; } = CropMode.VerticalCrop;
    public string OutputFolder { get; set; } = "";
    public bool BurnTitleHook { get; set; } = false;
    /// <summary>Show reaction tags like "[laughs]" in the burned captions.</summary>
    public bool IncludeReactions { get; set; } = false;
    /// <summary>Run face detection to place the vertical crop when a short has no camera cuts yet.</summary>
    public bool AutoCamera { get; set; } = true;
    /// <summary>Colour / exposure treatment (see VisualLook). "auto" by default, "none" leaves the picture alone.</summary>
    public string Look { get; set; } = "auto";
    /// <summary>"original" or "en": which text the burned captions use.</summary>
    public string CaptionLanguage { get; set; } = "original";
    /// <summary>Appended to the file name, e.g. " (EN)" for the English version.</summary>
    public string FileSuffix { get; set; } = "";
}

public sealed class ShortResult
{
    public ShortSuggestion Suggestion { get; set; } = new();
    public string OutputPath { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
}
