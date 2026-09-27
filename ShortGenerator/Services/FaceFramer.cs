using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using Windows.Storage.Streams;
using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>
/// Decides where the 9:16 camera should look by detecting faces on sampled frames with the face detector
/// that ships with Windows (Windows.Media.FaceAnalysis). Produces a small list of camera cuts per clip.
/// </summary>
public sealed class FaceFramer
{
    private readonly FfmpegRunner _ffmpeg;

    public FaceFramer(FfmpegRunner ffmpeg) => _ffmpeg = ffmpeg;

    public static bool IsSupported
    {
        get { try { return FaceDetector.IsSupported; } catch { return false; } }
    }

    private sealed record Sample(double Time, double X, double Y, double Zoom, bool Found, List<Face> Faces);

    /// <summary>One detected face, in percent of the frame (centre and height).</summary>
    public sealed record Face(double X, double Y, double Height);

    /// <summary>Everything detection learned about a short, kept so the editor can offer other framings without re-running.</summary>
    public sealed class Analysis
    {
        public double Duration { get; init; }
        public List<(double Time, List<Face> Faces)> Frames { get; init; } = new();
        public List<CameraKeyframe> Cuts { get; init; } = new();
    }

    /// <param name="samplesPerSecond">Detection rate. 2 is plenty for talking heads.</param>
    public async Task<List<CameraKeyframe>> DetectAsync(VideoInfo video, ShortSuggestion s, IProgress<double>? progress, IProgress<string> log, CancellationToken ct, double samplesPerSecond = 2)
        => (await AnalyzeAsync(video, s, progress, log, ct, samplesPerSecond)).Cuts;

    public async Task<Analysis> AnalyzeAsync(VideoInfo video, ShortSuggestion s, IProgress<double>? progress, IProgress<string> log, CancellationToken ct, double samplesPerSecond = 2)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Face detection is not available on this Windows build.");

        var workDir = Path.Combine(Path.GetTempPath(), $"shortgen_faces_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            // 1) sample frames (small, detection does not need resolution)
            await _ffmpeg.RunAsync(new[]
            {
                "-y", "-ss", F(s.StartSeconds), "-to", F(s.EndSeconds), "-i", video.FilePath,
                "-vf", $"fps={F(samplesPerSecond)},scale=640:-2", "-q:v", "5", Path.Combine(workDir, "f%05d.jpg")
            }, null, new Progress<double>(p => progress?.Report(p * 0.4)), s.Duration, ct);

            var files = Directory.GetFiles(workDir, "f*.jpg").OrderBy(f => f).ToArray();
            if (files.Length == 0) throw new InvalidOperationException("No frames could be extracted for face detection.");

            // 2) detect faces per frame
            var detector = await FaceDetector.CreateAsync();
            var format = FaceDetector.GetSupportedBitmapPixelFormats().Contains(BitmapPixelFormat.Gray8) ? BitmapPixelFormat.Gray8 : BitmapPixelFormat.Nv12;
            var samples = new List<Sample>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                double t = i / samplesPerSecond;
                var sample = await DetectOneAsync(detector, format, files[i], t);
                samples.Add(sample);
                progress?.Report(0.4 + 0.6 * (i + 1) / files.Length);
            }

            int found = samples.Count(x => x.Found);
            var cuts = ToCuts(samples, s.Duration);
            log.Report($"Camera: faces found in {found}/{samples.Count} frames -> {cuts.Count} camera cut(s) for \"{s.Title}\".");
            return new Analysis { Duration = s.Duration, Cuts = cuts, Frames = samples.Select(x => (x.Time, x.Faces)).ToList() };
        }
        finally
        {
            try { Directory.Delete(workDir, true); } catch { }
        }
    }

    private static async Task<Sample> DetectOneAsync(FaceDetector detector, BitmapPixelFormat format, string jpegPath, double t)
    {
        var bytes = await File.ReadAllBytesAsync(jpegPath);
        using var ms = new InMemoryRandomAccessStream();
        await ms.WriteAsync(bytes.AsBuffer());
        ms.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ms);
        using var bmp = await decoder.GetSoftwareBitmapAsync();
        using var converted = SoftwareBitmap.Convert(bmp, format);
        var faces = await detector.DetectFacesAsync(converted);
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        var all = faces.Select(f => new Face((f.FaceBox.X + f.FaceBox.Width / 2.0) / w * 100, (f.FaceBox.Y + f.FaceBox.Height / 2.0) / h * 100, f.FaceBox.Height / (double)h * 100)).ToList();
        if (faces.Count == 0) return new Sample(t, 50, 50, 1, false, all);

        // Largest face is the active speaker most of the time. If two faces of similar size fit inside one
        // 9:16 window, frame both; otherwise follow the largest.
        var ordered = faces.Select(f => f.FaceBox).OrderByDescending(b => b.Width * b.Height).ToList();
        var main = ordered[0];
        double cx = main.X + main.Width / 2.0, cy = main.Y + main.Height / 2.0, faceH = main.Height;
        if (ordered.Count > 1)
        {
            var second = ordered[1];
            bool similar = second.Width * second.Height >= 0.5 * main.Width * main.Height;
            double spanX = Math.Abs((second.X + second.Width / 2.0) - cx);
            double windowW = h * CameraMath.OutAspect; // crop width at zoom 1
            if (similar && spanX + main.Width < windowW * 0.9)
            {
                cx = (cx + second.X + second.Width / 2.0) / 2;
                cy = (cy + second.Y + second.Height / 2.0) / 2;
                faceH = Math.Max(main.Height, second.Height);
            }
        }

        // Zoom so a face fills roughly a fifth of the frame height; never crop harder than 1.6x.
        double faceFrac = faceH / h;
        double zoom = Math.Clamp(0.20 / Math.Max(0.05, faceFrac), 1.0, 1.6);
        // Put the face slightly above the middle of the frame (eyes around 40% from the top).
        double cropH = h / zoom;
        double targetCy = cy + cropH * 0.10;

        return new Sample(t, cx / w * 100, targetCy / h * 100, zoom, true, all);
    }

    /// <summary>Camera keyframe values that frame a face of the given size at the given centre (percent).</summary>
    public static (double X, double Y, double Zoom) Frame(double cx, double cy, double faceHeightPct)
    {
        double zoom = Math.Clamp(0.20 / Math.Max(0.05, faceHeightPct / 100.0), 1.0, 1.6);
        double cropH = 100 / zoom;
        return (Math.Round(cx, 1), Math.Round(cy + cropH * 0.10, 1), Math.Round(zoom, 2));
    }

    /// <summary>
    /// Other places the camera could look during [from, to): every person seen there (largest and most
    /// present first), a two-shot when two people fit side by side, and finally the wide centred frame.
    /// </summary>
    public static List<(CameraKeyframe Key, string Label)> Alternatives(Analysis a, double from, double to)
    {
        var faces = a.Frames.Where(f => f.Time >= from && f.Time < to).SelectMany(f => f.Faces).ToList();
        // cluster by horizontal position: faces within 15% of a cluster centre are the same person
        var clusters = new List<List<Face>>();
        foreach (var f in faces.OrderByDescending(f => f.Height))
        {
            var c = clusters.FirstOrDefault(cl => Math.Abs(cl.Average(x => x.X) - f.X) < 15);
            if (c is null) clusters.Add(new List<Face> { f }); else c.Add(f);
        }
        var ranked = clusters.Where(c => c.Count >= 2).OrderByDescending(c => c.Count * c.Average(x => x.Height)).ToList();
        var result = new List<(CameraKeyframe, string)>();
        int n = 1;
        foreach (var c in ranked)
        {
            var (x, y, z) = Frame(Median(c.Select(f => f.X)), Median(c.Select(f => f.Y)), Median(c.Select(f => f.Height)));
            result.Add((new CameraKeyframe { Time = from, X = x, Y = y, Zoom = z, Source = "alt" }, $"person {n++}"));
        }
        if (ranked.Count >= 2)
        {
            var a1 = ranked[0]; var a2 = ranked[1];
            double x1 = Median(a1.Select(f => f.X)), x2 = Median(a2.Select(f => f.X));
            if (Math.Abs(x1 - x2) < 45)
            {
                var (x, y, _) = Frame((x1 + x2) / 2, (Median(a1.Select(f => f.Y)) + Median(a2.Select(f => f.Y))) / 2, Math.Max(Median(a1.Select(f => f.Height)), Median(a2.Select(f => f.Height))));
                result.Add((new CameraKeyframe { Time = from, X = x, Y = Math.Round(y, 1), Zoom = 1.0, Source = "alt" }, "both"));
            }
        }
        result.Add((new CameraKeyframe { Time = from, X = 50, Y = 50, Zoom = 1.0, Source = "alt" }, "wide shot"));
        return result;
    }

    /// <summary>
    /// Turns per-frame detections into a few stable cuts: a new cut starts only when the subject moves clearly
    /// (more than 12% of the width or a zoom change over 0.25) and stays there for at least 1.5 s; each cut uses
    /// the median of its samples. Frames without a face inherit the previous cut.
    /// </summary>
    private static List<CameraKeyframe> ToCuts(List<Sample> samples, double duration)
    {
        var detected = samples.Where(s => s.Found).ToList();
        if (detected.Count == 0) return new List<CameraKeyframe> { CameraMath.Centered() };

        // fill gaps: carry the last detection forward (and the first one backward)
        var filled = new List<Sample>(samples.Count);
        Sample last = detected[0];
        foreach (var s in samples) { if (s.Found) last = s; filled.Add(new Sample(s.Time, last.X, last.Y, last.Zoom, true, s.Faces)); }

        // A cut must hold for 2 s to count, and no cut may be shorter than 3 s: short flickers read as mistakes.
        const double moveThreshold = 12, zoomThreshold = 0.25, minHold = 2.0, minCut = 3.0;
        var groups = new List<List<Sample>> { new() { filled[0] } };
        int i = 1;
        while (i < filled.Count)
        {
            var current = groups[^1];
            double refX = Median(current.Select(x => x.X)), refZ = Median(current.Select(x => x.Zoom));
            var cand = filled[i];
            bool moved = Math.Abs(cand.X - refX) > moveThreshold || Math.Abs(cand.Zoom - refZ) > zoomThreshold;
            if (moved)
            {
                // does the move persist for minHold seconds?
                int j = i; bool persists = true;
                while (j < filled.Count && filled[j].Time - cand.Time < minHold)
                {
                    if (Math.Abs(filled[j].X - cand.X) > moveThreshold) { persists = false; break; }
                    j++;
                }
                if (persists && cand.Time - current[0].Time >= minCut) { groups.Add(new List<Sample> { cand }); i++; continue; }
            }
            current.Add(cand);
            i++;
        }

        var cuts = groups.Select(g => new CameraKeyframe
        {
            Time = Math.Round(g[0].Time, 2),
            X = Math.Round(Median(g.Select(x => x.X)), 1),
            Y = Math.Round(Median(g.Select(x => x.Y)), 1),
            Zoom = Math.Round(Median(g.Select(x => x.Zoom)), 2),
            Source = "auto"
        }).ToList();
        cuts[0].Time = 0;
        return cuts.Where(c => c.Time < duration).ToList();
    }

    private static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(v => v).ToArray();
        return a.Length == 0 ? 0 : a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
    }

    private static string F(double d) => d.ToString("F3", CultureInfo.InvariantCulture);
}
