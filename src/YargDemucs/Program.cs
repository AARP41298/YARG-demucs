using System.Diagnostics;
using System.Text;
using YargDemucs;
using YargDemucs.Audio;
using YargDemucs.Library;
using YargDemucs.Stems;

Options options;
try
{
    options = Options.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Run with --help for the options.");
    return 2;
}

if (options.Help)
{
    Options.PrintUsage();
    return 0;
}

var settings = ToolSettings.Load();
if (options.NautilusPath != null)
{
    settings.NautilusPath = Path.GetFullPath(options.NautilusPath);
    settings.Save();
}

var roots = options.Folders.Count > 0 ? options.Folders : YargSettings.FindSongFolders(options.Profile, options.DataPath, settings);
if (options.Folders.Count == 0)
{
    Console.WriteLine("YARG settings: " + (YargSettings.LastSource ?? "not found"));
}

if (roots.Count == 0)
{
    Console.Error.WriteLine("No song folders. Add them in YARG, or pass --folder <path>.");
    return 2;
}

foreach (var root in roots)
{
    Console.WriteLine("Song folder:   " + root);
}

Nautilus? nautilus = null;
if (!string.IsNullOrWhiteSpace(settings.NautilusPath) && File.Exists(settings.NautilusPath))
{
    nautilus = new Nautilus(settings.NautilusPath);
    Console.WriteLine("Nautilus:      " + settings.NautilusPath);
}
else
{
    Console.WriteLine("Nautilus:      not set, CON files are skipped (pass --nautilus <Nautilus.exe> once).");
}

using var runLock = RunLock.TryAcquire();
if (runLock == null)
{
    Console.Error.WriteLine("YARG Demucs is already running.");
    return 3;
}

string workDir = Path.Combine(Path.GetTempPath(), "yarg-demucs", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workDir);
var runner = new ProcessRunner { Priority = options.Priority };
Console.CancelKeyPress += (_, e) =>
{
    if (runner.IsStopRequested)
    {
        // Second Ctrl+C: quit now.
        Console.WriteLine();
        Console.WriteLine("Exiting.");
        return;
    }

    e.Cancel = true;
    Console.WriteLine();
    Console.WriteLine("Stopping after the current step (press Ctrl+C again to quit now)...");
    runner.Stop();
};

try
{
    var cache = new ConListCache(Path.Combine(ToolSettings.Folder, "cache.json"));
    var watch = Stopwatch.StartNew();
    Console.WriteLine();
    Console.WriteLine("Scanning song folders...");
    var scanProgress = new ProgressLine();
    var scanner = new LibraryScanner(nautilus, workDir, cache)
    {
        IsStopRequested = () => runner.IsStopRequested,
        Progress = (found, cons, path) => scanProgress.Update(
            $"  {found} songs, {cons} CON files read - {Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))}"),
    };
    var songs = scanner.Scan(roots);
    scanProgress.Done();
    cache.Save();
    if (runner.IsStopRequested)
    {
        return 130;
    }

    foreach (var warning in scanner.Warnings.Take(20))
    {
        Console.WriteLine("  " + warning);
    }

    if (scanner.Warnings.Count > 20)
    {
        Console.WriteLine($"  ... and {scanner.Warnings.Count - 20} more");
    }

    Console.WriteLine($"Found {songs.Count} songs ({scanner.ConPackages} CON files) in {watch.Elapsed.TotalSeconds:0.0}s.");

    var separator = new Separator(runner)
    {
        Shifts = options.Shifts,
        Device = options.Device,
        Model = options.Model,
    };
    Console.WriteLine($"Demucs:        shifts {options.Shifts}, device {options.Device ?? "auto"}, model {options.Model ?? "default"}; " +
        $"output {options.Format}{(options.Format == "wav" ? "" : " " + options.Bitrate + " kbps")}");
    int official = songs.Count(s => s.IsOfficial);
    if (official > 0 && !options.CheckOfficial)
    {
        Console.WriteLine($"Skipping {official} official Rock Band / Guitar Hero songs (--check-official to include them).");
    }
    var audio = new SongAudio(nautilus);

    var toCheck = options.CheckOfficial ? songs : songs.Where(s => !s.IsOfficial).ToList();
    CheckSilence(toCheck, cache, audio, separator, runner, workDir, options.Recheck);
    if (runner.IsStopRequested)
    {
        return 130;
    }

    WriteMissingLists(songs, cache, options.CheckOfficial);

    var pending = FindPending(songs, cache, options);
    Console.WriteLine();
    Console.WriteLine($"{pending.Count} songs need stems.");
    if (options.ListOnly || options.CheckOnly)
    {
        if (options.ListOnly)
        {
            foreach (var song in pending)
            {
                Console.WriteLine($"  {song.Label}  ({song.Location})");
            }
        }

        return 0;
    }

    var installer = new StemInstaller(separator)
    {
        Extension = "." + options.Format,
        BitrateKbps = options.Bitrate,
        Generator = "yarg-demucs " + (options.Model ?? "htdemucs"),
    };
    return Generate(pending, audio, separator, installer, runner, workDir);
}
finally
{
    try
    {
        Directory.Delete(workDir, true);
    }
    catch (Exception)
    {
    }
}

static void CheckSilence(List<Song> songs, ConListCache cache, SongAudio audio, Separator separator,
    ProcessRunner runner, string workDir, bool recheck)
{
    var pending = songs.Where(s => s.NeedsSilenceCheck && !s.HasStems && (recheck || !cache.TryGetSilent(s, out _))).ToList();
    if (pending.Count == 0)
    {
        return;
    }

    Console.WriteLine($"Checking {pending.Count} Rock Band songs for silent stem channels (once; results are cached)...");
    var progress = new ProgressLine();
    var started = Stopwatch.StartNew();
    int done = 0;
    int sinceSave = 0;
    void Check(Song song, string? ogg)
    {
        done++;
        double rate = done / Math.Max(started.Elapsed.TotalSeconds, 0.001);
        var left = TimeSpan.FromSeconds((pending.Count - done) / Math.Max(rate, 0.001));
        progress.Update($"  {done}/{pending.Count}  {rate:0.0} songs/s, about {ProgressLine.Format(left)} left - {song.Label}");
        if (ogg == null)
        {
            return;
        }

        string probeDir = Path.Combine(workDir, "probe");
        Directory.CreateDirectory(probeDir);
        try
        {
            if (separator.TryCheckSilence(ogg, song.CoreChannels, probeDir, out bool silent))
            {
                cache.SetSilent(song, silent);
            }
            else
            {
                cache.SetSilent(song, false);
            }
        }
        catch (StemException ex)
        {
            progress.Done();
            Console.WriteLine("  " + song.Label + ": " + ex.Message);
        }
        finally
        {
            File.Delete(ogg);
        }

        if (++sinceSave >= 25)
        {
            sinceSave = 0;
            cache.Save();
        }
    }

    try
    {
        // CON packs: extract their moggs together, a few at a time to bound temp space.
        foreach (var pack in pending.Where(s => s.Kind == SongKind.Con).GroupBy(s => s.Location))
        {
            foreach (var batch in pack.Chunk(16))
            {
                if (runner.IsStopRequested)
                {
                    return;
                }

                string batchDir = Path.Combine(workDir, "batch");
                Directory.CreateDirectory(batchDir);
                Dictionary<Song, string> mixes;
                try
                {
                    mixes = audio.ExportConMixes(pack.Key, batch, batchDir);
                }
                catch (Exception ex) when (ex is NautilusException or StemException or IOException)
                {
                    progress.Done();
                    Console.WriteLine("  " + pack.Key + ": " + ex.Message);
                    mixes = new Dictionary<Song, string>();
                }

                foreach (var song in batch)
                {
                    Check(song, mixes.TryGetValue(song, out var ogg) ? ogg : null);
                }

                Directory.Delete(batchDir, true);
            }
        }

        foreach (var song in pending.Where(s => s.Kind == SongKind.RbFolder))
        {
            if (runner.IsStopRequested)
            {
                return;
            }

            string songDir = Path.Combine(workDir, "rb");
            Directory.CreateDirectory(songDir);
            string? ogg = null;
            try
            {
                ogg = audio.ExportMix(song, songDir);
            }
            catch (Exception ex) when (ex is NautilusException or StemException or IOException)
            {
                progress.Done();
                Console.WriteLine("  " + song.Label + ": " + ex.Message);
            }

            Check(song, ogg);
            Directory.Delete(songDir, true);
        }
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
        cache.Save();
        progress.Done();
    }
}

static void WriteMissingLists(List<Song> songs, ConListCache cache, bool checkOfficial)
{
    foreach (var group in songs.GroupBy(s => s.Root, StringComparer.OrdinalIgnoreCase))
    {
        var names = group
            .Where(s => (checkOfficial || !s.IsOfficial) && s.NeedsSilenceCheck && cache.TryGetSilent(s, out bool silent) && silent)
            .Select(s => s.StemId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        string path = Path.Combine(group.Key, StemLayout.FolderName, StemLayout.MissingListName);
        if (names.Count == 0 && !File.Exists(path))
        {
            continue;
        }

        StemLayout.EnsureStemRoot(group.Key);
        var text = new StringBuilder();
        text.AppendLine("# Rock Band songs whose drum, bass, and vocal channels are silent. Written by YARG Demucs.");
        foreach (var name in names)
        {
            text.AppendLine(name);
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }
}

static List<Song> FindPending(List<Song> songs, ConListCache cache, Options options)
{
    var pending = new List<Song>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var song in songs.OrderBy(s => s.Artist, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
    {
        bool checkedSilent = (options.CheckOfficial || !song.IsOfficial) &&
            song.NeedsSilenceCheck && cache.TryGetSilent(song, out bool silent) && silent;
        bool missing = song.ListsNoStems || checkedSilent;
        // Copies of a Rock Band song share one stem folder.
        if (!missing || song.HasStems || !seen.Add(song.StemDirectory))
        {
            continue;
        }

        if (options.Match != null &&
            !song.Label.Contains(options.Match, StringComparison.OrdinalIgnoreCase) &&
            !song.Location.Contains(options.Match, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        pending.Add(song);
        if (options.Limit > 0 && pending.Count >= options.Limit)
        {
            break;
        }
    }

    return pending;
}

static int Generate(List<Song> pending, SongAudio audio, Separator separator, StemInstaller installer,
    ProcessRunner runner, string workDir)
{
    int generated = 0;
    int failed = 0;
    var total = Stopwatch.StartNew();
    for (int i = 0; i < pending.Count && !runner.IsStopRequested; i++)
    {
        var song = pending[i];
        Console.Write($"[{i + 1}/{pending.Count}] {song.Label} ... ");
        var watch = Stopwatch.StartNew();
        string songDir = Path.Combine(workDir, "song" + i);
        Directory.CreateDirectory(songDir);
        try
        {
            string mix = audio.ExportMix(song, songDir);
            string stereo = Path.Combine(songDir, "stereo.wav");
            separator.MakeStereoMix(mix, stereo, song.SongChannels);
            string separated = separator.Separate(stereo, Path.Combine(songDir, "out"));
            installer.Install(song, separated, songDir);
            generated++;
            Console.WriteLine($"done ({watch.Elapsed:m\\:ss})");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("stopped");
        }
        catch (Exception ex) when (ex is StemException or NautilusException or IOException or UnauthorizedAccessException)
        {
            failed++;
            Console.WriteLine("FAILED: " + ex.Message);
        }
        finally
        {
            try
            {
                Directory.Delete(songDir, true);
            }
            catch (Exception)
            {
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Stems generated for {generated} songs, {failed} failed, in {total.Elapsed:h\\:mm\\:ss}.");
    if (generated > 0)
    {
        Console.WriteLine("YARG shows them the next time the music library opens.");
    }

    if (runner.IsStopRequested)
    {
        return 130;
    }

    return failed > 0 ? 1 : 0;
}
