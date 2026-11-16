using YargDemucs.Audio;
using YargDemucs.Library;

namespace YargDemucs.Stems;

/// <summary>
/// Writes separated stems where YARG reads them. Demucs "other" becomes the song stem,
/// which is the backing left after vocals, bass, and drums are removed.
/// </summary>
public sealed class StemInstaller
{
    private static readonly string[] RetiredStems =
    {
        "song", "vocals", "vocal", "vocals_1", "vocals_2", "bass", "drums", "drums_1", "drums_2", "drums_3", "drums_4",
    };

    private static readonly string[] RetiredPlaceholders = { "guitar", "keys", "rhythm" };

    private readonly Separator _separator;

    public StemInstaller(Separator separator)
    {
        _separator = separator;
    }

    public string Extension { get; set; } = ".wav";
    public int BitrateKbps { get; set; } = 160;
    public int Shifts { get; set; } = 1;
    public string Model { get; set; } = "htdemucs";

    public void Install(Song song, string separatedDir, string workDir)
    {
        // Encode first, so a failure leaves the song untouched.
        string encoded = Path.Combine(workDir, "encoded");
        Directory.CreateDirectory(encoded);
        var sources = new (string Demucs, string Stem)[]
        {
            ("other", "song"), ("vocals", "vocals"), ("bass", "bass"), ("drums", "drums"),
        };
        foreach (var (demucs, stem) in sources)
        {
            _separator.Encode(Path.Combine(separatedDir, demucs + ".wav"), Path.Combine(encoded, stem + Extension), BitrateKbps);
        }

        string target = song.StemDirectory;
        Directory.CreateDirectory(target);
        if (song.Kind == SongKind.Folder)
        {
            RetireFolderAudio(target);
        }
        else
        {
            StemLayout.EnsureStemRoot(song.Root);
            foreach (var file in Directory.EnumerateFiles(target))
            {
                if (StemLayout.IsAudio(file))
                {
                    File.Delete(file);
                }
            }
        }

        foreach (var (_, stem) in sources)
        {
            File.Copy(Path.Combine(encoded, stem + Extension), Path.Combine(target, stem + Extension), true);
        }

        // Magma projects read vocal.* instead of vocals.*.
        if (song.Kind == SongKind.Folder && File.Exists(Path.Combine(target, "magma.rbproj")))
        {
            File.Copy(Path.Combine(encoded, "vocals" + Extension), Path.Combine(target, "vocal" + Extension), true);
        }

        // YARG only checks that the marker exists; the details are for people.
        string format = Extension.TrimStart('.') + (Extension == ".wav" ? "" : $" {BitrateKbps} kbps");
        StemLayout.WriteMarker(target, string.Join(Environment.NewLine,
            "generator=yarg-demucs " + typeof(StemInstaller).Assembly.GetName().Version?.ToString(3),
            "model=" + Model,
            "shifts=" + Shifts,
            "format=" + format,
            "date=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            "source=" + song.Location + (song.Kind is SongKind.Con or SongKind.RbFolder ? " (" + song.StemId + ")" : "")));
    }

    /// <summary>
    /// Moves the folder's original song and stem audio to *.pre-demucs.* so YARG only loads the
    /// new stems. Small placeholder guitar/keys/rhythm files are moved too.
    /// </summary>
    private static void RetireFolderAudio(string directory)
    {
        foreach (var stem in RetiredStems)
        {
            Retire(directory, stem, onlyPlaceholders: false);
        }

        foreach (var stem in RetiredPlaceholders)
        {
            Retire(directory, stem, onlyPlaceholders: true);
        }
    }

    private static void Retire(string directory, string stem, bool onlyPlaceholders)
    {
        foreach (var format in StemLayout.AudioFormats)
        {
            var info = new FileInfo(Path.Combine(directory, stem + format));
            if (!info.Exists || (onlyPlaceholders && info.Length > StemLayout.PlaceholderMaxBytes))
            {
                continue;
            }

            string backup = Path.Combine(directory, stem + ".pre-demucs" + format);
            if (File.Exists(backup))
            {
                info.Delete();
            }
            else
            {
                info.MoveTo(backup);
            }
        }
    }
}
