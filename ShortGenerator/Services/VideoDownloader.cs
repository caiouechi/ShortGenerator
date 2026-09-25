using ShortGenerator.Models;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace ShortGenerator.Services;

/// <summary>Downloads YouTube / Instagram / TikTok videos (and anything else yt-dlp supports) as MP4.</summary>
public sealed class VideoDownloader
{
    private readonly ToolLocator _tools;
    private readonly FfmpegRunner _ffmpeg;

    public VideoDownloader(ToolLocator tools, FfmpegRunner ffmpeg)
    {
        _tools = tools;
        _ffmpeg = ffmpeg;
    }

    public static VideoSource DetectSource(string url)
    {
        var u = url.ToLowerInvariant();
        if (u.Contains("youtube.com") || u.Contains("youtu.be")) return VideoSource.YouTube;
        if (u.Contains("instagram.com")) return VideoSource.Instagram;
        if (u.Contains("tiktok.com")) return VideoSource.TikTok;
        return VideoSource.Other;
    }

    public static bool LooksLikeUrl(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https");

    public async Task<VideoInfo> DownloadAsync(string url, string outputFolder, IProgress<double> progress, IProgress<string> log, CancellationToken ct)
    {
        var ytdlpPath = _tools.YtDlpPath ?? throw new FileNotFoundException(
            "yt-dlp.exe was not found. Open Settings and click 'Download missing tools'.");
        Directory.CreateDirectory(outputFolder);

        var ytdl = new YoutubeDL
        {
            YoutubeDLPath = ytdlpPath,
            FFmpegPath = _tools.FfmpegPath ?? "ffmpeg",
            OutputFolder = outputFolder,
            OutputFileTemplate = "%(title).80s [%(id)s].%(ext)s",
            RestrictFilenames = true,
            OverwriteFiles = true,
            IgnoreDownloadErrors = false
        };

        log.Report("Fetching video metadata...");
        var info = new VideoInfo { Url = url, Source = DetectSource(url) };
        var meta = await ytdl.RunVideoDataFetch(url, ct);
        if (meta.Success && meta.Data is not null)
        {
            info.Title = meta.Data.Title ?? "";
            info.Uploader = meta.Data.Uploader ?? "";
            info.DurationSeconds = meta.Data.Duration ?? 0;
            log.Report($"Found: {info.Title} ({TimeSpan.FromSeconds(info.DurationSeconds):mm\\:ss})");
        }
        else
        {
            log.Report("Metadata fetch failed, trying the download anyway: " + string.Join(" ", meta.ErrorOutput ?? Array.Empty<string>()));
        }

        log.Report("Downloading...");
        var progressHandler = new Progress<DownloadProgress>(p =>
        {
            if (p.State == DownloadState.Downloading) progress.Report(p.Progress);
            else if (p.State is DownloadState.PostProcessing) progress.Report(0.99);
        });
        var outputHandler = new Progress<string>(s =>
        {
            if (!string.IsNullOrWhiteSpace(s) && !s.Contains("[download]")) log.Report(s.Trim());
        });

        // Prefer MP4 (H.264 + AAC) so ffmpeg can cut it quickly and every player can open it.
        const string format = "bestvideo[ext=mp4][vcodec^=avc1]+bestaudio[ext=m4a]/bestvideo[ext=mp4]+bestaudio/best[ext=mp4]/best";
        var overrides = new OptionSet { NoPlaylist = true };

        var result = await ytdl.RunVideoDownload(url, format, DownloadMergeFormat.Mp4, VideoRecodeFormat.None, ct, progressHandler, outputHandler, overrides);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Data) || !File.Exists(result.Data))
        {
            var err = string.Join(Environment.NewLine, result.ErrorOutput ?? Array.Empty<string>());
            throw new InvalidOperationException(
                "Download failed." + Environment.NewLine + err + Environment.NewLine +
                "Tip: sites change often. Try Settings -> 'Update yt-dlp'. Private Instagram/TikTok posts cannot be downloaded.");
        }

        info.FilePath = result.Data;
        if (string.IsNullOrWhiteSpace(info.Title)) info.Title = Path.GetFileNameWithoutExtension(info.FilePath);

        var probe = await _ffmpeg.ProbeAsync(info.FilePath, ct);
        if (probe.Width > 0) { info.Width = probe.Width; info.Height = probe.Height; }
        if (probe.Duration > 0) info.DurationSeconds = probe.Duration;

        progress.Report(1);
        log.Report($"Saved to {info.FilePath}");
        return info;
    }

    public async Task<VideoInfo> LoadLocalAsync(string filePath, CancellationToken ct)
    {
        var info = new VideoInfo
        {
            FilePath = filePath,
            Title = Path.GetFileNameWithoutExtension(filePath),
            Source = VideoSource.LocalFile,
            Url = filePath
        };
        var probe = await _ffmpeg.ProbeAsync(filePath, ct);
        info.Width = probe.Width;
        info.Height = probe.Height;
        info.DurationSeconds = probe.Duration;
        return info;
    }
}
