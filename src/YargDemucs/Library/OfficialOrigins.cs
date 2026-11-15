namespace YargDemucs.Library;

/// <summary>
/// game_origin values of songs released in Rock Band or Guitar Hero games and DLC. Those ship
/// with real stems, so they are not checked for silent stem channels unless asked.
/// Customs use ugc_plus (or other ugc values) and are always checked.
/// </summary>
public static class OfficialOrigins
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "rb1", "rb1_dlc", "rb2", "rb2_dlc", "rb3", "rb3_dlc", "rb4", "rb4_dlc", "rbvr", "rb_blitz", "blitz",
        "lego", "beatles", "greenday", "gdrb", "bh", "bh2", "bh_dlc",
    };

    public static bool IsOfficial(string origin)
    {
        // gh1, gh2, gh80s, gh3, ghot, ghwt, gh5, gh5_beta, ghm, ghvh, ghwor, ...
        return Names.Contains(origin)
            || origin.StartsWith("gh", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("rbtp", StringComparison.OrdinalIgnoreCase);
    }
}
