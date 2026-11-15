namespace YargDemucs.Audio;

/// <summary>
/// ffmpeg reorders Ogg Vorbis channels into its own surround order when a file has 3 to 8
/// channels (libavcodec ff_vorbis_channel_layout_offsets). songs.dta numbers channels in file
/// order, as YARG's audio engine reads them, so they must be translated before use in ffmpeg.
/// </summary>
public static class VorbisChannels
{
    // Row n-1: ffmpeg channel j holds Vorbis channel Offsets[n-1][j].
    private static readonly int[][] Offsets =
    {
        new[] { 0 },
        new[] { 0, 1 },
        new[] { 0, 2, 1 },
        new[] { 0, 1, 2, 3 },
        new[] { 0, 2, 1, 3, 4 },
        new[] { 0, 2, 1, 5, 3, 4 },
        new[] { 0, 2, 1, 6, 5, 3, 4 },
        new[] { 0, 2, 1, 7, 5, 6, 3, 4 },
    };

    /// <summary>Translates file-order channel numbers to the order ffmpeg decodes them in.</summary>
    public static int[] ToFfmpegOrder(string audioPath, int[] channels)
    {
        if (channels.Length == 0 || !audioPath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            return channels;
        }

        int count = ReadChannelCount(audioPath);
        if (count < 3 || count > 8)
        {
            return channels;
        }

        var row = Offsets[count - 1];
        return channels.Select(c => c >= 0 && c < count ? Array.IndexOf(row, c) : c).ToArray();
    }

    /// <summary>Channel count from the Vorbis identification header, or 0.</summary>
    public static int ReadChannelCount(string oggPath)
    {
        try
        {
            using var stream = File.OpenRead(oggPath);
            var head = new byte[512];
            int read = stream.Read(head, 0, head.Length);
            var marker = "\u0001vorbis"u8;
            int at = head.AsSpan(0, read).IndexOf(marker);
            // 0x01 "vorbis", then a 4-byte version, then the channel count.
            return at >= 0 && at + 11 < read ? head[at + 11] : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
