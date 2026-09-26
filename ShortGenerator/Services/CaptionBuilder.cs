using System.Drawing;
using System.Globalization;
using System.Text;
using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>Turns transcript segments into an ASS subtitle file rendered in the chosen caption style.</summary>
public static class CaptionBuilder
{
    public sealed record WordTiming(string Word, double Start, double End);

    /// <summary>
    /// Builds the ASS file content for a clip. Segment times must already be relative to the clip start.
    /// Words inside a segment get proportional timings (by character count), which is close enough for
    /// short phrase captions and never leaves a word unshown.
    /// </summary>
    public static string BuildAss(IReadOnlyList<TranscriptSegment> segments, CaptionStyle style, int wordsPerCaption,
        int fontSizeOverride, int playResX, int playResY, string? hookText = null, bool includeReactions = false,
        List<CaptionKeyframe>? captionPositions = null)
    {
        segments = PrepareText(segments, includeReactions);
        // Dragged caption positions override the style's alignment: each caption is anchored at the
        // position in effect at its start time (times are relative to the clip, like the segments).
        string PosTagAt(double time)
        {
            var k = captionPositions is { Count: > 0 } ? Keyframes.ActiveAt(captionPositions, time) : null;
            return k is null ? "" : $"{{\\an5\\pos({Math.Round(k.X / 100.0 * playResX)},{Math.Round(k.Y / 100.0 * playResY)})}}";
        }
        double scale = ComputeScale(playResX, playResY);
        int fontSize = (int)Math.Round((fontSizeOverride > 0 ? fontSizeOverride : style.FontSize) * scale);
        int outline = (int)Math.Round(style.Outline * scale);
        int shadow = (int)Math.Round(style.Shadow * scale);
        int marginV = (int)Math.Round(style.MarginV * scale);
        int marginH = (int)Math.Round(60 * scale);

        // For karaoke, ASS uses PrimaryColour for the already-sung part and SecondaryColour for the rest.
        var primary = style.Karaoke ? style.HighlightColor : style.PrimaryColor;
        var secondary = style.Karaoke ? style.PrimaryColor : style.PrimaryColor;

        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine($"PlayResX: {playResX}");
        sb.AppendLine($"PlayResY: {playResY}");
        sb.AppendLine("WrapStyle: 0");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine(string.Join(",", new[]
        {
            "Style: Caption", style.FontName, fontSize.ToString(),
            AssColor(primary), AssColor(secondary), AssColor(style.OutlineColor), AssColor(style.BackColor),
            style.Bold ? "-1" : "0", style.Italic ? "-1" : "0", "0", "0", "100", "100", "0", "0",
            style.BorderStyle.ToString(), outline.ToString(), shadow.ToString(), style.Alignment.ToString(),
            marginH.ToString(), marginH.ToString(), marginV.ToString(), "1"
        }));
        // Hook title at the top of the frame.
        sb.AppendLine(string.Join(",", new[]
        {
            "Style: Hook", style.FontName, ((int)(fontSize * 0.8)).ToString(),
            AssColor(Color.White), AssColor(Color.White), AssColor(Color.Black), AssColor(Color.FromArgb(200, 0, 0, 0)),
            "-1", "0", "0", "0", "100", "100", "0", "0", "3", ((int)Math.Round(6 * scale)).ToString(), "0", "8",
            marginH.ToString(), marginH.ToString(), ((int)Math.Round(200 * scale)).ToString(), "1"
        }));
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        if (!string.IsNullOrWhiteSpace(hookText))
        {
            var hookEnd = Math.Min(4.0, segments.Count > 0 ? segments[^1].End : 4.0);
            sb.AppendLine($"Dialogue: 1,{AssTime(0)},{AssTime(hookEnd)},Hook,,0,0,0,,{{\\fad(150,300)}}{Escape(hookText.Trim())}");
        }

        foreach (var chunk in Chunk(segments, Math.Max(1, wordsPerCaption)))
        {
            var start = chunk[0].Start;
            var end = chunk[^1].End;
            if (end - start < 0.05) end = start + 0.05;

            var text = new StringBuilder();
            text.Append(PosTagAt(start));
            if (style.Blur > 0) text.Append($"{{\\blur{style.Blur}}}");
            if (style.PopAnimation) text.Append("{\\fscx80\\fscy80\\t(0,110,\\fscx100\\fscy100)}");

            if (style.Karaoke)
            {
                foreach (var w in chunk)
                {
                    int cs = Math.Max(1, (int)Math.Round((w.End - w.Start) * 100));
                    text.Append($"{{\\k{cs}}}").Append(Escape(Case(w.Word, style))).Append(' ');
                }
            }
            else
            {
                text.Append(Escape(Case(string.Join(" ", chunk.Select(w => w.Word)), style)));
            }

            sb.AppendLine($"Dialogue: 0,{AssTime(start)},{AssTime(end)},Caption,,0,0,0,,{text.ToString().TrimEnd()}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Removes reaction tags such as "[laughs]" unless the user wants them shown, and drops segments left empty.
    /// Kept tags are shown in lower case even for uppercase styles ("[laughs]" reads better than "[LAUGHS]").
    /// </summary>
    public static List<TranscriptSegment> PrepareText(IReadOnlyList<TranscriptSegment> segments, bool includeReactions)
    {
        var list = new List<TranscriptSegment>(segments.Count);
        foreach (var s in segments)
        {
            var text = includeReactions ? s.Text : TranscriptEvents.Strip(s.Text);
            if (string.IsNullOrWhiteSpace(text)) continue;
            list.Add(new TranscriptSegment { Start = s.Start, End = s.End, Text = text });
        }
        return list;
    }

    /// <summary>Font scale relative to the 1080x1920 design canvas.</summary>
    public static double ComputeScale(int w, int h)
    {
        if (w <= 0 || h <= 0) return 1;
        return h >= w ? h / 1920.0 : (h / 1080.0) * 0.72; // landscape: keep captions from dominating the frame
    }

    public static IEnumerable<List<WordTiming>> Chunk(IReadOnlyList<TranscriptSegment> segments, int wordsPerCaption)
    {
        foreach (var seg in segments)
        {
            var words = seg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length == 0) continue;

            var timed = TimeWords(words, seg.Start, seg.End);
            for (int i = 0; i < timed.Count; i += wordsPerCaption)
                yield return timed.GetRange(i, Math.Min(wordsPerCaption, timed.Count - i));
        }
    }

    private static List<WordTiming> TimeWords(string[] words, double start, double end)
    {
        double total = words.Sum(w => w.Length + 1.0);
        double duration = Math.Max(0.1, end - start);
        var result = new List<WordTiming>(words.Length);
        double t = start;
        foreach (var w in words)
        {
            double d = duration * (w.Length + 1.0) / total;
            result.Add(new WordTiming(w, t, t + d));
            t += d;
        }
        return result;
    }

    private static string Case(string s, CaptionStyle style)
    {
        if (!style.Uppercase) return s;
        // Upper-case the words but keep "[laughs]"-style tags lower case.
        return System.Text.RegularExpressions.Regex.Replace(s.ToUpperInvariant(), @"\[[A-Z]+\]", m => m.Value.ToLowerInvariant());
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("{", "(").Replace("}", ")").Replace("\r", "").Replace("\n", "\\N");

    /// <summary>ASS colours are &amp;HAABBGGRR, where alpha 00 is opaque.</summary>
    public static string AssColor(Color c) => $"&H{255 - c.A:X2}{c.B:X2}{c.G:X2}{c.R:X2}";

    public static string AssTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
    }
}
