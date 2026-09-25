using System.Drawing;

namespace ShortGenerator.Models;

/// <summary>
/// Describes a burned-in caption look. Rendered via an ASS (Advanced SubStation Alpha)
/// subtitle file that ffmpeg burns into the clip.
/// </summary>
public sealed class CaptionStyle
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string FontName { get; init; } = "Arial";
    /// <summary>Font size for a 1080x1920 canvas.</summary>
    public int FontSize { get; init; } = 72;
    public bool Bold { get; init; } = true;
    public bool Italic { get; init; }
    public bool Uppercase { get; init; }
    public Color PrimaryColor { get; init; } = Color.White;
    /// <summary>Karaoke "spoken word" colour.</summary>
    public Color HighlightColor { get; init; } = Color.Yellow;
    public Color OutlineColor { get; init; } = Color.Black;
    /// <summary>Box colour (BorderStyle 3) or shadow colour (BorderStyle 1).</summary>
    public Color BackColor { get; init; } = Color.FromArgb(160, 0, 0, 0);
    public int Outline { get; init; } = 4;
    public int Shadow { get; init; } = 0;
    public int Blur { get; init; }
    /// <summary>1 = outline+shadow, 3 = opaque box behind text.</summary>
    public int BorderStyle { get; init; } = 1;
    /// <summary>ASS alignment (numpad layout): 2 = bottom-center, 5 = middle-center, 8 = top-center.</summary>
    public int Alignment { get; init; } = 2;
    public int MarginV { get; init; } = 260;
    public bool Karaoke { get; init; }
    public bool PopAnimation { get; init; }

    public static readonly IReadOnlyList<CaptionStyle> All = new List<CaptionStyle>
    {
        new()
        {
            Id = "classic", Name = "Classic",
            Description = "Clean white text with a black outline at the bottom. Safe, readable, works everywhere.",
            FontName = "Arial", FontSize = 64, Bold = true, Outline = 3, Shadow = 1, Alignment = 2, MarginV = 240
        },
        new()
        {
            Id = "bold-pop", Name = "Bold Pop",
            Description = "Huge uppercase words in the center with a thick outline and a pop-in animation. The MrBeast / Hormozi look.",
            FontName = "Impact", FontSize = 96, Bold = true, Uppercase = true, Outline = 6, Shadow = 2,
            Alignment = 5, MarginV = 0, PrimaryColor = Color.White, OutlineColor = Color.Black, PopAnimation = true
        },
        new()
        {
            Id = "karaoke", Name = "Karaoke Highlight",
            Description = "Whole phrase visible, the word being spoken lights up in yellow. Keeps viewers reading along.",
            FontName = "Arial Black", FontSize = 78, Bold = true, Uppercase = true, Outline = 5, Shadow = 0,
            Alignment = 5, MarginV = 0, PrimaryColor = Color.White, HighlightColor = Color.FromArgb(255, 230, 0), Karaoke = true
        },
        new()
        {
            Id = "minimal-box", Name = "Minimal Box",
            Description = "White text on a semi-transparent dark box near the bottom. Podcast / interview style.",
            FontName = "Segoe UI", FontSize = 60, Bold = true, Outline = 0, Shadow = 0, BorderStyle = 3,
            BackColor = Color.FromArgb(170, 0, 0, 0), Alignment = 2, MarginV = 300
        },
        new()
        {
            Id = "neon", Name = "Neon Glow",
            Description = "Cyan text with a soft glow. Gaming, tech and music content.",
            FontName = "Verdana", FontSize = 70, Bold = true, Outline = 3, Shadow = 0, Blur = 6,
            PrimaryColor = Color.FromArgb(0, 255, 255), OutlineColor = Color.FromArgb(0, 120, 200), Alignment = 5
        },
        new()
        {
            Id = "yellow-punch", Name = "Yellow Punch",
            Description = "Bold yellow uppercase text with a black outline. Very high contrast, news / reaction style.",
            FontName = "Arial Black", FontSize = 84, Bold = true, Uppercase = true, Outline = 6, Shadow = 3,
            PrimaryColor = Color.FromArgb(255, 220, 0), OutlineColor = Color.Black, Alignment = 2, MarginV = 280, PopAnimation = true
        }
    };

    public static CaptionStyle Get(string id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];
}
