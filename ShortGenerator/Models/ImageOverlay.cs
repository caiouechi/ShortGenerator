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
