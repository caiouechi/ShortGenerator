using System.Globalization;
using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>Crop geometry for the 9:16 camera and the ffmpeg filter graph that applies camera cuts.</summary>
public static class CameraMath
{
    public const double OutAspect = 9.0 / 16.0;

    /// <summary>Crop rectangle in source pixels for a camera keyframe (center in percent, zoom 1 = full height).</summary>
    public static (int X, int Y, int W, int H) CropRect(int srcW, int srcH, CameraKeyframe k)
    {
        double zoom = Math.Clamp(k.Zoom, 1.0, 4.0);
        double ch = srcH / zoom;
        double cw = ch * OutAspect;
        if (cw > srcW) { cw = srcW; ch = cw / OutAspect; }
        // even sizes keep yuv420p happy
        int w = Math.Max(2, (int)Math.Round(cw / 2) * 2);
        int h = Math.Max(2, (int)Math.Round(ch / 2) * 2);
        int x = (int)Math.Round(k.X / 100.0 * srcW - w / 2.0);
        int y = (int)Math.Round(k.Y / 100.0 * srcH - h / 2.0);
        x = Math.Clamp(x, 0, Math.Max(0, srcW - w));
        y = Math.Clamp(y, 0, Math.Max(0, srcH - h));
        return (x, y, w, h);
    }

    /// <summary>Centered crop (the default when a short has no camera cuts).</summary>
    public static CameraKeyframe Centered() => new() { Time = 0, X = 50, Y = 50, Zoom = 1, Source = "auto" };

    /// <summary>
    /// Builds the video part of the filter graph for a clip with camera cuts. Input label [0:v], output unlabeled
    /// (the caller appends ",subtitles=..." and "[vout]"). Uses trim + concat so each cut can have its own zoom.
    /// </summary>
    public static string BuildCutsFilter(int srcW, int srcH, double clipDuration, List<CameraKeyframe> camera)
    {
        var cuts = camera.Where(k => k.Time < clipDuration - 0.05).OrderBy(k => k.Time).ToList();
        if (cuts.Count == 0) cuts.Add(Centered());
        if (cuts[0].Time > 0.001) cuts.Insert(0, new CameraKeyframe { Time = 0, X = cuts[0].X, Y = cuts[0].Y, Zoom = cuts[0].Zoom, Source = cuts[0].Source });

        if (cuts.Count == 1)
        {
            var r = CropRect(srcW, srcH, cuts[0]);
            return $"[0:v]crop={r.W}:{r.H}:{r.X}:{r.Y},scale=1080:1920:flags=lanczos,setsar=1";
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("[0:v]split=").Append(cuts.Count);
        for (int i = 0; i < cuts.Count; i++) sb.Append($"[s{i}]");
        sb.Append(';');
        for (int i = 0; i < cuts.Count; i++)
        {
            double start = cuts[i].Time;
            double end = i + 1 < cuts.Count ? cuts[i + 1].Time : clipDuration;
            var r = CropRect(srcW, srcH, cuts[i]);
            sb.Append($"[s{i}]trim=start={F(start)}:end={F(end)},setpts=PTS-STARTPTS,crop={r.W}:{r.H}:{r.X}:{r.Y},scale=1080:1920:flags=lanczos,setsar=1[v{i}];");
        }
        for (int i = 0; i < cuts.Count; i++) sb.Append($"[v{i}]");
        sb.Append($"concat=n={cuts.Count}:v=1:a=0");
        return sb.ToString();
    }

    private static string F(double d) => d.ToString("F3", CultureInfo.InvariantCulture);
}
