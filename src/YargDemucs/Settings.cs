using System.Text.Json;

namespace YargDemucs;

/// <summary>Tool settings in %LOCALAPPDATA%\YARG Demucs\settings.json.</summary>
public sealed class ToolSettings
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YARG Demucs");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public string? NautilusPath { get; set; }

    /// <summary>Name saved in contributions.json for the shared database.</summary>
    public string? Contributor { get; set; }

    /// <summary>The YARG profile picked last time (release, nightly, or dev).</summary>
    public string? LastProfile { get; set; }

    public static ToolSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<ToolSettings>(File.ReadAllText(FilePath)) ?? new ToolSettings();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Console.Error.WriteLine("Ignoring unreadable settings: " + ex.Message);
        }

        return new ToolSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>Reads the song folders from YARG's settings.json.</summary>
public static class YargSettings
{
    public static readonly string[] Profiles = { "release", "nightly", "dev" };

    public static string? LastSource { get; private set; }

    /// <summary>Unity's persistentDataPath for YARG on Windows; each build keeps a profile folder in it.</summary>
    public static string BaseDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "YARC", "YARG");

    /// <summary>
    /// Song folders of the chosen YARG profile. With several profiles and no --profile, asks which
    /// one (Enter keeps the last choice). Runs without a console use the last choice, or the
    /// profile used most recently.
    /// </summary>
    public static List<string> FindSongFolders(string? profile, string? dataPath, ToolSettings settings)
    {
        if (dataPath != null)
        {
            return ReadSongFolders(Path.Combine(dataPath, "settings.json"));
        }

        if (profile == null)
        {
            var available = Profiles
                .Where(p => File.Exists(SettingsFile(p)))
                .OrderByDescending(p => File.GetLastWriteTimeUtc(SettingsFile(p)))
                .ToList();
            if (available.Count == 0)
            {
                return new List<string>();
            }

            string fallback = settings.LastProfile != null && available.Contains(settings.LastProfile)
                ? settings.LastProfile
                : available[0];
            profile = available.Count > 1 && !Console.IsInputRedirected
                ? AskProfile(available, fallback)
                : fallback;
            if (settings.LastProfile != profile)
            {
                settings.LastProfile = profile;
                settings.Save();
            }
        }

        return ReadSongFolders(SettingsFile(profile));
    }

    private static string SettingsFile(string profile) => Path.Combine(BaseDir, profile, "settings.json");

    private static string AskProfile(List<string> available, string fallback)
    {
        Console.WriteLine("YARG profiles in " + BaseDir + ":");
        for (int i = 0; i < available.Count; i++)
        {
            string p = available[i];
            var folders = ReadSongFolders(SettingsFile(p), quiet: true);
            string used = File.GetLastWriteTime(SettingsFile(p)).ToString("yyyy-MM-dd HH:mm");
            string summary = folders.Count == 0 ? "no song folders" : string.Join(", ", folders);
            Console.WriteLine($"  {i + 1}. {p,-8} last used {used}  {summary}{(p == fallback ? "  (default)" : "")}");
        }

        while (true)
        {
            Console.Write($"Which profile? [1-{available.Count}, Enter = {fallback}]: ");
            string? answer = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(answer))
            {
                return fallback;
            }

            if (int.TryParse(answer, out int n) && n >= 1 && n <= available.Count)
            {
                return available[n - 1];
            }

            var named = available.FirstOrDefault(p => p.Equals(answer, StringComparison.OrdinalIgnoreCase));
            if (named != null)
            {
                return named;
            }
        }
    }

    private static List<string> ReadSongFolders(string file, bool quiet = false)
    {
        var folders = new List<string>();
        if (!File.Exists(file))
        {
            return folders;
        }

        if (!quiet)
        {
            LastSource = file;
        }
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            if (document.RootElement.TryGetProperty("SongFolders", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    string? value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        folders.Add(value);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Console.Error.WriteLine("Could not read " + file + ": " + ex.Message);
        }

        return folders;
    }
}

/// <summary>Keeps two copies of the tool from separating at the same time.</summary>
public sealed class RunLock : IDisposable
{
    private readonly FileStream _stream;

    private RunLock(FileStream stream)
    {
        _stream = stream;
    }

    public static RunLock? TryAcquire()
    {
        try
        {
            Directory.CreateDirectory(ToolSettings.Folder);
            var stream = new FileStream(Path.Combine(ToolSettings.Folder, "run.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new RunLock(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
