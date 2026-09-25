namespace ShortGenerator.Services;

/// <summary>
/// Loudness profile of the 16 kHz mono PCM WAV that Whisper consumes. Used to flag moments that are
/// much louder than the speaker's normal level: big laughs, cheering, shouting, screams.
/// </summary>
public sealed class AudioEnergy
{
    public const double WindowSeconds = 0.25;

    private readonly double[] _rmsDb;      // one value per window, in dBFS
    public double MedianDb { get; }
    public double P95Db { get; }

    private AudioEnergy(double[] rmsDb)
    {
        _rmsDb = rmsDb;
        var speech = rmsDb.Where(d => d > -60).OrderBy(d => d).ToArray(); // ignore silence
        MedianDb = speech.Length > 0 ? speech[speech.Length / 2] : -30;
        P95Db = speech.Length > 0 ? speech[(int)(speech.Length * 0.95)] : -20;
    }

    public static AudioEnergy FromWav(string wavPath)
    {
        using var fs = File.OpenRead(wavPath);
        using var br = new BinaryReader(fs);

        // minimal RIFF parse: find "fmt " and "data" chunks
        if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a WAV file.");
        br.ReadInt32();
        if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAV file.");
        int channels = 1, sampleRate = 16000, bits = 16;
        long dataStart = -1, dataLen = 0;
        while (fs.Position + 8 <= fs.Length)
        {
            var id = new string(br.ReadChars(4));
            int len = br.ReadInt32();
            if (id == "fmt ")
            {
                br.ReadInt16(); channels = br.ReadInt16(); sampleRate = br.ReadInt32(); br.ReadInt32(); br.ReadInt16(); bits = br.ReadInt16();
                fs.Position += len - 16;
            }
            else if (id == "data") { dataStart = fs.Position; dataLen = Math.Min(len, fs.Length - fs.Position); break; }
            else fs.Position += len + (len & 1);
        }
        if (dataStart < 0 || bits != 16) throw new InvalidDataException("Unsupported WAV layout.");

        fs.Position = dataStart;
        int samplesPerWindow = (int)(sampleRate * WindowSeconds) * channels;
        long totalSamples = dataLen / 2;
        var windows = new List<double>((int)(totalSamples / samplesPerWindow) + 1);
        var buffer = new byte[samplesPerWindow * 2];
        long remaining = dataLen;
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = fs.Read(buffer, 0, toRead);
            if (read <= 0) break;
            remaining -= read;
            double sum = 0; int n = read / 2;
            for (int i = 0; i < n; i++)
            {
                double s = BitConverter.ToInt16(buffer, i * 2) / 32768.0;
                sum += s * s;
            }
            double rms = n > 0 ? Math.Sqrt(sum / n) : 0;
            windows.Add(rms > 1e-6 ? 20 * Math.Log10(rms) : -100);
        }
        return new AudioEnergy(windows.ToArray());
    }

    /// <summary>Loudest window inside [start, end], in dBFS.</summary>
    public double PeakDb(double start, double end)
    {
        int a = Math.Clamp((int)(start / WindowSeconds), 0, Math.Max(0, _rmsDb.Length - 1));
        int b = Math.Clamp((int)Math.Ceiling(end / WindowSeconds), a, Math.Max(0, _rmsDb.Length - 1));
        double peak = -100;
        for (int i = a; i <= b && i < _rmsDb.Length; i++) peak = Math.Max(peak, _rmsDb[i]);
        return peak;
    }

    /// <summary>Average level of the non-silent windows in [start, end], in dBFS.</summary>
    public double MeanDb(double start, double end)
    {
        int a = Math.Clamp((int)(start / WindowSeconds), 0, Math.Max(0, _rmsDb.Length - 1));
        int b = Math.Clamp((int)Math.Ceiling(end / WindowSeconds), a, Math.Max(0, _rmsDb.Length - 1));
        double sum = 0; int n = 0;
        for (int i = a; i <= b && i < _rmsDb.Length; i++) if (_rmsDb[i] > -60) { sum += _rmsDb[i]; n++; }
        return n > 0 ? sum / n : MedianDb;
    }

    /// <summary>How far above the speaker's normal level the loudest moment of the range is, in dB.</summary>
    public double PeakAboveMedian(double start, double end) => PeakDb(start, end) - MedianDb;

    /// <summary>
    /// True when the range contains a genuine burst: a moment in the loudest ~5% of the recording, well above
    /// normal speech, AND clearly louder than the seconds around it. The last test stops a uniformly loud
    /// passage (a produced voice-over, background music) from being flagged as shouting.
    /// </summary>
    public bool IsIntense(double start, double end)
    {
        double peak = PeakDb(start, end);
        double neighbourhood = MeanDb(start - 4, end + 4);
        return peak >= P95Db && peak - MedianDb >= 8 && peak - neighbourhood >= 6;
    }
}
