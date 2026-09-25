using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>Loads an existing transcript from .srt, .vtt, or a JSON file (this app's sidecar or a plain Transcript).</summary>
public static class TranscriptImporter
{
    public const string FileFilter = "Transcripts|*.srt;*.vtt;*.json;*.shortgen.json|SubRip (*.srt)|*.srt|WebVTT (*.vtt)|*.vtt|JSON|*.json|All files|*.*";

    private static readonly Regex TimeLine = new(
        @"(?<s>\d{1,2}:\d{2}:\d{2}[,\.]\d{1,3}|\d{1,2}:\d{2}[,\.]\d{1,3})\s*-->\s*(?<e>\d{1,2}:\d{2}:\d{2}[,\.]\d{1,3}|\d{1,2}:\d{2}[,\.]\d{1,3})",
        RegexOptions.Compiled);

    public static Transcript Load(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var text = File.ReadAllText(path);
        var t = ext == ".json" ? LoadJson(text) : LoadCues(text);
        if (t.Segments.Count == 0) throw new FormatException("No timed lines were found in this file.");
        t.Segments = t.Segments.OrderBy(s => s.Start).ToList();
        return t;
    }

    private static Transcript LoadJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        // sidecar: { "Transcript": {...} }   or a bare Transcript: { "Segments": [...] }
        var tEl = root.TryGetProperty("Transcript", out var inner) ? inner : root;
        var t = new Transcript();
        if (tEl.TryGetProperty("Language", out var lang)) t.Language = lang.GetString() ?? "";
        if (tEl.TryGetProperty("Segments", out var segs) && segs.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in segs.EnumerateArray())
            {
                var seg = new TranscriptSegment
                {
                    Start = s.TryGetProperty("Start", out var a) ? a.GetDouble() : 0,
                    End = s.TryGetProperty("End", out var b) ? b.GetDouble() : 0,
                    Text = s.TryGetProperty("Text", out var c) ? c.GetString() ?? "" : ""
                };
                if (s.TryGetProperty("Loudness", out var l) && l.ValueKind == JsonValueKind.Number) seg.Loudness = l.GetDouble();
                if (seg.End > seg.Start && seg.Text.Trim().Length > 0) t.Segments.Add(seg);
            }
        }
        return t;
    }

    /// <summary>SRT and WebVTT share the "start --> end" cue layout.</summary>
    private static Transcript LoadCues(string content)
    {
        var t = new Transcript();
        var lines = content.Replace("\r\n", "\n").Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            var m = TimeLine.Match(lines[i]);
            if (!m.Success) { i++; continue; }
            double start = ParseTime(m.Groups["s"].Value), end = ParseTime(m.Groups["e"].Value);
            i++;
            var textLines = new List<string>();
            while (i < lines.Length && lines[i].Trim().Length > 0 && !TimeLine.IsMatch(lines[i]))
            {
                textLines.Add(Regex.Replace(lines[i], "<[^>]+>", "").Trim()); // drop VTT/HTML tags
                i++;
            }
            var text = string.Join(" ", textLines).Trim();
            if (end > start && text.Length > 0) t.Segments.Add(new TranscriptSegment { Start = start, End = end, Text = text });
        }
        return t;
    }

    private static double ParseTime(string s)
    {
        s = s.Replace(',', '.');
        var parts = s.Split(':');
        double total = 0;
        foreach (var p in parts) total = total * 60 + double.Parse(p, CultureInfo.InvariantCulture);
        return total;
    }
}
