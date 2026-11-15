using System.Text.Json;

namespace YargDemucs.Library;

/// <summary>
/// Remembers what is in each CON (keyed by path, size, and date) and which Rock Band songs had
/// silent stem channels, so a library of big packs is only read once.
/// Stored in %LOCALAPPDATA%\YARG Demucs\cache.json.
/// </summary>
public sealed class ConListCache
{
    /// <summary>
    /// Version 3 listens at every point before calling a song silent and reads 3-8 channel moggs
    /// in file order. Results from older versions are checked again.
    /// </summary>
    private const int SilenceCheckVersion = 3;

    /// <summary>Version 2 stores game_origin, so older package lists are read again.</summary>
    private const int PackagesVersion = 2;

    private sealed class Data
    {
        public int SilenceVersion { get; set; }
        public int PackageVersion { get; set; }
        public Dictionary<string, List<Dta.RbSong>> Packages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> SilentStems { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private readonly string _path;
    private readonly object _gate = new();
    private Data _data = new() { SilenceVersion = SilenceCheckVersion, PackageVersion = PackagesVersion };

    public ConListCache(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<Data>(File.ReadAllText(path), Json);
                if (loaded != null)
                {
                    var silent = new Dictionary<string, bool>(loaded.SilentStems, StringComparer.OrdinalIgnoreCase);
                    if (loaded.SilenceVersion < SilenceCheckVersion)
                    {
                        silent.Clear();
                    }

                    _data = new Data
                    {
                        SilenceVersion = SilenceCheckVersion,
                        PackageVersion = PackagesVersion,
                        Packages = loaded.PackageVersion < PackagesVersion
                            ? new(StringComparer.OrdinalIgnoreCase)
                            : new(loaded.Packages, StringComparer.OrdinalIgnoreCase),
                        SilentStems = silent,
                    };
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Console.Error.WriteLine("Ignoring an unreadable cache (" + ex.Message + "): " + path);
        }
    }

    public List<Dta.RbSong> GetOrRead(FileInfo con, Func<List<Dta.RbSong>> read)
    {
        string key = $"{con.FullName}|{con.Length}|{con.LastWriteTimeUtc.Ticks}";
        lock (_gate)
        {
            if (_data.Packages.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var songs = read();
        lock (_gate)
        {
            // Drop entries for older versions of the same file.
            foreach (var old in _data.Packages.Keys.Where(k => k.StartsWith(con.FullName + "|", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _data.Packages.Remove(old);
            }

            _data.Packages[key] = songs;
        }

        return songs;
    }

    public bool TryGetSilent(Song song, out bool silent)
    {
        lock (_gate)
        {
            return _data.SilentStems.TryGetValue(song.CacheKey, out silent);
        }
    }

    public void SetSilent(Song song, bool silent)
    {
        lock (_gate)
        {
            _data.SilentStems[song.CacheKey] = silent;
        }
    }

    public void Save()
    {
        try
        {
            string text;
            lock (_gate)
            {
                text = JsonSerializer.Serialize(_data, Json);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, _path, true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine("Could not save the cache: " + ex.Message);
        }
    }
}
