using System.Text.Json.Serialization;

namespace ShortGenerator.Models;

/// <summary>
/// An illustration shown over the short for a while: a picture of the person, object or place being talked about.
/// Times are relative to the short's start; position is the centre in percent of the output frame; size is the
/// width in percent of the output frame width. Rendered as a pre-drawn PNG composited only during its window.
/// </summary>
public sealed class ImageOverlay
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("t")] public double Start { get; set; }
    [JsonPropertyName("end")] public double End { get; set; } = 3;
    [JsonPropertyName("x")] public double X { get; set; } = 50;
    [JsonPropertyName("y")] public double Y { get; set; } = 28;
    [JsonPropertyName("size")] public double Size { get; set; } = 45;
    [JsonPropertyName("rotation")] public double Rotation { get; set; }
    /// <summary>plain | frame | card | circle</summary>
    [JsonPropertyName("style")] public string Style { get; set; } = "frame";
    /// <summary>none | pop | fade | slide</summary>
    [JsonPropertyName("animation")] public string Animation { get; set; } = "pop";

    // ---- video clips: a moment from another video shown over the short (the goal being talked about) ----

    /// <summary>image | video</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "image";
    /// <summary>Video: where the clip starts in its source file (seconds); it plays for End - Start.</summary>
    [JsonPropertyName("srcStart")] public double SourceStart { get; set; }
    /// <summary>Video: a split, i.e. another part of the main video itself, locked to the short's time
    /// (SourceStart = the short's start + Start, kept in step when Start changes).</summary>
    [JsonPropertyName("sync")] public bool Sync { get; set; }
    /// <summary>Video: true = covers the whole 9:16 frame (cropped from the source), false = an inset window placed like an image.</summary>
    [JsonPropertyName("full")] public bool FullFrame { get; set; } = true;
    /// <summary>Video: centre of the crop in percent of the source frame, and its zoom (1 = the largest crop that fits).</summary>
    [JsonPropertyName("cropX")] public double CropX { get; set; } = 50;
    [JsonPropertyName("cropY")] public double CropY { get; set; } = 50;
    [JsonPropertyName("zoom")] public double Zoom { get; set; } = 1;

    /// <summary>Video inset: the window's shape as width / height (9/16 vertical, 1 square, 16/9 wide); 0 = the source's own frame.</summary>
    [JsonPropertyName("aspect")] public double Aspect { get; set; }

    public static readonly (double Aspect, string Name)[] InsetShapes =
    {
        (9.0 / 16, "Vertical 9:16"), (1, "Square 1:1"), (16.0 / 9, "Wide 16:9"), (0, "Whole frame"),
    };

    [JsonIgnore] public bool IsVideo => Kind == "video";

    /// <summary>
    /// The crop rectangle of a video clip in source pixels: aspect <paramref name="aspect"/> (width / height), the
    /// largest that fits divided by the zoom, centred on CropX / CropY and kept inside the frame. Even sizes for H.264.
    /// </summary>
    public (int X, int Y, int W, int H) CropRect(int srcW, int srcH, double aspect)
    {
        double w, h;
        if ((double)srcW / srcH > aspect) { h = srcH; w = srcH * aspect; } else { w = srcW; h = srcW / aspect; }
        double z = Math.Clamp(Zoom, 1, 4);
        w /= z; h /= z;
        double cx = CropX / 100.0 * srcW, cy = CropY / 100.0 * srcH;
        double x = Math.Clamp(cx - w / 2, 0, srcW - w), y = Math.Clamp(cy - h / 2, 0, srcH - h);
        int E(double v) => Math.Max(2, (int)Math.Round(v / 2) * 2);
        return ((int)Math.Round(x), (int)Math.Round(y), Math.Min(E(w), srcW - srcW % 2), Math.Min(E(h), srcH - srcH % 2));
    }

    public static readonly (string Id, string Name)[] Styles =
    {
        ("frame", "Photo"), ("card", "Card"), ("circle", "Circle"), ("plain", "Plain"),
    };

    public static readonly (string Id, string Name)[] Animations =
    {
        ("pop", "Pop in"), ("fade", "Fade in"), ("slide", "Slide up"), ("none", "None"),
    };

    public static string StyleName(string id) => Styles.FirstOrDefault(s => s.Id == id).Name ?? id;
    public static string AnimationName(string id) => Animations.FirstOrDefault(s => s.Id == id).Name ?? id;
}
