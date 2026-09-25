using System.Diagnostics;

namespace ShortGenerator.Services;

/// <summary>Finds yt-dlp / ffmpeg / ffprobe: first in the app's tools folder, then next to the exe, then on PATH.</summary>
public sealed class ToolLocator
{
    private readonly string _toolsFolder;

    public ToolLocator(string toolsFolder)
    {
        _toolsFolder = toolsFolder;
    }

    public string ToolsFolder => _toolsFolder;

    public string? YtDlpPath => Find("yt-dlp.exe");
    public string? FfmpegPath => Find("ffmpeg.exe");
    public string? FfprobePath => Find("ffprobe.exe") ?? SiblingOf(FfmpegPath, "ffprobe.exe");

    private string? Find(string exe)
    {
        var candidates = new[]
        {
            Path.Combine(_toolsFolder, exe),
            Path.Combine(AppContext.BaseDirectory, exe),
            Path.Combine(AppContext.BaseDirectory, "tools", exe),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim(), exe);
                if (File.Exists(p)) return p;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    private static string? SiblingOf(string? path, string exe)
    {
        if (path is null) return null;
        var dir = Path.GetDirectoryName(path);
        if (dir is null) return null;
        // WinGet exposes ffmpeg through a links folder; resolve the link target when possible.
        var candidate = Path.Combine(dir, exe);
        if (File.Exists(candidate)) return candidate;
        try
        {
            var target = new FileInfo(path).LinkTarget;
            if (target is not null)
            {
                var realDir = Path.GetDirectoryName(Path.GetFullPath(target, dir));
                if (realDir is not null && File.Exists(Path.Combine(realDir, exe)))
                    return Path.Combine(realDir, exe);
            }
        }
        catch { }
        return null;
    }

    /// <summary>Downloads the latest yt-dlp.exe (and ffmpeg if missing) into the tools folder.</summary>
    public async Task DownloadMissingToolsAsync(IProgress<string> log, CancellationToken ct)
    {
        Directory.CreateDirectory(_toolsFolder);
        if (YtDlpPath is null)
        {
            log.Report("Downloading yt-dlp.exe from GitHub releases...");
            await YoutubeDLSharp.Utils.DownloadYtDlp(_toolsFolder);
            log.Report("yt-dlp downloaded.");
        }
        ct.ThrowIfCancellationRequested();
        if (FfmpegPath is null)
        {
            log.Report("Downloading ffmpeg.exe (this can take a minute)...");
            await YoutubeDLSharp.Utils.DownloadFFmpeg(_toolsFolder);
            log.Report("ffmpeg downloaded.");
        }
        if (FfprobePath is null)
        {
            log.Report("Downloading ffprobe.exe...");
            await YoutubeDLSharp.Utils.DownloadFFprobe(_toolsFolder);
            log.Report("ffprobe downloaded.");
        }
    }

    /// <summary>Asks yt-dlp to update itself (yt-dlp -U). Sites change often; this fixes most download failures.</summary>
    public async Task<string> UpdateYtDlpAsync(CancellationToken ct)
    {
        var exe = YtDlpPath ?? throw new FileNotFoundException("yt-dlp.exe not found.");
        var psi = new ProcessStartInfo(exe, "-U")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        var err = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (output + Environment.NewLine + err).Trim();
    }
}
