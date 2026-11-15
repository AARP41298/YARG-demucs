namespace YargDemucs.Library;

public enum SongKind
{
    /// <summary>Unpacked song folder (song.ini / notes.chart / notes.mid with loose audio).</summary>
    Folder,
    /// <summary>.sng archive.</summary>
    Sng,
    /// <summary>Song inside a packed CON/LIVE file.</summary>
    Con,
    /// <summary>Song in an extracted Rock Band folder (songs/songs.dta with moggs on disk).</summary>
    RbFolder,
}

/// <summary>
/// One song in the library, with what is needed to separate it and where YARG looks for its stems.
/// </summary>
public sealed class Song
{
    public required SongKind Kind { get; init; }

    /// <summary>The song folder the user added to YARG that holds this song.</summary>
    public required string Root { get; init; }

    /// <summary>Song folder, .sng file, CON file, or extracted RB "songs" folder.</summary>
    public required string Location { get; init; }

    public string Artist { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Rock Band short name (the folder in songs/&lt;name&gt;/...), or the .sng id YARG builds.
    /// Empty for unpacked folders, whose stems go in the folder itself.
    /// </summary>
    public string StemId { get; init; } = string.Empty;

    /// <summary>Rock Band only: path inside the CON, or file on disk, of the mogg.</summary>
    public string MoggPath { get; init; } = string.Empty;

    /// <summary>Rock Band only: mogg channels that hold drums, bass, and vocals.</summary>
    public int[] CoreChannels { get; init; } = Array.Empty<int>();

    /// <summary>Rock Band only: mogg channels left for the song stem (not an instrument or crowd).</summary>
    public int[] SongChannels { get; init; } = Array.Empty<int>();

    /// <summary>True when the song lists no vocals, bass, or drums at all.</summary>
    public bool ListsNoStems { get; init; }

    /// <summary>Rock Band songs that list stems need a listen to know whether those channels are silent.</summary>
    public bool NeedsSilenceCheck => (Kind is SongKind.Con or SongKind.RbFolder) && !ListsNoStems && CoreChannels.Length > 0;

    public string Label => string.IsNullOrWhiteSpace(Artist) ? Name : Artist + " - " + Name;

    /// <summary>Where the stems go, matching YARG.Core's CustomStems.</summary>
    public string StemDirectory => Kind == SongKind.Folder
        ? Location
        : Path.Combine(Root, StemLayout.FolderName, StemLayout.SanitizeId(StemId));

    public bool HasStems => StemLayout.HasMarker(StemDirectory);

    /// <summary>Key for the silence check cache: changes when the file does.</summary>
    public string CacheKey
    {
        get
        {
            string file = Kind == SongKind.RbFolder ? MoggPath : Location;
            try
            {
                var info = new FileInfo(file);
                return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{StemId}";
            }
            catch (Exception)
            {
                return file + "|" + StemId;
            }
        }
    }
}
