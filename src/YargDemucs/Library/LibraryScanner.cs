namespace YargDemucs.Library;

/// <summary>
/// Finds songs under the song folders YARG uses: unpacked song folders, .sng files,
/// CON/LIVE packages (read with Nautilus), and extracted Rock Band folders.
/// </summary>
public sealed class LibraryScanner
{
    private static readonly string[] ChartFiles = { "notes.chart", "notes.mid", "notes.midi", "song.ini" };
    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        StemLayout.FolderName, "songs_updates", "songs_upgrades",
    };

    private readonly Nautilus? _nautilus;
    private readonly string _workDir;
    private readonly ConListCache _conCache;

    public List<string> Warnings { get; } = new();
    public int ConPackages { get; private set; }

    /// <summary>Checked between folders and packages so Ctrl+C stops the scan.</summary>
    public Func<bool> IsStopRequested { get; set; } = () => false;

    /// <summary>Called as songs are found: (songs so far, CON files so far, folder being read).</summary>
    public Action<int, int, string>? Progress { get; set; }

    public LibraryScanner(Nautilus? nautilus, string workDir, ConListCache conCache)
    {
        _nautilus = nautilus;
        _workDir = workDir;
        _conCache = conCache;
    }

    public List<Song> Scan(IEnumerable<string> roots)
    {
        var songs = new List<Song>();
        foreach (var rootPath in roots)
        {
            string root = Path.GetFullPath(rootPath).TrimEnd('\\', '/');
            if (!Directory.Exists(root))
            {
                Warnings.Add("Song folder not found: " + root);
                continue;
            }

            ScanDirectory(new DirectoryInfo(root), root, songs);
        }

        return songs;
    }

    private void ScanDirectory(DirectoryInfo directory, string root, List<Song> songs)
    {
        if (IsStopRequested())
        {
            return;
        }

        Progress?.Invoke(songs.Count, ConPackages, directory.FullName);
        FileInfo[] files;
        DirectoryInfo[] children;
        try
        {
            files = directory.GetFiles();
            children = directory.GetDirectories();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warnings.Add("Could not read " + directory.FullName + ": " + ex.Message);
            return;
        }

        // Extracted Rock Band folder: <dir>/songs.dta inside a folder named "songs".
        if (directory.Name.Equals("songs", StringComparison.OrdinalIgnoreCase) &&
            files.Any(f => f.Name.Equals("songs.dta", StringComparison.OrdinalIgnoreCase)))
        {
            AddRbFolder(directory, root, songs);
            return;
        }

        bool ignored = files.Any(f => f.Name.Equals(".yargignore", StringComparison.OrdinalIgnoreCase));
        if (!ignored)
        {
            if (files.Any(f => ChartFiles.Contains(f.Name, StringComparer.OrdinalIgnoreCase)))
            {
                AddFolder(directory, root, files, songs);
                return;
            }

            foreach (var file in files)
            {
                if (IsStopRequested())
                {
                    return;
                }

                if (file.Extension.Equals(".sng", StringComparison.OrdinalIgnoreCase))
                {
                    AddSng(file, root, songs);
                }
                else if (IsConPackage(file))
                {
                    AddCon(file, root, songs);
                    Progress?.Invoke(songs.Count, ConPackages, file.FullName);
                }
            }
        }

        foreach (var child in children)
        {
            if (SkipDirectories.Contains(child.Name) || child.Attributes.HasFlag(FileAttributes.Hidden))
            {
                continue;
            }

            ScanDirectory(child, root, songs);
        }
    }

    private static void AddFolder(DirectoryInfo directory, string root, FileInfo[] files, List<Song> songs)
    {
        bool hasCore = false;
        bool hasMix = false;
        foreach (var file in files)
        {
            if (file.Length <= StemLayout.PlaceholderMaxBytes || !StemLayout.IsAudio(file.Name))
            {
                continue;
            }

            string stem = Path.GetFileNameWithoutExtension(file.Name);
            hasCore |= StemLayout.IsCoreStem(stem);
            hasMix |= stem.Equals("song", StringComparison.OrdinalIgnoreCase);
        }

        var (artist, name) = ReadIni(Path.Combine(directory.FullName, "song.ini"), directory.Name);
        songs.Add(new Song
        {
            Kind = SongKind.Folder,
            Root = root,
            Location = directory.FullName,
            Artist = artist,
            Name = name,
            ListsNoStems = !hasCore && hasMix,
        });
    }

    private static void AddSng(FileInfo file, string root, List<Song> songs)
    {
        var archive = SngArchive.TryOpen(file.FullName);
        if (archive == null)
        {
            return;
        }

        songs.Add(new Song
        {
            Kind = SongKind.Sng,
            Root = root,
            Location = file.FullName,
            Name = Path.GetFileNameWithoutExtension(file.Name),
            StemId = StemLayout.SngId(root, file.FullName),
            ListsNoStems = archive.MissesCoreStems() && archive.FindFullMix() != null,
        });
    }

    private void AddCon(FileInfo file, string root, List<Song> songs)
    {
        if (_nautilus == null)
        {
            Warnings.Add("Skipped CON (Nautilus not set): " + file.FullName);
            return;
        }

        ConPackages++;
        List<Dta.RbSong> rbSongs;
        try
        {
            rbSongs = _conCache.GetOrRead(file, () =>
            {
                string work = Path.Combine(_workDir, "dta-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                try
                {
                    return Dta.ReadSongs(_nautilus.ReadDta(file.FullName, work));
                }
                finally
                {
                    Directory.Delete(work, true);
                }
            });
        }
        catch (Exception ex)
        {
            Warnings.Add("Could not read CON " + file.FullName + ": " + ex.Message);
            return;
        }

        foreach (var rb in rbSongs)
        {
            songs.Add(new Song
            {
                Kind = SongKind.Con,
                Root = root,
                Location = file.FullName,
                Artist = rb.Artist,
                Name = rb.Name,
                StemId = rb.ShortName,
                MoggPath = rb.Location + ".mogg",
                Drums = rb.Drums,
                Bass = rb.Bass,
                Vocals = rb.Vocals,
                Source = rb.Origins.FirstOrDefault() ?? string.Empty,
                SongChannels = rb.SongChannels,
                IsOfficial = rb.IsOfficial,
                ListsNoStems = rb.ListsNoStems,
            });
        }
    }

    private void AddRbFolder(DirectoryInfo songsDir, string root, List<Song> songs)
    {
        List<Dta.RbSong> rbSongs;
        try
        {
            rbSongs = Dta.ReadSongs(File.ReadAllBytes(Path.Combine(songsDir.FullName, "songs.dta")));
        }
        catch (Exception ex)
        {
            Warnings.Add("Could not read " + songsDir.FullName + ": " + ex.Message);
            return;
        }

        foreach (var rb in rbSongs)
        {
            // YARG reads songs/<short>/<short>.mogg next to songs.dta.
            string mogg = Path.Combine(songsDir.FullName, rb.ShortName, rb.ShortName + ".mogg");
            if (!File.Exists(mogg))
            {
                continue;
            }

            songs.Add(new Song
            {
                Kind = SongKind.RbFolder,
                Root = root,
                Location = songsDir.FullName,
                Artist = rb.Artist,
                Name = rb.Name,
                StemId = rb.ShortName,
                MoggPath = mogg,
                Drums = rb.Drums,
                Bass = rb.Bass,
                Vocals = rb.Vocals,
                Source = rb.Origins.FirstOrDefault() ?? string.Empty,
                SongChannels = rb.SongChannels,
                IsOfficial = rb.IsOfficial,
                ListsNoStems = rb.ListsNoStems,
            });
        }
    }

    private static bool IsConPackage(FileInfo file)
    {
        if (file.Length < 0x1000 || StemLayout.IsAudio(file.Name))
        {
            return false;
        }

        try
        {
            using var stream = file.OpenRead();
            Span<byte> magic = stackalloc byte[4];
            if (stream.Read(magic) < 4)
            {
                return false;
            }

            return magic.SequenceEqual("CON "u8) || magic.SequenceEqual("LIVE"u8) || magic.SequenceEqual("PIRS"u8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static (string Artist, string Name) ReadIni(string path, string fallbackName)
    {
        string artist = string.Empty;
        string name = fallbackName;
        if (!File.Exists(path))
        {
            return (artist, name);
        }

        try
        {
            foreach (var raw in File.ReadLines(path))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = raw[..eq].Trim();
                string value = raw[(eq + 1)..].Trim();
                if (key.Equals("artist", StringComparison.OrdinalIgnoreCase))
                {
                    artist = value;
                }
                else if (key.Equals("name", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                {
                    name = value;
                }
            }
        }
        catch (IOException)
        {
        }

        return (artist, name);
    }
}
