using System.Text;

namespace YargDemucs.Library;

/// <summary>
/// Reads the file list of a .sng archive (SNGPKG) and extracts files from it.
/// Files are masked with a 16-byte key: byte i is xored with key[i % 16] ^ (i % 256).
/// </summary>
public sealed class SngArchive
{
    public readonly record struct Entry(long Position, long Length);

    private static readonly byte[] Magic = "SNGPKG"u8.ToArray();

    public string Path { get; }
    public Dictionary<string, Entry> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly byte[] _mask = new byte[256];

    private SngArchive(string path)
    {
        Path = path;
    }

    public static SngArchive? TryOpen(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            var magic = reader.ReadBytes(Magic.Length);
            if (!magic.AsSpan().SequenceEqual(Magic))
            {
                return null;
            }

            reader.ReadUInt32(); // version
            var keys = reader.ReadBytes(16);
            var archive = new SngArchive(path);
            for (int i = 0; i < 256; i++)
            {
                archive._mask[i] = (byte) (keys[i % 16] ^ i);
            }

            long metadataLength = reader.ReadInt64();
            stream.Position += metadataLength;

            reader.ReadInt64(); // file index length
            ulong count = reader.ReadUInt64();
            for (ulong i = 0; i < count; i++)
            {
                int nameLength = reader.ReadByte();
                string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                long length = reader.ReadInt64();
                long position = reader.ReadInt64();
                archive.Files[name] = new Entry(position, length);
            }

            return archive;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>True when the archive has no vocals, bass, or drums audio (placeholders don't count).</summary>
    public bool MissesCoreStems()
    {
        foreach (var (name, entry) in Files)
        {
            if (entry.Length > StemLayout.PlaceholderMaxBytes && StemLayout.IsAudio(name) &&
                StemLayout.IsCoreStem(System.IO.Path.GetFileNameWithoutExtension(name)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Name of the biggest song.* audio file, or null.</summary>
    public string? FindFullMix()
    {
        string? best = null;
        long bestLength = StemLayout.PlaceholderMaxBytes;
        foreach (var format in StemLayout.AudioFormats)
        {
            if (Files.TryGetValue("song" + format, out var entry) && entry.Length > bestLength)
            {
                best = "song" + format;
                bestLength = entry.Length;
            }
        }

        return best;
    }

    public void Extract(string name, string destination)
    {
        var entry = Files[name];
        using var input = File.OpenRead(Path);
        using var output = File.Create(destination);
        input.Position = entry.Position;
        var buffer = new byte[1 << 16];
        long done = 0;
        while (done < entry.Length)
        {
            int want = (int) Math.Min(buffer.Length, entry.Length - done);
            int read = input.Read(buffer, 0, want);
            if (read <= 0)
            {
                throw new EndOfStreamException("The .sng file ended early: " + Path);
            }

            for (int i = 0; i < read; i++)
            {
                buffer[i] ^= _mask[(done + i) & 0xFF];
            }

            output.Write(buffer, 0, read);
            done += read;
        }
    }
}
