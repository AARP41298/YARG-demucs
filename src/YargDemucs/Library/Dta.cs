using System.Globalization;
using System.Text;

namespace YargDemucs.Library;

/// <summary>
/// Minimal reader for Rock Band songs.dta files: nested lists of symbols, strings, and numbers.
/// </summary>
public static class Dta
{
    public sealed class Node
    {
        public readonly List<object> Items = new();

        /// <summary>First item as text: the key of a (key value...) list.</summary>
        public string Key => Items.Count > 0 && Items[0] is string s ? s : string.Empty;

        public Node? Child(string key)
        {
            foreach (var item in Items)
            {
                if (item is Node node && node.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>Second item as text, for (key "value").</summary>
        public string Text(string key)
        {
            var child = Child(key);
            return child != null && child.Items.Count > 1 && child.Items[1] is string s ? s : string.Empty;
        }

        /// <summary>Every whole number after the key, at any depth.</summary>
        public List<int> Numbers()
        {
            var numbers = new List<int>();
            for (int i = 1; i < Items.Count; i++)
            {
                Collect(Items[i], numbers);
            }

            return numbers;
        }

        /// <summary>How many numbers follow the key (pans, vols, cores).</summary>
        public int CountValues()
        {
            int count = 0;
            for (int i = 1; i < Items.Count; i++)
            {
                count += CountIn(Items[i]);
            }

            return count;
        }

        private static void Collect(object item, List<int> numbers)
        {
            if (item is Node node)
            {
                foreach (var child in node.Items)
                {
                    Collect(child, numbers);
                }
            }
            else if (item is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                numbers.Add(value);
            }
        }

        private static int CountIn(object item)
        {
            if (item is Node node)
            {
                int count = 0;
                foreach (var child in node.Items)
                {
                    count += CountIn(child);
                }

                return count;
            }

            return item is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? 1 : 0;
        }
    }

    /// <summary>A Rock Band song read from a songs.dta entry.</summary>
    public sealed class RbSong
    {
        public string ShortName = string.Empty;
        public string Name = string.Empty;
        public string Artist = string.Empty;
        /// <summary>songs/x/x (no extension), as written in (song (name ...)).</summary>
        public string Location = string.Empty;
        public int[] Drums = Array.Empty<int>();
        public int[] Bass = Array.Empty<int>();
        public int[] Vocals = Array.Empty<int>();
        public int[] SongChannels = Array.Empty<int>();

        public int[] CoreChannels => Drums.Concat(Bass).Concat(Vocals).Distinct().OrderBy(c => c).ToArray();
        public bool ListsNoStems => Drums.Length == 0 && Bass.Length == 0 && Vocals.Length == 0;
    }

    public static List<RbSong> ReadSongs(byte[] data)
    {
        var songs = new List<RbSong>();
        foreach (var item in Parse(Decode(data)).Items)
        {
            if (item is not Node entry || entry.Key.Length == 0)
            {
                continue;
            }

            var song = entry.Child("song");
            if (song == null)
            {
                continue;
            }

            string location = song.Text("name").Trim().TrimStart('/');
            if (location.Length == 0)
            {
                continue;
            }

            var rb = new RbSong
            {
                Name = entry.Text("name"),
                Artist = entry.Text("artist"),
                Location = location,
                // YARG takes the short name from the folder in songs/<name>/...
                ShortName = ShortNameOf(location, entry.Key),
            };

            var used = new HashSet<int>();
            var tracks = song.Child("tracks");
            if (tracks != null)
            {
                foreach (var trackItem in tracks.Items.Skip(1))
                {
                    // Usually ((drum (0 1)) (bass 2) ...), but some files skip the outer list.
                    var lists = trackItem is Node n && n.Key.Length == 0
                        ? n.Items.OfType<Node>()
                        : new[] { trackItem }.OfType<Node>();
                    foreach (var track in lists)
                    {
                        if (track.Key.Length == 0)
                        {
                            continue;
                        }

                        var channels = track.Numbers().ToArray();
                        foreach (int c in channels)
                        {
                            used.Add(c);
                        }

                        switch (track.Key.ToLowerInvariant())
                        {
                            case "drum":
                                rb.Drums = channels;
                                break;
                            case "bass":
                                rb.Bass = channels;
                                break;
                            case "vocals":
                                rb.Vocals = channels;
                                break;
                        }
                    }
                }
            }

            var crowd = song.Child("crowd_channels");
            if (crowd != null)
            {
                foreach (int c in crowd.Numbers())
                {
                    used.Add(c);
                }
            }

            int total = song.Child("pans")?.CountValues() ?? 0;
            if (total == 0)
            {
                total = song.Child("vols")?.CountValues() ?? 0;
            }

            rb.SongChannels = Enumerable.Range(0, total).Where(c => !used.Contains(c)).ToArray();
            songs.Add(rb);
        }

        return songs;
    }

    private static string ShortNameOf(string location, string fallback)
    {
        if (location.StartsWith("songs/", StringComparison.OrdinalIgnoreCase))
        {
            int slash = location.IndexOf('/', 6);
            if (slash > 6)
            {
                return location.Substring(6, slash - 6);
            }
        }

        return fallback;
    }

    private static string Decode(byte[] data)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(data);
        }
    }

    public static Node Parse(string text)
    {
        var root = new Node();
        var stack = new Stack<Node>();
        stack.Push(root);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == ';')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }
            }
            else if (c is '(' or '{' or '[')
            {
                var node = new Node();
                stack.Peek().Items.Add(node);
                stack.Push(node);
                i++;
            }
            else if (c is ')' or '}' or ']')
            {
                if (stack.Count > 1)
                {
                    stack.Pop();
                }

                i++;
            }
            else if (c is '"' or '\'')
            {
                int end = text.IndexOf(c, i + 1);
                if (end < 0)
                {
                    end = text.Length;
                }

                stack.Peek().Items.Add(text.Substring(i + 1, end - i - 1));
                i = end + 1;
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('(' or ')' or '{' or '}' or '[' or ']' or ';'))
                {
                    i++;
                }

                stack.Peek().Items.Add(text.Substring(start, i - start));
            }
        }

        return root;
    }
}
