using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShortGenerator.Services;

/// <summary>Thin wrapper around ffmpeg / ffprobe processes with progress reporting.</summary>
public sealed class FfmpegRunner
{
    private static readonly Regex TimeRegex = new(@"time=(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

    private readonly ToolLocator _tools;

    public FfmpegRunner(ToolLocator tools) => _tools = tools;

    public string FfmpegExe => _tools.FfmpegPath ?? throw new FileNotFoundException(
        "ffmpeg.exe was not found. Open Settings and click 'Download missing tools', or install ffmpeg and add it to PATH.");

    /// <summary>Returns (width, height, durationSeconds) using ffprobe; zeros when ffprobe is unavailable.</summary>
    public async Task<(int Width, int Height, double Duration)> ProbeAsync(string file, CancellationToken ct)
    {
        var ffprobe = _tools.FfprobePath;
        if (ffprobe is null) return (0, 0, 0);

        var psi = new ProcessStartInfo(ffprobe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var a in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height:format=duration", "-of", "json", file })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var json = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            int w = 0, h = 0; double d = 0;
            if (doc.RootElement.TryGetProperty("streams", out var streams) && streams.GetArrayLength() > 0)
            {
                var s = streams[0];
                if (s.TryGetProperty("width", out var wv)) w = wv.GetInt32();
                if (s.TryGetProperty("height", out var hv)) h = hv.GetInt32();
            }
            if (doc.RootElement.TryGetProperty("format", out var fmt) && fmt.TryGetProperty("duration", out var dv))
                double.TryParse(dv.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            return (w, h, d);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    /// <summary>Extracts mono 16 kHz PCM audio, which is what Whisper expects.</summary>
    public Task ExtractWhisperAudioAsync(string videoPath, string wavPath, IProgress<double>? progress, double totalSeconds, CancellationToken ct)
        => RunAsync(new[] { "-y", "-i", videoPath, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wavPath },
            null, progress, totalSeconds, ct);

    /// <summary>Runs ffmpeg with the given arguments. Progress is derived from the "time=" field in stderr.</summary>
    public async Task RunAsync(IEnumerable<string> args, string? workingDirectory, IProgress<double>? progress, double totalSeconds, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(FfmpegExe)
        {
            RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (workingDirectory is not null) psi.WorkingDirectory = workingDirectory;
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-stats");
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stderr = new StringBuilder();
        var buffer = new char[4096];
        var line = new StringBuilder();

        // ffmpeg writes stats with '\r' separators, so read char by char instead of line by line.
        while (true)
        {
            int read = await p.StandardError.ReadAsync(buffer, ct);
            if (read == 0) break;
            for (int i = 0; i < read; i++)
            {
                char c = buffer[i];
                if (c is '\r' or '\n')
                {
                    if (line.Length > 0)
                    {
                        var text = line.ToString();
                        line.Clear();
                        var m = TimeRegex.Match(text);
                        if (m.Success && progress is not null && totalSeconds > 0)
                        {
                            double t = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60
                                       + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                            progress.Report(Math.Clamp(t / totalSeconds, 0, 1));
                        }
                        else if (!m.Success)
                        {
                            stderr.AppendLine(text);
                        }
                    }
                }
                else line.Append(c);
            }
        }
        if (line.Length > 0) stderr.AppendLine(line.ToString());

        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited with code {p.ExitCode}:{Environment.NewLine}{stderr}");
        progress?.Report(1);
    }
}
