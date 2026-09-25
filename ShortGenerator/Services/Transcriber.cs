using ShortGenerator.Models;
using Whisper.net;
using Whisper.net.Ggml;

namespace ShortGenerator.Services;

/// <summary>Local speech-to-text with Whisper.net (runs on the CPU, no API key needed).</summary>
public sealed class Transcriber
{
    private readonly FfmpegRunner _ffmpeg;
    private readonly string _modelFolder;

    public Transcriber(FfmpegRunner ffmpeg, string modelFolder)
    {
        _ffmpeg = ffmpeg;
        _modelFolder = modelFolder;
    }

    public static readonly string[] ModelNames = { "Tiny", "Base", "Small", "Medium", "LargeV3Turbo" };

    public static string DescribeModel(string name) => name switch
    {
        "Tiny" => "Tiny (75 MB) - fastest, rough accuracy",
        "Base" => "Base (142 MB) - fast, decent accuracy",
        "Small" => "Small (466 MB) - good accuracy",
        "Medium" => "Medium (1.5 GB) - very good, slow on CPU",
        "LargeV3Turbo" => "Large v3 Turbo (1.6 GB) - best accuracy, slowest",
        _ => name
    };

    private static GgmlType ParseModel(string name) => name switch
    {
        "Tiny" => GgmlType.Tiny,
        "Small" => GgmlType.Small,
        "Medium" => GgmlType.Medium,
        "LargeV3Turbo" => GgmlType.LargeV3Turbo,
        _ => GgmlType.Base
    };

    public string ModelPath(string modelName) => Path.Combine(_modelFolder, $"ggml-{modelName.ToLowerInvariant()}.bin");

    public bool IsModelDownloaded(string modelName) => File.Exists(ModelPath(modelName));

    public async Task EnsureModelAsync(string modelName, IProgress<string> log, CancellationToken ct)
    {
        var path = ModelPath(modelName);
        if (File.Exists(path)) return;
        Directory.CreateDirectory(_modelFolder);
        log.Report($"Downloading Whisper model '{modelName}' (first time only)...");
        var tmp = path + ".part";
        await using (var src = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ParseModel(modelName), QuantizationType.NoQuantization, ct))
        await using (var dst = File.Create(tmp))
        {
            await src.CopyToAsync(dst, ct);
        }
        File.Move(tmp, path, true);
        log.Report("Model ready.");
    }

    /// <param name="detectReactions">
    /// When true, Whisper is nudged to write laughs, applause, music etc. as bracketed tags (normalised to
    /// "[laughs]", "[applause]", ...). When false, any such annotation is stripped from the text.
    /// </param>
    public async Task<Transcript> TranscribeAsync(VideoInfo video, string modelName, string language, bool detectReactions, IProgress<double> progress, IProgress<string> log, CancellationToken ct)
    {
        await EnsureModelAsync(modelName, log, ct);

        var wav = Path.Combine(Path.GetTempPath(), $"shortgen_{Guid.NewGuid():N}.wav");
        try
        {
            log.Report("Extracting audio...");
            await _ffmpeg.ExtractWhisperAudioAsync(video.FilePath, wav, new Progress<double>(p => progress.Report(p * 0.1)), video.DurationSeconds, ct);

            log.Report($"Transcribing with Whisper {modelName}... (this runs locally and may take a while)");

            // Whisper is CPU-bound native code: keep it off the UI thread so the app stays responsive.
            var transcript = await Task.Run(async () =>
            {
                using var factory = WhisperFactory.FromPath(ModelPath(modelName));
                var builder = factory.CreateBuilder()
                    .WithThreads(Math.Max(1, Environment.ProcessorCount - 1))
                    // Anti-repetition: don't feed the previous window's text back as a prompt (the classic
                    // cause of one sentence repeating for minutes), and let whisper.cpp fall back to higher
                    // temperatures when a decode looks degenerate.
                    .WithNoContext()
                    .WithEntropyThreshold(2.4f)
                    .WithLogProbThreshold(-1.0f)
                    .WithTemperatureInc(0.2f);
                builder = string.IsNullOrWhiteSpace(language) || language == "auto"
                    ? builder.WithLanguageDetection()
                    : builder.WithLanguage(language);
                if (detectReactions)
                    builder = builder.WithPrompt(TranscriptEvents.PromptFor(language == "auto" ? "" : language));

                using var processor = builder.Build();
                var result = new Transcript();
                await using var stream = File.OpenRead(wav);

                await foreach (var seg in processor.ProcessAsync(stream, ct))
                {
                    var text = detectReactions ? TranscriptEvents.Normalize(seg.Text) : TranscriptEvents.Strip(seg.Text);
                    if (text.Length == 0) continue;
                    result.Segments.Add(new TranscriptSegment
                    {
                        Start = seg.Start.TotalSeconds,
                        End = seg.End.TotalSeconds,
                        Text = text
                    });
                    if (string.IsNullOrEmpty(result.Language) && !string.IsNullOrEmpty(seg.Language))
                        result.Language = seg.Language;
                    if (video.DurationSeconds > 0)
                        progress.Report(0.1 + 0.9 * Math.Clamp(seg.End.TotalSeconds / video.DurationSeconds, 0, 1));
                }
                return result;
            }, ct);

            int removed = RemoveRepetitionLoops(transcript);
            SplitLongSegments(transcript, maxSeconds: 6);

            if (detectReactions)
            {
                // Loudness pass: flag moments far above the speaker's normal level (big laughs, shouting, screams).
                try
                {
                    var energy = await Task.Run(() => AudioEnergy.FromWav(wav), ct);
                    int intense = 0;
                    foreach (var s in transcript.Segments)
                    {
                        s.Loudness = Math.Round(energy.PeakAboveMedian(s.Start, s.End), 1);
                        bool isIntense = energy.IsIntense(s.Start, s.End);
                        var tagged = TranscriptEvents.ApplyIntensity(s.Text, isIntense);
                        if (tagged != s.Text) { s.Text = tagged; intense++; }
                    }
                    log.Report($"Loudness analysis: {intense} intense moments flagged ({TranscriptEvents.BigLaugh}, {TranscriptEvents.Shouting}, ...).");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.Report("Loudness analysis skipped: " + ex.Message);
                }
            }
            if (removed > 0)
                log.Report($"Removed {removed} repeated segments that looked like a Whisper hallucination loop. " +
                           "If a passage is missing, re-run with a bigger model (Small or Medium) or set the language explicitly.");

            progress.Report(1);
            int reactions = detectReactions ? transcript.Segments.Count(s => TranscriptEvents.ContainsReaction(s.Text)) : 0;
            log.Report($"Transcription done: {transcript.Segments.Count} segments, language '{transcript.Language}'" +
                       (detectReactions ? $", {reactions} with laughs/reactions." : "."));
            return transcript;
        }
        finally
        {
            try { if (File.Exists(wav)) File.Delete(wav); } catch { }
        }
    }

    /// <summary>
    /// Whisper occasionally returns one very long segment (a whole 20-30 s window as a single line), which
    /// makes clip boundaries and caption timing coarse. Split such segments at sentence punctuation, never
    /// mid-word, distributing time by character count.
    /// </summary>
    public static void SplitLongSegments(Transcript transcript, double maxSeconds)
    {
        var result = new List<TranscriptSegment>(transcript.Segments.Count);
        foreach (var seg in transcript.Segments)
        {
            if (seg.Duration <= maxSeconds) { result.Add(seg); continue; }

            // sentence pieces: split after . ! ? … or a closing quote following them
            var pieces = System.Text.RegularExpressions.Regex.Split(seg.Text, @"(?<=[\.\!\?…][""”]?)\s+")
                .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (pieces.Count < 2) { result.Add(seg); continue; }

            // merge tiny pieces so we don't create 0.3 s captions, but keep each part under maxSeconds
            double perChar = seg.Duration / Math.Max(1, seg.Text.Length);
            var groups = new List<string>();
            var current = "";
            foreach (var p in pieces)
            {
                var candidate = current.Length == 0 ? p : current + " " + p;
                if (current.Length > 0 && candidate.Length * perChar > maxSeconds) { groups.Add(current); current = p; }
                else current = candidate;
            }
            if (current.Length > 0) groups.Add(current);

            double t = seg.Start;
            double totalChars = groups.Sum(g => g.Length);
            for (int i = 0; i < groups.Count; i++)
            {
                double d = i == groups.Count - 1 ? seg.End - t : seg.Duration * groups[i].Length / totalChars;
                result.Add(new TranscriptSegment { Start = t, End = t + d, Text = groups[i] });
                t += d;
            }
        }
        transcript.Segments = result;
    }

    /// <summary>
    /// Whisper sometimes gets stuck emitting the same sentence over and over (a decoding loop, not real speech).
    /// Collapse any run of 3+ consecutive identical segments down to its first occurrence.
    /// Returns how many segments were dropped.
    /// </summary>
    public static int RemoveRepetitionLoops(Transcript transcript)
    {
        var segs = transcript.Segments;
        if (segs.Count < 3) return 0;

        var kept = new List<TranscriptSegment>(segs.Count);
        int i = 0, removed = 0;
        while (i < segs.Count)
        {
            int j = i + 1;
            while (j < segs.Count && Normalize(segs[j].Text) == Normalize(segs[i].Text)) j++;
            int runLength = j - i;
            if (runLength >= 3)
            {
                kept.Add(segs[i]);            // keep one copy, drop the echo
                removed += runLength - 1;
            }
            else
            {
                for (int k = i; k < j; k++) kept.Add(segs[k]);
            }
            i = j;
        }
        transcript.Segments = kept;
        return removed;

        static string Normalize(string s) =>
            new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}
