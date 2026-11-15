using System.Text.Json;

namespace YargDemucs;

/// <summary>Tool settings in %LOCALAPPDATA%\YARG Demucs\settings.json.</summary>
public sealed class ToolSettings
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YARG Demucs");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public string? NautilusPath { get; set; }

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

    public static List<string> FindSongFolders(string? profile, string? dataPath)
    {
        string? folder = dataPath;
        if (folder == null)
        {
            // Unity's persistentDataPath on Windows.
            string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "YARC", "YARG");
            if (profile != null)
            {
                folder = Path.Combine(baseDir, profile);
            }
            else
            {
                // The profile whose settings changed last is the YARG build in use.
                folder = Profiles
                    .Select(p => Path.Combine(baseDir, p))
                    .Where(p => File.Exists(Path.Combine(p, "settings.json")))
                    .OrderByDescending(p => File.GetLastWriteTimeUtc(Path.Combine(p, "settings.json")))
                    .FirstOrDefault();
            }
        }

        var folders = new List<string>();
        string? file = folder == null ? null : Path.Combine(folder, "settings.json");
        if (file == null || !File.Exists(file))
        {
            return folders;
        }

        LastSource = file;
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
