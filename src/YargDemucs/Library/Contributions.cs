namespace YargDemucs.Library;

/// <summary>
/// Results checked on this PC that the shared database doesn't have yet are written to
/// %LOCALAPPDATA%\YARG Demucs\contributions.json, to be submitted and merged into stemsdb/.
/// </summary>
public static class Contributions
{
    public static string FilePath => Path.Combine(ToolSettings.Folder, "contributions.json");

    public static void Write(List<Song> songs, ConListCache cache, string? contributor)
    {
        var shared = cache.Shared;
        var entries = new Dictionary<string, StemsDb.Entry>(StringComparer.OrdinalIgnoreCase);
        int conflicts = 0;
        foreach (var song in songs)
        {
            if (!song.NeedsSilenceCheck || !cache.TryGetLocal(song, out bool silent))
            {
                continue;
            }

            var entry = StemsDb.FromSong(song, silent, contributor ?? string.Empty);
            if (shared != null && shared.TryGet(entry.Key, out var known))
            {
                if (known.IsSilent == silent)
                {
                    continue;
                }

                conflicts++;
            }

            entries[entry.Key] = entry;
        }

        if (entries.Count == 0)
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }

            return;
        }

        StemsDb.WriteContributions(FilePath, entries.Values);
        Console.WriteLine();
        Console.WriteLine($"{entries.Count} songs checked here are not in the shared database yet" +
            (conflicts > 0 ? $" ({conflicts} disagree with it)" : "") + ":");
        Console.WriteLine("  " + FilePath);
        Console.WriteLine("  Send it (an issue or pull request on the YARG Demucs repo) so others don't have to check them." +
            (string.IsNullOrWhiteSpace(contributor) ? " Add --contributor <name> to be credited." : ""));
    }

    /// <summary>
    /// Merges contributions files into the repository's stemsdb/. Prints for each song:
    ///   + new   ~ changed source/names   = same   ! conflict (kept as is)
    /// Without write it only reports.
    /// </summary>
    public static int Merge(List<string> files, bool write)
    {
        string? directory = StemsDb.FindInRepository();
        if (directory == null)
        {
            Console.Error.WriteLine("Run --merge from inside the YARG Demucs repository (stemsdb/ not found).");
            return 2;
        }

        var db = StemsDb.Load(directory);
        int added = 0, same = 0, conflict = 0;
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            foreach (var entry in StemsDb.ReadFile(file))
            {
                string label = $"{entry.Artist} - {entry.Title} ({entry.ConId}, {entry.Source})";
                if (db.TryGet(entry.Key, out var known))
                {
                    if (known.IsSilent == entry.IsSilent)
                    {
                        same++;
                        continue;
                    }

                    conflict++;
                    Console.WriteLine($"! {label}: database says {known.Stems} ({known.CheckedBy}), file says {entry.Stems} ({entry.CheckedBy}). Kept; fix by hand if the file is right.");
                    continue;
                }

                added++;
                Console.WriteLine($"+ {label}: {entry.Stems}");
                db.Set(entry);
                touched.Add(entry.Source);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{added} new, {same} already known, {conflict} conflicts.");
        if (!write)
        {
            Console.WriteLine("Dry run: add --write to save.");
            return 0;
        }

        if (touched.Count > 0)
        {
            db.Save(directory, touched);
            Console.WriteLine("Saved " + string.Join(", ", touched.Select(s => s + ".json")) + " in " + directory);
        }

        return 0;
    }
}
