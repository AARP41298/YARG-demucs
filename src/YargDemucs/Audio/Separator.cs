using System.Globalization;
using System.Text;

namespace YargDemucs.Audio;

/// <summary>ffmpeg and Demucs steps: stereo mix, separation, encoding, and silence checks.</summary>
public sealed class Separator
{
    private static readonly double[] ProbeSeconds = { 20, 45, 90, 120 };
    private readonly ProcessRunner _runner;
    private int _launcher = -1;

    public Separator(ProcessRunner runner)
    {
        _runner = runner;
    }

    public int Shifts { get; set; } = 1;
    public string? Device { get; set; }
    public string? Model { get; set; }

    /// <summary>
    /// Writes a 44.1 kHz stereo wav of the mix. songChannels picks the song stem out of a
    /// multi-channel mogg; if those channels are silent, every channel is mixed down instead.
    /// </summary>
    public void MakeStereoMix(string input, string output, int[] songChannels)
    {
        songChannels = VorbisChannels.ToFfmpegOrder(input, songChannels);
        bool picked = songChannels.Length > 0;
        if (picked)
        {
            Ffmpeg(input, output, "-af", BuildPanFilter(songChannels));
            if (WavAudio.HasEnergy(output))
            {
                return;
            }
        }

        Ffmpeg(input, output, "-ac", "2");
        if (!WavAudio.HasEnergy(output))
        {
            throw new StemException("The song mix is silent, so Demucs cannot separate it.");
        }
    }

    /// <summary>Runs Demucs and returns the folder with vocals, bass, drums, and other wavs.</summary>
    public string Separate(string stereoWav, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var args = new List<string> { "--shifts=" + Math.Clamp(Shifts, 1, 8) };
        if (!string.IsNullOrWhiteSpace(Device))
        {
            args.Add("-d");
            args.Add(Device);
        }

        if (!string.IsNullOrWhiteSpace(Model))
        {
            args.Add("-n");
            args.Add(Model);
        }

        args.Add("-o");
        args.Add(outDir);
        args.Add(stereoWav);

        // demucs on PATH, else the Python launcher, else python.
        var launchers = new (string File, string[] Prefix)[]
        {
            ("demucs", Array.Empty<string>()),
            ("py", new[] { "-m", "demucs" }),
            ("python", new[] { "-m", "demucs" }),
        };
        int[] order = _launcher >= 0 ? new[] { _launcher } : new[] { 0, 1, 2 };
        string lastError = "demucs was not found. Install it (pip install demucs) and make sure it is on PATH.";
        foreach (int i in order)
        {
            var (file, prefix) = launchers[i];
            var result = _runner.Run(file, prefix.Concat(args));
            if (_runner.IsStopRequested)
            {
                throw new OperationCanceledException();
            }

            if (!result.Started || IsMissingDemucs(result.Output))
            {
                if (_launcher == i)
                {
                    _launcher = -1;
                    return Separate(stereoWav, outDir);
                }

                continue;
            }

            string? found = FindSeparated(outDir);
            if (result.ExitCode == 0 && found != null)
            {
                _launcher = i;
                return found;
            }

            lastError = result.Output.Length > 0 ? result.Output : "demucs exited with code " + result.ExitCode + ".";
            break;
        }

        throw new StemException(SummarizeError(lastError));
    }

    /// <summary>Copies a wav, or encodes it as opus/ogg/mp3, to destination (extension decides).</summary>
    public void Encode(string wav, string destination, int bitrateKbps)
    {
        string ext = Path.GetExtension(destination).ToLowerInvariant();
        if (ext == ".wav")
        {
            File.Copy(wav, destination, true);
            return;
        }

        string codec = ext switch
        {
            ".opus" => "libopus",
            ".ogg" => "libvorbis",
            ".mp3" => "libmp3lame",
            _ => throw new StemException("Unsupported output format: " + ext),
        };
        Ffmpeg(wav, destination, "-c:a", codec, "-b:a", bitrateKbps + "k");
    }

    /// <summary>
    /// Listens to one second at a few points. True when it could tell; silent is true when the
    /// given channels are quiet at every point where something else plays. Intros often have
    /// only one instrument, so one quiet point is not enough.
    /// </summary>
    public bool TryCheckSilence(string audio, int[] channels, string workDir, out bool silent)
    {
        silent = false;
        channels = VorbisChannels.ToFfmpegOrder(audio, channels);

        // One ffmpeg run decodes every point: each is its own input, seeked, with its own output.
        var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };
        foreach (double seconds in ProbeSeconds)
        {
            args.AddRange(new[] { "-ss", seconds.ToString(CultureInfo.InvariantCulture), "-t", "1", "-i", audio });
        }

        var slices = new string[ProbeSeconds.Length];
        for (int i = 0; i < slices.Length; i++)
        {
            slices[i] = Path.Combine(workDir, $"slice{i}.wav");
            args.AddRange(new[] { "-map", i + ":a", "-c:a", "pcm_f32le", slices[i] });
        }

        var result = _runner.Run("ffmpeg", args);
        if (_runner.IsStopRequested)
        {
            throw new OperationCanceledException();
        }

        if (!result.Started)
        {
            throw new StemException("ffmpeg was not found. It is required to read the audio.");
        }

        // Points past the end of a short song come out empty and are skipped.
        int heard = 0;
        foreach (var slice in slices)
        {
            if (!File.Exists(slice) || !WavAudio.TryReadPeaks(slice, out var peaks, out _) ||
                !peaks.Any(p => p >= WavAudio.SilentPeak))
            {
                continue;
            }

            heard++;
            if (channels.Any(c => (uint) c < (uint) peaks.Length && peaks[c] >= WavAudio.SilentPeak))
            {
                return true;
            }
        }

        silent = heard > 0;
        return heard > 0;
    }

    private void Ffmpeg(string input, string output, params string[] options)
    {
        var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error", "-i", input };
        args.AddRange(options);
        if (Path.GetExtension(output).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-ar");
            args.Add("44100");
        }

        args.Add(output);
        var result = _runner.Run("ffmpeg", args);
        if (_runner.IsStopRequested)
        {
            throw new OperationCanceledException();
        }

        if (!result.Started)
        {
            throw new StemException("ffmpeg was not found. Install it and make sure it is on PATH.");
        }

        if (result.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length < 4096)
        {
            throw new StemException("ffmpeg failed: " + SummarizeError(result.Output));
        }
    }

    private static string BuildPanFilter(int[] channels)
    {
        if (channels.Length == 1)
        {
            return $"pan=stereo|c0=c{channels[0]}|c1=c{channels[0]}";
        }

        var left = new StringBuilder();
        var right = new StringBuilder();
        for (int i = 0; i < channels.Length; i++)
        {
            var side = i % 2 == 0 ? left : right;
            if (side.Length > 0)
            {
                side.Append('+');
            }

            side.Append('c').Append(channels[i]);
        }

        return "pan=stereo|c0=" + left + "|c1=" + right;
    }

    private static string? FindSeparated(string outDir)
    {
        foreach (var directory in Directory.EnumerateDirectories(outDir, "*", SearchOption.AllDirectories))
        {
            if (new[] { "vocals", "bass", "drums", "other" }.All(s => File.Exists(Path.Combine(directory, s + ".wav"))))
            {
                return directory;
            }
        }

        return null;
    }

    private static bool IsMissingDemucs(string output)
    {
        return output.Contains("No module named demucs", StringComparison.OrdinalIgnoreCase)
            || output.Contains("is not recognized as an internal or external command", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Picks the useful line out of a Python traceback.</summary>
    public static string SummarizeError(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Unknown error";
        }

        if (text.Contains("CUDA out of memory", StringComparison.OrdinalIgnoreCase))
        {
            return "CUDA out of memory";
        }

        string? last = null;
        string? exception = null;
        foreach (var raw in text.Split('\r', '\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("File ") || line.StartsWith("Traceback") ||
                line.StartsWith("During handling") || line.Contains("site-packages", StringComparison.OrdinalIgnoreCase) ||
                line.Trim('^', '~', ' ').Length == 0)
            {
                continue;
            }

            last = line;
            int colon = line.IndexOf(':');
            string head = (colon > 0 ? line[..colon] : line).Trim();
            head = head[(head.LastIndexOf('.') + 1)..];
            if (head.EndsWith("Error", StringComparison.Ordinal) || head.EndsWith("Exception", StringComparison.Ordinal))
            {
                exception = line;
            }
        }

        string chosen = exception ?? last ?? text.Trim();
        return chosen.Length > 180 ? chosen[..180] : chosen;
    }
}

public sealed class StemException : Exception
{
    public StemException(string message) : base(message)
    {
    }
}
