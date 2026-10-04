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
            // video clips need their source size for the crop
            var sizes = new Dictionary<string, (int W, int H)>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in layers.Where(o => o.IsVideo))
                if (!sizes.ContainsKey(o.Path)) { var (w, h, _) = await _ffmpeg.ProbeAsync(o.Path, ct); sizes[o.Path] = (w, h); }
            layers.RemoveAll(o => o.IsVideo && (sizes[o.Path].W <= 0 || sizes[o.Path].H <= 0));
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
                    if (o.IsVideo)
                    {
                        // a moment of another video: cut from its source, cropped and scaled, shifted to its window;
                        // its sound is not used (the short keeps the main audio), captions still go on top
                        var (sw, sh) = sizes[o.Path];
                        args.AddRange(new[] { "-ss", F(Math.Max(0, o.SourceStart)), "-t", F(b - a + 0.1), "-i", o.Path });
                        string chain;
                        string pos;
                        if (o.FullFrame)
                        {
                            var r = o.CropRect(sw, sh, (double)outW / outH);
                            chain = $"crop={r.W}:{r.H}:{r.X}:{r.Y},scale={outW}:{outH}:flags=lanczos";
                            pos = "x=0:y=0";
                        }
                        else
                        {
                            var r = o.CropRect(sw, sh, o.Aspect > 0 ? o.Aspect : (double)sw / sh);
                            int boxW = Math.Max(2, (int)Math.Round(o.Size / 100.0 * outW / 2) * 2);
                            int border = o.Style == "plain" ? 0 : Math.Max(4, (int)Math.Round(boxW * 0.012 / 2) * 2);
                            chain = $"crop={r.W}:{r.H}:{r.X}:{r.Y},scale={boxW - 2 * border}:-2:flags=lanczos" + (border > 0 ? $",pad=iw+{2 * border}:ih+{2 * border}:{border}:{border}:white" : "");
                            pos = $"x='{F(o.X / 100.0 * outW)}-w/2':y='{F(o.Y / 100.0 * outH)}-h/2'";
                        }
                        sb.Append($";[{i + 1}:v]{chain},setsar=1,fps=30,setpts=PTS-STARTPTS+{F(a)}/TB[o{i}]");
                        sb.Append($";[{prev}][o{i}]overlay={pos}:enable='between(t,{F(a)},{F(b)})':eof_action=pass[b{i}]");
                        prev = $"b{i}";
                        continue;
                    }
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

    /// <summary>How long the cover image opens the TikTok copy; TikTok's cover is then pointed at its middle.</summary>
    public const double CoverLeadSeconds = 0.1;

    /// <summary>
    /// TikTok takes no cover image through its API, only the time of a frame inside the video. This makes a copy of
    /// the short that opens with the cover image for <see cref="CoverLeadSeconds"/> (three frames at 30 fps, the audio
    /// shifted to match), so TikTok's cover can point at it. Same encode as the render. Returns the copy's path, in a
    /// temporary folder under the same file name (galiluna names the stored file after it); the caller deletes it.
    /// </summary>
    public async Task<string> MakeCoverLeadCopyAsync(string videoPath, string coverPath, CancellationToken ct)
    {
        var (w, h, _) = await _ffmpeg.ProbeAsync(videoPath, ct);
        if (w <= 0 || h <= 0) (w, h) = (1080, 1920);
        var dir = Path.Combine(Path.GetTempPath(), "ShortGenerator", "tiktok-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var outPath = Path.Combine(dir, Path.GetFileName(videoPath));
        int delayMs = (int)Math.Round(CoverLeadSeconds * 1000);
        string Graph(bool audio) =>
            $"[1:v]scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h},setsar=1,fps=30,format=yuv420p[c];" +
            $"[0:v]setsar=1,fps=30,format=yuv420p[v];[c][v]concat=n=2:v=1:a=0[ov]" +
            (audio ? $";[0:a]adelay={delayMs}:all=1[oa]" : "");
        string crf = "20";
        IEnumerable<string> Args(bool audio)
        {
            var a = new List<string>
            {
                "-y", "-i", videoPath, "-loop", "1", "-framerate", "30", "-t", F(CoverLeadSeconds), "-i", coverPath,
                "-filter_complex", Graph(audio), "-map", "[ov]",
            };
            if (audio) a.AddRange(new[] { "-map", "[oa]", "-c:a", "aac", "-b:a", "160k" });
            a.AddRange(new[] { "-c:v", "libx264", "-preset", "fast", "-crf", crf, "-pix_fmt", "yuv420p", "-r", "30", "-movflags", "+faststart", outPath });
            return a;
        }
        bool audio = true;
        try
        {
            await _ffmpeg.RunAsync(Args(audio), null, null, 0, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // a short without an audio track: same copy, video only
            audio = false;
            await _ffmpeg.RunAsync(Args(audio), null, null, 0, ct);
        }
        // galiluna takes up to 48 MB; a re-encode can land a little above a file that was close to it
        if (new FileInfo(outPath).Length > 46L * 1024 * 1024)
        {
            crf = "24";
            await _ffmpeg.RunAsync(Args(audio), null, null, 0, ct);
        }
        return outPath;
    }

    /// <summary>The frame time (seconds into the short) the cover uses, or null when it is a custom image.</summary>
    public static double? CoverFrameTime(ShortSuggestion s, string? language)
    {
        var (coverTime, coverImage) = s.CoverFor(language);
        if (!string.IsNullOrWhiteSpace(coverImage) && File.Exists(coverImage)) return null;
        return Math.Clamp(coverTime ?? Math.Min(1.0, s.Duration / 2), 0, Math.Max(0, s.Duration - 0.05));
    }

    /// <summary>
    /// One frame of the short at <paramref name="t"/> (seconds into it), framed like the render (camera cut, crop mode,
    /// look) and scaled to <paramref name="width"/>: what the viewer sees at that moment, without captions or layers.
    /// The clip dialog shows it to place a small video window on the short.
    /// </summary>
    public async Task<string> ExportShortFrameAsync(VideoInfo video, ShortSuggestion s, GenerateOptions options, double t, string outPath, int width, CancellationToken ct)
    {
        t = Math.Clamp(t, 0, Math.Max(0, s.Duration - 0.05));
        string vf;
        if (options.CropMode == CropMode.VerticalCrop && video.Width > 0 && video.Height > 0)
        {
            var k = s.CameraAt(t) ?? CameraMath.Centered();
            var r = CameraMath.CropRect(video.Width, video.Height, k);
            vf = $"crop={r.W}:{r.H}:{r.X}:{r.Y},scale={width}:-2:flags=lanczos";
        }
        else if (options.CropMode == CropMode.VerticalBlurredBackground)
        {
            vf = "split=2[bg][fg];[bg]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=25:8[bgb];" +
                 $"[fg]scale=1080:1920:force_original_aspect_ratio=decrease[fgs];[bgb][fgs]overlay=(W-w)/2:(H-h)/2,scale={width}:-2";
        }
        else vf = $"scale={width}:-2";
        var look = VisualLook.Get(options.Look);
        if (look.Filter.Length > 0) vf += "," + look.Filter;
        await _ffmpeg.RunAsync(new[]
        {
            "-y", "-ss", (s.StartSeconds + t).ToString("F3", CultureInfo.InvariantCulture), "-i", video.FilePath,
            "-filter_complex", "[0:v]" + vf + "[vout]", "-map", "[vout]", "-frames:v", "1", "-q:v", "3", outPath
        }, null, null, 0, ct);
        return outPath;
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

    internal static string SafeFileName(string name)
    {
        var invalid = new string(Path.GetInvalidFileNameChars());
        var cleaned = Regex.Replace(name, $"[{Regex.Escape(invalid)}]", "");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        if (cleaned.Length > 60) cleaned = cleaned[..60].Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "short" : cleaned;
    }
}
