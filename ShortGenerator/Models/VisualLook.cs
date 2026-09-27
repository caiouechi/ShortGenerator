namespace ShortGenerator.Models;

/// <summary>
/// A colour / exposure treatment applied to the whole short. The ffmpeg filter is burned into the render
/// and the cover; the CSS filter approximates the same look in the editor's live preview.
/// </summary>
public sealed class VisualLook
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>ffmpeg video filter chain; empty means leave the picture alone.</summary>
    public string Filter { get; init; } = "";
    /// <summary>CSS filter for the WebView preview; "none" when nothing changes.</summary>
    public string Css { get; init; } = "none";

    public static readonly VisualLook[] All =
    {
        new()
        {
            Id = "auto", Name = "Auto enhance (recommended)",
            Description = "A touch more contrast, colour and sharpness so phone screens read the picture better. Safe on any footage.",
            Filter = "eq=contrast=1.06:brightness=0.015:saturation=1.12,unsharp=5:5:0.35:5:5:0",
            Css = "contrast(1.06) brightness(1.03) saturate(1.12)"
        },
        new() { Id = "none", Name = "Do nothing", Description = "Keep the original picture exactly as it is." },
        new()
        {
            Id = "brighten", Name = "Brighten dark footage",
            Description = "Lifts shadows and mid-tones for dim rooms and underexposed webcams.",
            Filter = "eq=brightness=0.08:gamma=1.2:contrast=1.04:saturation=1.05",
            Css = "brightness(1.18) contrast(1.04) saturate(1.05)"
        },
        new()
        {
            Id = "tame", Name = "Tame bright footage",
            Description = "Pulls back blown-out highlights and washed-out skin on overexposed shots.",
            Filter = "eq=brightness=-0.05:gamma=0.9:contrast=1.08",
            Css = "brightness(0.9) contrast(1.08)"
        },
        new()
        {
            Id = "vivid", Name = "Vivid",
            Description = "Punchy colours and crisp detail. Good for studios with coloured lights.",
            Filter = "eq=saturation=1.4:contrast=1.1,unsharp=5:5:0.4:5:5:0",
            Css = "saturate(1.4) contrast(1.1)"
        },
        new()
        {
            Id = "cinematic", Name = "Cinematic",
            Description = "Deeper contrast, slightly muted colour, teal shadows and warm highlights.",
            Filter = "eq=contrast=1.15:saturation=0.85:brightness=-0.02,colorbalance=rs=0.04:bs=-0.03:rh=0.03:bh=0.05",
            Css = "contrast(1.15) saturate(0.85) sepia(0.12)"
        },
        new()
        {
            Id = "warm", Name = "Warm",
            Description = "A gentle golden tint for friendly, conversational content.",
            Filter = "eq=saturation=1.1,colorbalance=rs=0.06:gs=0.02:bs=-0.06",
            Css = "saturate(1.1) sepia(0.15)"
        },
    };

    public static VisualLook Get(string? id) => All.FirstOrDefault(l => l.Id == id) ?? All[0];
}
