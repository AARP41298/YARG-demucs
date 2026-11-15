using YargDemucs.Audio;
using YargDemucs.Library;

namespace YargDemucs.Stems;

/// <summary>Gets a song's full mix out of its folder, .sng, CON, or mogg.</summary>
public sealed class SongAudio
{
    private const int UnencryptedMogg = 0x0A;
    private readonly Nautilus? _nautilus;

    public SongAudio(Nautilus? nautilus)
    {
        _nautilus = nautilus;
    }

    /// <summary>Writes the full mix into workDir and returns its path.</summary>
    public string ExportMix(Song song, string workDir)
    {
        switch (song.Kind)
        {
            case SongKind.Folder:
            {
                // After a run the original mix is kept as song.pre-demucs.*.
                string? mix = FindBiggest(song.Location, "song.pre-demucs") ?? FindBiggest(song.Location, "song");
                if (mix == null)
                {
                    throw new StemException("No song audio in the folder.");
                }

                string copy = Path.Combine(workDir, "mix" + Path.GetExtension(mix));
                File.Copy(mix, copy, true);
                return copy;
            }
            case SongKind.Sng:
            {
                var archive = SngArchive.TryOpen(song.Location) ?? throw new StemException("Could not read the .sng file.");
                string name = archive.FindFullMix() ?? throw new StemException("No song audio in the .sng file.");
                string output = Path.Combine(workDir, "mix" + Path.GetExtension(name));
                archive.Extract(name, output);
                return output;
            }
            default:
                return MoggToOgg(ExportMogg(song, workDir), Path.Combine(workDir, "mix.ogg"));
        }
    }

    /// <summary>
    /// Exports the decrypted moggs of several songs from one CON in a single Nautilus run.
    /// Returns song → ogg path for the ones that worked.
    /// </summary>
    public Dictionary<Song, string> ExportConMixes(string con, IReadOnlyList<Song> songs, string workDir)
    {
        var nautilus = _nautilus ?? throw new StemException("Nautilus is needed to read CON files.");
        string outDir = Path.Combine(workDir, "moggs");
        nautilus.ExtractMoggs(con, songs.Select(s => s.MoggPath), outDir);
        var result = new Dictionary<Song, string>();
        int index = 0;
        foreach (var song in songs)
        {
            string mogg = Path.Combine(outDir, song.MoggPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(mogg))
            {
                continue;
            }

            try
            {
                result[song] = MoggToOgg(mogg, Path.Combine(workDir, $"mix{index++}.ogg"));
            }
            catch (StemException)
            {
                // Reported as "could not check" by the caller.
            }

            File.Delete(mogg);
        }

        return result;
    }

    private string ExportMogg(Song song, string workDir)
    {
        if (song.Kind == SongKind.Con)
        {
            var nautilus = _nautilus ?? throw new StemException("Nautilus is needed to read CON files.");
            string outDir = Path.Combine(workDir, "moggs");
            nautilus.ExtractMoggs(song.Location, new[] { song.MoggPath }, outDir);
            string mogg = Path.Combine(outDir, song.MoggPath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(mogg) ? mogg : throw new StemException("Nautilus could not extract the mogg.");
        }

        // Extracted Rock Band folder: decrypt with Nautilus only if needed.
        if (MoggVersion(song.MoggPath) == UnencryptedMogg)
        {
            return song.MoggPath;
        }

        var tool = _nautilus ?? throw new StemException("This mogg is encrypted; Nautilus is needed to decrypt it.");
        string decrypted = Path.Combine(workDir, "decrypted.mogg");
        tool.Decrypt(song.MoggPath, decrypted);
        return decrypted;
    }

    /// <summary>Drops the mogg header, leaving the Ogg Vorbis stream.</summary>
    private static string MoggToOgg(string mogg, string ogg)
    {
        using var input = File.OpenRead(mogg);
        using var reader = new BinaryReader(input);
        int version = reader.ReadInt32();
        if (version != UnencryptedMogg)
        {
            throw new StemException($"The mogg is still encrypted (version {version}).");
        }

        int start = reader.ReadInt32();
        if (start <= 8 || start >= input.Length)
        {
            throw new StemException("The mogg header is damaged.");
        }

        input.Position = start;
        using (var output = File.Create(ogg))
        {
            input.CopyTo(output);
        }

        return ogg;
    }

    private static int MoggVersion(string mogg)
    {
        using var input = File.OpenRead(mogg);
        using var reader = new BinaryReader(input);
        return reader.ReadInt32();
    }

    private static string? FindBiggest(string directory, string stemName)
    {
        string? best = null;
        long bestLength = StemLayout.PlaceholderMaxBytes;
        foreach (var format in StemLayout.AudioFormats)
        {
            var info = new FileInfo(Path.Combine(directory, stemName + format));
            if (info.Exists && info.Length > bestLength)
            {
                best = info.FullName;
                bestLength = info.Length;
            }
        }

        return best;
    }
}
