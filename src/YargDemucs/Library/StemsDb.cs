using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YargDemucs.Library;

/// <summary>
/// Shared database of Rock Band songs already checked for silent stem channels, so each song is
/// listened to by one person instead of everyone. One JSON file per game_origin in stemsdb/:
///
///   { "source": "ugc_plus", "songs": [ { "con_id": "highwaytohell", "artist": "AC/DC",
///     "title": "Highway to Hell", "layout": "d0,1 b2,3 v6,7 s10,11", "stems": "silent",
///     "checked_by": "..." } ] }
///
/// A song is matched by source (game_origin), con_id, and layout (which mogg channels hold drums,
/// bass, vocals, and the song stem), so a custom and an official copy of the same song, or a
/// re-release with different audio, are kept apart.
/// </summary>
public sealed class StemsDb
{
    public const string FolderName = "stemsdb";
    public const string Silent = "silent";
    public const string Present = "present";

    public sealed class Entry
    {
        [JsonPropertyName("con_id")] public string ConId { get; set; } = string.Empty;
        [JsonPropertyName("artist")] public string Artist { get; set; } = string.Empty;
        [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("layout")] public string Layout { get; set; } = string.Empty;
        [JsonPropertyName("stems")] public string Stems { get; set; } = string.Empty;
        [JsonPropertyName("checked_by")] public string CheckedBy { get; set; } = string.Empty;

        [JsonIgnore] public string Source { get; set; } = string.Empty;
        [JsonIgnore] public string Key => KeyOf(Source, ConId, Layout);
        [JsonIgnore] public bool IsSilent => Stems == Silent;
    }

    private sealed class SourceFile
    {
        [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
        [JsonPropertyName("songs")] public List<Entry> Songs { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public string? Directory { get; private set; }
    public int Count => _entries.Count;
    public IEnumerable<Entry> Entries => _entries.Values;

    public static string KeyOf(string source, string conId, string layout) =>
        SourceOf(source) + "|" + conId.Trim().ToLowerInvariant() + "|" + layout;

    private static string SourceOf(string source) => string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim().ToLowerInvariant();

    public static string LayoutOf(Song song)
    {
        static string Join(int[] channels) => string.Join(",", channels);
        return $"d{Join(song.Drums)} b{Join(song.Bass)} v{Join(song.Vocals)} s{Join(song.SongChannels)}";
    }

    public static Entry FromSong(Song song, bool silent, string checkedBy) => new()
    {
        ConId = song.StemId,
        Artist = song.Artist,
        Title = song.Name,
        Layout = LayoutOf(song),
        Stems = silent ? Silent : Present,
        CheckedBy = checkedBy,
        Source = SourceOf(song.Source),
    };

    public bool TryGet(Song song, out bool silent)
    {
        if (_entries.TryGetValue(KeyOf(song.Source, song.StemId, LayoutOf(song)), out var entry))
        {
            silent = entry.IsSilent;
            return true;
        }

        silent = false;
        return false;
    }

    public bool TryGet(string key, out Entry entry) => _entries.TryGetValue(key, out entry!);

    public void Set(Entry entry) => _entries[entry.Key] = entry;

    /// <summary>The stemsdb folder next to the exe (copied on build), or null.</summary>
    public static string? FindNextToExe()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, FolderName);
        return System.IO.Directory.Exists(dir) ? dir : null;
    }

    /// <summary>The stemsdb folder of the repository the exe was built from (for --merge).</summary>
    public static string? FindInRepository()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "YargDemucs.slnx")))
                {
                    return Path.Combine(dir.FullName, FolderName);
                }
            }
        }

        return null;
    }

    public static StemsDb Load(string? directory)
    {
        var db = new StemsDb { Directory = directory };
        if (directory == null || !System.IO.Directory.Exists(directory))
        {
            return db;
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.json"))
        {
            foreach (var entry in ReadFile(file))
            {
                db._entries[entry.Key] = entry;
            }
        }

        return db;
    }

    /// <summary>Reads a stemsdb source file or a contributions file (same format, any source).</summary>
    public static List<Entry> ReadFile(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text);
            var list = new List<Entry>();
            // A contributions file holds several sources: { "files": [ {source, songs}, ... ] }.
            var files = document.RootElement.TryGetProperty("files", out var many)
                ? many.Deserialize<List<SourceFile>>(Json) ?? new()
                : new List<SourceFile> { document.RootElement.Deserialize<SourceFile>(Json) ?? new() };
            foreach (var file in files)
            {
                foreach (var entry in file.Songs)
                {
                    entry.Source = file.Source;
                    if (!string.IsNullOrWhiteSpace(entry.ConId) &&
                        (entry.Stems == Silent || entry.Stems == Present))
                    {
                        list.Add(entry);
                    }
                }
            }

            return list;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Console.Error.WriteLine("Could not read " + path + ": " + ex.Message);
            return new List<Entry>();
        }
    }

    /// <summary>Writes one file per source, songs sorted by artist and title.</summary>
    public void Save(string directory, IEnumerable<string>? onlySources = null)
    {
        System.IO.Directory.CreateDirectory(directory);
        var sources = onlySources?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in _entries.Values.GroupBy(e => e.Source, StringComparer.OrdinalIgnoreCase))
        {
            if (sources != null && !sources.Contains(group.Key))
            {
                continue;
            }

            var file = new SourceFile { Source = group.Key, Songs = Sorted(group) };
            File.WriteAllText(Path.Combine(directory, SafeName(group.Key) + ".json"),
                JsonSerializer.Serialize(file, Json) + Environment.NewLine);
        }
    }

    /// <summary>Writes entries as a contributions file: { "files": [ {source, songs}, ... ] }.</summary>
    public static void WriteContributions(string path, IEnumerable<Entry> entries)
    {
        var files = entries
            .GroupBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SourceFile { Source = g.Key, Songs = Sorted(g) })
            .ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(new { files }, Json) + Environment.NewLine);
    }

    private static List<Entry> Sorted(IEnumerable<Entry> entries) => entries
        .OrderBy(e => e.Artist, StringComparer.OrdinalIgnoreCase)
        .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
        .ThenBy(e => e.ConId, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string SafeName(string source)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(source.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return name.Length == 0 ? "unknown" : name;
    }
}
