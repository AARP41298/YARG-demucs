using System.Diagnostics;

namespace YargDemucs;

public sealed class Options
{
    public bool Help;
    public bool ListOnly;
    public bool CheckOnly;
    public bool Recheck;
    public bool CheckOfficial;
    public int Jobs = 4;
    public int Limit;
    public string? Match;
    public int Shifts = 1;
    public string? Device;
    public string? Model;
    public string Format = "opus";
    public int Bitrate = 160;
    public ProcessPriorityClass Priority = ProcessPriorityClass.BelowNormal;
    public readonly List<string> Folders = new();
    public string? Profile;
    public string? DataPath;
    public string? NautilusPath;
    public string? Contributor;
    public readonly List<string> MergeFiles = new();
    public bool Write;

    private static readonly string[] Formats = { "opus", "ogg", "mp3", "wav" };

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string Next()
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException(arg + " needs a value.");
                }

                return args[++i];
            }

            int NextInt(int min, int max)
            {
                string value = Next();
                if (!int.TryParse(value, out int number) || number < min || number > max)
                {
                    throw new ArgumentException($"{arg} must be a number from {min} to {max}.");
                }

                return number;
            }

            switch (arg.ToLowerInvariant())
            {
                case "-h":
                case "--help":
                case "/?":
                    options.Help = true;
                    break;
                case "--list":
                    options.ListOnly = true;
                    break;
                case "--check-only":
                    options.CheckOnly = true;
                    break;
                case "--recheck":
                    options.Recheck = true;
                    break;
                case "--jobs":
                    options.Jobs = NextInt(1, 16);
                    break;
                case "--check-official":
                    options.CheckOfficial = true;
                    break;
                case "--limit":
                    options.Limit = NextInt(1, int.MaxValue);
                    break;
                case "--match":
                    options.Match = Next();
                    break;
                case "--shifts":
                    options.Shifts = NextInt(1, 8);
                    break;
                case "--device":
                    options.Device = Next();
                    break;
                case "--model":
                    options.Model = Next();
                    break;
                case "--format":
                    options.Format = Next().ToLowerInvariant().TrimStart('.');
                    if (!Formats.Contains(options.Format))
                    {
                        throw new ArgumentException("--format must be opus, ogg, mp3, or wav.");
                    }
                    break;
                case "--bitrate":
                    options.Bitrate = NextInt(64, 320);
                    break;
                case "--priority":
                    options.Priority = Next().ToLowerInvariant() switch
                    {
                        "idle" or "low" => ProcessPriorityClass.Idle,
                        "belownormal" => ProcessPriorityClass.BelowNormal,
                        "normal" => ProcessPriorityClass.Normal,
                        _ => throw new ArgumentException("--priority must be idle, belownormal, or normal."),
                    };
                    break;
                case "--folder":
                    options.Folders.Add(Path.GetFullPath(Next()));
                    break;
                case "--profile":
                    options.Profile = Next().ToLowerInvariant();
                    if (!YargSettings.Profiles.Contains(options.Profile))
                    {
                        throw new ArgumentException("--profile must be release, nightly, or dev.");
                    }
                    break;
                case "--data":
                    options.DataPath = Next();
                    break;
                case "--contributor":
                    options.Contributor = Next().Trim();
                    break;
                case "--merge":
                    options.MergeFiles.Add(Next());
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        options.MergeFiles.Add(args[++i]);
                    }
                    break;
                case "--write":
                    options.Write = true;
                    break;
                case "--nautilus":
                    options.NautilusPath = Next();
                    if (!File.Exists(options.NautilusPath))
                    {
                        throw new ArgumentException("Nautilus.exe not found: " + options.NautilusPath);
                    }
                    break;
                default:
                    throw new ArgumentException("Unknown option: " + arg);
            }
        }

        return options;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            YARG Demucs - generate stems for YARG songs that only have a full mix

            Usage: yarg-demucs [options]

            What to do
              --list               Only list the songs that need stems.
              --check-only         Only check Rock Band songs for silent stems and update
                                   missing-stems.txt (YARG's "no stems" tag).
              --recheck            Check Rock Band songs again even if they were checked before.
              --jobs <n>           CON files checked at the same time. Default 4; on an SSD more can
                                   help, on a slow HDD fewer.
              --check-official     Also check songs from official Rock Band / Guitar Hero games and
                                   DLC (game_origin rb1, rb3_dlc, gh5, beatles, ...). They ship with
                                   stems, so they are skipped by default.
              --limit <n>          Generate stems for at most n songs.
              --match <text>       Only songs whose artist, title, or path contains this text.

            Demucs
              --shifts <1-8>       Demucs --shifts: separates the song n times, each shifted by a
                                   random fraction of a second, and averages the results. Slightly
                                   cleaner stems; takes n times as long. Default 1.
              --device <cuda|cpu>  Default: Demucs chooses.
              --model <name>       Demucs model, such as htdemucs_ft. Default: Demucs's default.
              --priority <level>   idle, belownormal (default), or normal.

            Output
              --format <fmt>       opus (default), ogg, mp3, or wav.
              --bitrate <kbps>     For opus/ogg/mp3. Default 160.

            Library
              --folder <path>      Song folder (repeatable). Default: the song folders set in YARG.
              --profile <name>     YARG profile: release, nightly, or dev. Default: asks when there are
                                   several (Enter keeps the last choice).
              --data <path>        YARG data folder, if it is somewhere else.
              --nautilus <exe>     Nautilus.exe with the -backstagehero CLI. Remembered for next time.

            Shared database (stemsdb/)
              --contributor <name> Name saved in contributions.json. Remembered for next time.
              --merge <files...>   Merge contributions files into the repository's stemsdb/.
                                   Reports only; add --write to save.

            Press Ctrl+C to stop. Songs already finished are kept.
            Requires ffmpeg and demucs on PATH.
            """);
    }
}
