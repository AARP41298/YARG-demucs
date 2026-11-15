namespace YargDemucs;

/// <summary>
/// One console line rewritten in place (a few times a second) to show the app is working.
/// When output is redirected to a file, it writes a plain line every 10 seconds instead.
/// </summary>
public sealed class ProgressLine
{
    private readonly object _gate = new();
    private DateTime _last = DateTime.MinValue;
    private int _length;
    private string? _pending;

    public void Update(string text)
    {
        lock (_gate)
        {
            _pending = text;
            var now = DateTime.UtcNow;
            var interval = Console.IsOutputRedirected ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(250);
            if (now - _last < interval)
            {
                return;
            }

            _last = now;
            Write(text);
        }
    }

    /// <summary>Shows the last update and moves to a new line.</summary>
    public void Done()
    {
        lock (_gate)
        {
            if (_pending != null)
            {
                Write(_pending);
            }

            if (_length > 0)
            {
                Console.WriteLine();
            }

            _pending = null;
            _length = 0;
        }
    }

    private void Write(string text)
    {
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(text);
            return;
        }

        int width = Math.Max(20, SafeWidth() - 1);
        if (text.Length > width)
        {
            text = text[..(width - 3)] + "...";
        }

        Console.Write("\r" + text.PadRight(Math.Max(_length, text.Length)));
        _length = text.Length;
    }

    private static int SafeWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (IOException)
        {
            return 120;
        }
    }

    public static string Format(TimeSpan time)
    {
        if (time.TotalHours >= 1)
        {
            return $"{(int) time.TotalHours}h {time.Minutes:00}m";
        }

        return time.TotalMinutes >= 1 ? $"{(int) time.TotalMinutes}m {time.Seconds:00}s" : $"{time.Seconds}s";
    }
}
