namespace YargDemucs.Audio;

/// <summary>Peak level of each channel of a 16-bit or float wav, as ffmpeg writes them.</summary>
public static class WavAudio
{
    public const float SilentPeak = 0.001f;

    public static bool HasEnergy(string path)
    {
        if (!TryReadPeaks(path, out var peaks, out bool unknownFormat))
        {
            return unknownFormat;
        }

        return peaks.Any(p => p >= SilentPeak);
    }

    /// <summary>unknownFormat is true when the file is a wav this reader cannot decode.</summary>
    public static bool TryReadPeaks(string path, out float[] peaks, out bool unknownFormat)
    {
        peaks = Array.Empty<float>();
        unknownFormat = false;
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 12 || new string(reader.ReadChars(4)) != "RIFF")
            {
                return false;
            }

            reader.ReadUInt32();
            reader.ReadChars(4); // WAVE
            int format = 1;
            int channels = 0;
            int bits = 16;
            while (stream.Position + 8 <= stream.Length)
            {
                string id = new string(reader.ReadChars(4));
                long size = reader.ReadUInt32();
                long start = stream.Position;
                if (id == "fmt " && size >= 16)
                {
                    format = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    reader.ReadUInt32();
                    reader.ReadUInt32();
                    reader.ReadUInt16();
                    bits = reader.ReadUInt16();
                    if (format == 0xFFFE && size >= 26)
                    {
                        // WAVE_FORMAT_EXTENSIBLE: the real format starts the sub-format GUID.
                        reader.ReadUInt16();
                        reader.ReadUInt16();
                        reader.ReadUInt32();
                        format = reader.ReadUInt16();
                    }
                }
                else if (id == "data")
                {
                    if (channels <= 0)
                    {
                        return false;
                    }

                    // Streamed ffmpeg output may carry a placeholder size; read to the end.
                    long available = stream.Length - start;
                    if (size == 0 || size > available)
                    {
                        size = available;
                    }

                    if (!(format == 1 && bits == 16) && !(format == 3 && bits == 32))
                    {
                        unknownFormat = true;
                        return false;
                    }

                    peaks = ReadPeaks(stream, size, channels, format == 3);
                    return true;
                }

                long next = start + size + (size & 1);
                if (next <= start || next > stream.Length)
                {
                    break;
                }

                stream.Position = next;
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            unknownFormat = true;
        }

        return false;
    }

    private static float[] ReadPeaks(Stream stream, long size, int channels, bool isFloat)
    {
        var peaks = new float[channels];
        int sampleBytes = isFloat ? 4 : 2;
        int frameBytes = sampleBytes * channels;
        var buffer = new byte[Math.Max(frameBytes, 65536 - 65536 % frameBytes)];
        long remaining = size - size % frameBytes;
        long sampleIndex = 0;
        while (remaining > 0)
        {
            int want = (int) Math.Min(buffer.Length, remaining);
            int read = ReadFull(stream, buffer, want);
            if (read < frameBytes)
            {
                break;
            }

            read -= read % frameBytes;
            remaining -= read;
            for (int i = 0; i < read; i += sampleBytes, sampleIndex++)
            {
                float value = isFloat
                    ? Math.Abs(BitConverter.ToSingle(buffer, i))
                    : Math.Abs((int) (short) (buffer[i] | (buffer[i + 1] << 8))) / 32768f;
                int channel = (int) (sampleIndex % channels);
                if (value > peaks[channel])
                {
                    peaks[channel] = value;
                }
            }
        }

        return peaks;
    }

    private static int ReadFull(Stream stream, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, total, count - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
