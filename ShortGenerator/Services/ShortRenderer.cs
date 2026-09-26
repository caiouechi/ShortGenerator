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

        var baseName = $"{index:00} - {SafeFileName(s.Title)}";
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

            if (options.AddCaptions && transcript is not null)
            {
                var style = CaptionStyle.Get(options.CaptionStyleId);
                var slice = transcript.Slice(s.StartSeconds, s.EndSeconds);
                s.MigrateLegacyCaptionPosition();
                var ass = CaptionBuilder.BuildAss(slice, style, options.WordsPerCaption, options.FontSize, outW, outH,
                    options.BurnTitleHook ? s.Hook : null, options.IncludeReactions, s.CaptionPositions);
                var assPath = Path.Combine(workDir, "captions.ass");
                await File.WriteAllTextAsync(assPath, ass, new System.Text.UTF8Encoding(false), ct);
                // Relative path avoids Windows drive-letter escaping problems inside the filter graph.
                filters.Add("subtitles=captions.ass");
            }

            // Filters chain with ','. The blurred-background entry contains its own labelled sub-graph
            // and ends with an unlabelled overlay output, so it chains like any other filter.
            // With camera cuts, the graph already starts at [0:v] (split/trim/concat) and the remaining filters follow it.
            var vf = cameraGraph is not null
                ? cameraGraph + (filters.Count > 0 ? "," + string.Join(",", filters) : "")
                : "[0:v]" + string.Join(",", filters);

            var args = new List<string>
            {
                "-y",
                "-ss", s.StartSeconds.ToString("F3", CultureInfo.InvariantCulture),
                "-to", s.EndSeconds.ToString("F3", CultureInfo.InvariantCulture),
                "-i", video.FilePath,
                "-filter_complex", vf + "[vout]",
                "-map", "[vout]", "-map", "0:a?",
                "-c:v", "libx264", "-preset", "fast", "-crf", "20", "-pix_fmt", "yuv420p", "-r", "30",
                "-c:a", "aac", "-b:a", "160k",
                "-movflags", "+faststart",
                outPath
            };

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
