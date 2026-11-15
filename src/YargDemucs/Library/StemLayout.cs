using System.Text;

namespace YargDemucs.Library;

/// <summary>
/// Where stems are written so YARG finds them. Must match YARG.Core's CustomStems:
///   {root}/demucs/.yarg-stems             the song scan skips this folder
///   {root}/demucs/{id}/.yarg-stems        song, vocals, bass, drums audio for one song
///   {root}/demucs/missing-stems.txt       Rock Band short names whose stem channels are silent
/// Unpacked song folders get their stems in place, with .yarg-stems in the song folder.
/// </summary>
public static class StemLayout
{
    public const string FolderName = "demucs";
    public const string MarkerName = ".yarg-stems";
    public const string MissingListName = "missing-stems.txt";
    public const long PlaceholderMaxBytes = 4096;

    /// <summary>Audio formats YARG plays, in the order it prefers them.</summary>
    public static readonly string[] AudioFormats = { ".opus", ".ogg", ".mp3", ".wav", ".aiff" };

    private static readonly string[] MarkerNames = { MarkerName, ".yarg-demucs" };

    public static bool HasMarker(string directory)
    {
        foreach (var name in MarkerNames)
        {
            if (File.Exists(Path.Combine(directory, name)))
            {
                return true;
            }
        }

        return false;
    }

    public static void WriteMarker(string directory, string generator)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, MarkerName), generator + Environment.NewLine);
    }

    /// <summary>Creates {root}/demucs with its marker so YARG's song scan skips it.</summary>
    public static string EnsureStemRoot(string root)
    {
        string directory = Path.Combine(root, FolderName);
        if (!HasMarker(directory))
        {
            WriteMarker(directory, "yarg-demucs");
        }

        return directory;
    }

    /// <summary>"sng_" plus the .sng path inside the root, with folder separators as "__".</summary>
    public static string SngId(string root, string sngPath)
    {
        string relative = Path.GetRelativePath(root, Path.GetFullPath(sngPath));
        return "sng_" + relative.Replace(Path.DirectorySeparatorChar.ToString(), "__")
            .Replace(Path.AltDirectorySeparatorChar.ToString(), "__");
    }

    public static string SanitizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "song";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(id.Length);
        foreach (char c in id)
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        string name = builder.ToString().Trim().Trim('.');
        return name.Length == 0 ? "song" : name;
    }

    public static bool IsAudio(string fileName)
    {
        foreach (var format in AudioFormats)
        {
            if (fileName.EndsWith(format, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the name is a vocals, bass, or drums stem, as YARG counts them.</summary>
    public static bool IsCoreStem(string stemName)
    {
        return stemName.Equals("vocal", StringComparison.OrdinalIgnoreCase)
            || stemName.Equals("vocals", StringComparison.OrdinalIgnoreCase)
            || stemName.StartsWith("vocals_", StringComparison.OrdinalIgnoreCase)
            || stemName.Equals("bass", StringComparison.OrdinalIgnoreCase)
            || stemName.Equals("drums", StringComparison.OrdinalIgnoreCase)
            || stemName.StartsWith("drums_", StringComparison.OrdinalIgnoreCase);
    }
}
