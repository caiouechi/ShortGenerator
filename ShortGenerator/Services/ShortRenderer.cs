using System.Globalization;
using System.Text.RegularExpressions;
using ShortGenerator.Models;

namespace ShortGenerator.Services;

/// <summary>Cuts a suggestion out of the source video, reframes it to 9:16 and burns in captions with ffmpeg.</summary>
public sealed class ShortRenderer
{
    private readonly FfmpegRunner _ffmpeg;

    public ShortRenderer(FfmpegRunner ffmpeg) => _ffmpeg = ffmpeg;

    public async Task<ShortResult> RenderAsync(VideoInfo video, Transcript? transcript, ShortSuggestion s, GenerateOptions options, int index,
        IProgress<double> progress, IProgress<string> log, CancellationToken ct)
    {
        var result = new ShortResult { Suggestion = s };
        Directory.CreateDirectory(options.OutputFolder);

        var baseName = $"{index:00} - {SafeFileName(s.Title)}{options.FileSuffix}";
        var outPath = Path.Combine(options.OutputFolder, baseName + ".mp4");
        int n = 1;
        while (File.Exists(outPath)) outPath = Path.Combine(options.OutputFolder, $"{baseName} ({n++}).mp4");

        var workDir = Path.Combine(Path.GetTempPath(), $"shortgen_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var (outW, outH) = OutputSize(video, options.CropMode);
            var filters = new List<string>();
            string? cameraGraph = null;

            switch (options.CropMode)
            {
                case CropMode.VerticalCrop:
                    if (s.Camera.Count > 0 && video.Width > 0 && video.Height > 0)
                    {
                        // Camera cuts: the crop follows the detected / chosen subject, with per-cut zoom.
                        // BuildCutsFilter already consumes [0:v]; we chain the rest after it.
                        cameraGraph = CameraMath.BuildCutsFilter(video.Width, video.Height, s.Duration, s.Camera);
                    }
                    else
                    {
                        // Center crop to 9:16 then scale to 1080x1920. Works for landscape and already-vertical sources.
                        filters.Add("crop='min(iw,ih*9/16)':'min(ih,iw*16/9)'");
                        filters.Add("scale=1080:1920:flags=lanczos");
                    }
                    break;
                case CropMode.VerticalBlurredBackground:
                    filters.Add("split=2[bg][fg];[bg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=25:8[bgb];" +
                                "[fg]scale=1080:1920:force_original_aspect_ratio=decrease[fgs];[bgb][fgs]overlay=(W-w)/2:(H-h)/2");
                    break;
                case CropMode.Original:
                    // Ensure even dimensions for H.264.
                    filters.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
                    break;
            }

            var look = VisualLook.Get(options.Look);
            if (look.Filter.Length > 0) filters.Add(look.Filter);

            bool hasCaptions = options.AddCaptions && transcript is not null;
            if (hasCaptions)
            {
                var style = CaptionStyle.Get(options.CaptionStyleId);
                var slice = transcript!.Slice(s.StartSeconds, s.EndSeconds, english: options.CaptionLanguage == "en");
                s.MigrateLegacyCaptionPosition();
                var ass = CaptionBuilder.BuildAss(slice, style, options.WordsPerCaption, options.FontSize, outW, outH,
                    options.BurnTitleHook ? s.Hook : null, options.IncludeReactions, s.CaptionPositions);
                var assPath = Path.Combine(workDir, "captions.ass");
                await File.WriteAllTextAsync(assPath, ass, new System.Text.UTF8Encoding(false), ct);
            }

            // Main video chain. Filters chain with ','. The blurred-background entry contains its own labelled
            // sub-graph and ends with an unlabelled output, so it chains like any other filter. With camera cuts,
            // the graph already starts at [0:v] (split/trim/concat) and the remaining filters follow it.
            var main = cameraGraph is not null
                ? cameraGraph + (filters.Count > 0 ? "," + string.Join(",", filters) : "")
                : "[0:v]" + (filters.Count > 0 ? string.Join(",", filters) : "null");

            var args = new List<string>
            {
                "-y",
                "-ss", s.StartSeconds.ToString("F3", CultureInfo.InvariantCulture),
                "-to", s.EndSeconds.ToString("F3", CultureInfo.InvariantCulture),
                "-i", video.FilePath,
            };

            // Image layers: each one is pre-drawn once (style + rotation baked in) and added as a short looping
            // still input, composited only inside its window. Captions are burned last so they stay on top;
            // the relative subtitle path avoids Windows drive-letter escaping problems inside the filter graph.
            string graph;
            var layers = s.Overlays.Where(o => File.Exists(o.Path) && o.End > o.Start + 0.1 && o.Start < s.Duration).OrderBy(o => o.Start).ToList();
            if (layers.Count == 0)
                graph = main + (hasCaptions ? ",subtitles=captions.ass" : "");
            else
            {
                var sb = new System.Text.StringBuilder(main + "[base]");
                string prev = "base";
                for (int i = 0; i < layers.Count; i++)
                {
                    var o = layers[i];
                    double a = Math.Max(0, o.Start), b = Math.Min(s.Duration, o.End);
                    var png = Path.Combine(workDir, $"layer{i}.png");
                    OverlayBaker.Bake(o, outW, png);
                    args.AddRange(new[] { "-loop", "1", "-framerate", "30", "-t", F(b + 0.3), "-i", png });
                    sb.Append($";[{i + 1}:v]format=rgba{LayerAnimation(o, a, b)}[o{i}]");
                    double cx = o.X / 100.0 * outW, cy = o.Y / 100.0 * outH;
                    var y = $"{F(cy)}-h/2" + (o.Animation == "slide" ? $"+{F(outH * 0.08)}*max(0,1-(t-{F(a)})/0.35)" : "");
                    sb.Append($";[{prev}][o{i}]overlay=x='{F(cx)}-w/2':y='{y}':enable='between(t,{F(a)},{F(b)})':eof_action=pass[b{i}]");
                    prev = $"b{i}";
                }
                sb.Append($";[{prev}]" + (hasCaptions ? "subtitles=captions.ass" : "null"));
                graph = sb.ToString();
            }
            args.AddRange(new[]
            {
                "-filter_complex", graph + "[vout]",
                "-map", "[vout]", "-map", "0:a?",
                "-c:v", "libx264", "-preset", "fast", "-crf", "20", "-pix_fmt", "yuv420p", "-r", "30",
                "-c:a", "aac", "-b:a", "160k",
                "-movflags", "+faststart",
                outPath
            });

            log.Report($"Rendering \"{s.Title}\" ({s.StartSeconds:F1}s - {s.EndSeconds:F1}s)...");
            await _ffmpeg.RunAsync(args, workDir, progress, s.Duration, ct);

            result.OutputPath = outPath;
            result.Success = true;
            log.Report($"Done: {outPath}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Success = false;
            result.Error = ex.Message;
            log.Report($"Failed: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(workDir, true); } catch { }
        }
        return result;
    }

    /// <summary>
    /// Exports the cover / thumbnail for a short as a JPEG next to the video: the user's custom image when
    /// set, otherwise the frame at <see cref="ShortSuggestion.CoverTime"/> (default 1 s in), framed with the
    /// camera cut active at that moment so it matches the rendered video. Returns the JPEG path or null.
    /// </summary>
    private static string F(double d) => d.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>
    /// Filters applied to a layer's still image: the entrance (pop grows it from 55 %, fade and slide raise its
    /// opacity) and a short fade-out at the end. All are per-frame expressions on a small image: cheap.
    /// </summary>
    private static string LayerAnimation(ImageOverlay o, double a, double b)
    {
        var f = "";
        switch (o.Animation)
        {
            case "pop": f += $",scale=w='iw*clip(0.55+0.45*(t-{F(a)})/0.22,0.05,1)':h=-1:eval=frame"; break;
            case "fade":
            case "slide": f += $",fade=t=in:st={F(a)}:d=0.3:alpha=1"; break;
        }
        if (o.Animation != "none" && b - a > 0.8) f += $",fade=t=out:st={F(b - 0.25)}:d=0.25:alpha=1";
        return f;
    }

    /// <summary>The frame time (seconds into the short) the cover uses, or null when it is a custom image.</summary>
    public static double? CoverFrameTime(ShortSuggestion s, string? language)
    {
        var (coverTime, coverImage) = s.CoverFor(language);
        if (!string.IsNullOrWhiteSpace(coverImage) && File.Exists(coverImage)) return null;
        return Math.Clamp(coverTime ?? Math.Min(1.0, s.Duration / 2), 0, Math.Max(0, s.Duration - 0.05));
    }

    public async Task<string?> ExportCoverAsync(VideoInfo video, ShortSuggestion s, GenerateOptions options, string videoOutPath, CancellationToken ct)
    {
        var coverPath = Path.ChangeExtension(videoOutPath, ".cover.jpg");
        var (coverTime, coverImage) = s.CoverFor(options.CaptionLanguage);
        try
        {
            if (!string.IsNullOrWhiteSpace(coverImage) && File.Exists(coverImage))
            {
                var (w, h) = OutputSize(video, options.CropMode);
                // normalise the custom image to the output size (cover-fit) so every network gets the right aspect
                await _ffmpeg.RunAsync(new[]
                {
                    "-y", "-i", coverImage,
                    "-vf", $"scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h}",
                    "-frames:v", "1", "-q:v", "2", coverPath
                }, null, null, 0, ct);
                return coverPath;
            }

            double t = Math.Clamp(coverTime ?? Math.Min(1.0, s.Duration / 2), 0, Math.Max(0, s.Duration - 0.05));
            string vf;
            if (options.CropMode == CropMode.VerticalCrop && video.Width > 0 && video.Height > 0)
            {
                var k = s.CameraAt(t) ?? CameraMath.Centered();
                var r = CameraMath.CropRect(video.Width, video.Height, k);
                vf = $"crop={r.W}:{r.H}:{r.X}:{r.Y},scale=1080:1920:flags=lanczos";
            }
            else if (options.CropMode == CropMode.VerticalBlurredBackground)
            {
                vf = "split=2[bg][fg];[bg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=25:8[bgb];" +
                     "[fg]scale=1080:1920:force_original_aspect_ratio=decrease[fgs];[bgb][fgs]overlay=(W-w)/2:(H-h)/2";
            }
            else vf = "scale=trunc(iw/2)*2:trunc(ih/2)*2";
            var coverLook = VisualLook.Get(options.Look);
            if (coverLook.Filter.Length > 0) vf += "," + coverLook.Filter;

            await _ffmpeg.RunAsync(new[]
            {
                "-y", "-ss", (s.StartSeconds + t).ToString("F3", CultureInfo.InvariantCulture), "-i", video.FilePath,
                "-filter_complex", "[0:v]" + vf + "[vout]", "-map", "[vout]",
                "-frames:v", "1", "-q:v", "2", coverPath
            }, null, null, 0, ct);
            return coverPath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public static (int W, int H) OutputSize(VideoInfo video, CropMode mode)
    {
        if (mode != CropMode.Original) return (1080, 1920);
        int w = video.Width > 0 ? video.Width : 1920;
        int h = video.Height > 0 ? video.Height : 1080;
        return (w / 2 * 2, h / 2 * 2);
    }

    private static string SafeFileName(string name)
    {
        var invalid = new string(Path.GetInvalidFileNameChars());
        var cleaned = Regex.Replace(name, $"[{Regex.Escape(invalid)}]", "");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        if (cleaned.Length > 60) cleaned = cleaned[..60].Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "short" : cleaned;
    }
}
