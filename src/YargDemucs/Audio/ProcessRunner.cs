using System.Diagnostics;
using System.Text;

namespace YargDemucs.Audio;

/// <summary>
/// Runs ffmpeg and Demucs at a chosen CPU priority and can kill the running one (with its
/// child processes, which is where Demucs uses the GPU) when the user stops.
/// </summary>
public sealed class ProcessRunner
{
    private readonly object _gate = new();
    private Process? _active;
    private volatile bool _stop;

    public ProcessPriorityClass Priority { get; set; } = ProcessPriorityClass.BelowNormal;

    public bool IsStopRequested => _stop;

    public void Stop()
    {
        Process? active;
        lock (_gate)
        {
            _stop = true;
            active = _active;
        }

        Kill(active);
    }

    public readonly record struct Result(bool Started, int ExitCode, string Output);

    public Result Run(string fileName, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in arguments)
        {
            start.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(start)!;
            if (process == null)
            {
                return new Result(false, -1, string.Empty);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new Result(false, -1, ex.Message);
        }

        using (process)
        {
            try
            {
                process.PriorityClass = Priority;
            }
            catch (Exception)
            {
                // It may already have exited.
            }

            lock (_gate)
            {
                _active = process;
                if (_stop)
                {
                    Kill(process);
                }
            }

            var stderr = Task.Run(() => ReadTail(process.StandardError));
            var stdout = Task.Run(() => ReadTail(process.StandardOutput));
            process.WaitForExit();
            string output = Combine(stderr.Result, stdout.Result);
            lock (_gate)
            {
                if (ReferenceEquals(_active, process))
                {
                    _active = null;
                }
            }

            return new Result(true, process.ExitCode, output);
        }
    }

    private static void Kill(Process? process)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
        }
    }

    private static string ReadTail(StreamReader reader)
    {
        var tail = new StringBuilder();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            tail.AppendLine(line);
            if (tail.Length > 16000)
            {
                tail.Remove(0, tail.Length - 12000);
            }
        }

        return tail.ToString();
    }

    private static string Combine(string a, string b)
    {
        a = a.Trim();
        b = b.Trim();
        if (a.Length > 0 && b.Length > 0)
        {
            return a + "\n" + b;
        }

        return a.Length > 0 ? a : b;
    }
}
