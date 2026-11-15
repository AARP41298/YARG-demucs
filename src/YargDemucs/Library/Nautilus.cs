using System.Diagnostics;
using System.Text;

namespace YargDemucs.Library;

/// <summary>
/// Reads CON/LIVE packages and decrypts moggs through Nautilus's headless CLI
/// (the AARP41298/Nautilus fork, branch master-backstage):
///   Nautilus.exe -backstagehero dta     &lt;con&gt; &lt;out.dta&gt;
///   Nautilus.exe -backstagehero moggs   &lt;con&gt; &lt;list.txt&gt; &lt;outdir&gt;
///   Nautilus.exe -backstagehero decrypt &lt;in.mogg&gt; &lt;out.mogg&gt;
/// </summary>
public sealed class Nautilus
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);
    private readonly string _exe;

    public Nautilus(string exe)
    {
        _exe = exe;
    }

    public byte[] ReadDta(string con, string workDir)
    {
        string output = Path.Combine(workDir, "songs.dta");
        Run(output, "dta", con, output);
        return File.ReadAllBytes(output);
    }

    /// <summary>
    /// Extracts and decrypts these moggs (paths inside the CON) into outDir, keeping their paths.
    /// Reads the package once. Returns the paths that could not be extracted.
    /// </summary>
    public List<string> ExtractMoggs(string con, IEnumerable<string> paths, string outDir)
    {
        Directory.CreateDirectory(outDir);
        string list = outDir.TrimEnd('\\', '/') + ".txt";
        File.WriteAllLines(list, paths, new UTF8Encoding(false));
        string errors = outDir.TrimEnd('\\', '/') + ".error";
        try
        {
            Run(errors, "moggs", con, list, outDir);
        }
        catch (NautilusException) when (File.Exists(errors))
        {
            // Every mogg failed; the error file lists them.
        }

        var failed = File.Exists(errors)
            ? File.ReadAllLines(errors).Select(l => l.Split('\t')[0]).ToList()
            : new List<string>();
        File.Delete(list);
        File.Delete(errors);
        return failed;
    }

    public void Decrypt(string mogg, string output)
    {
        Run(output, "decrypt", mogg, output);
    }

    private void Run(string outPath, params string[] args)
    {
        string errorFile = outPath + ".error";
        if (File.Exists(errorFile) && !outPath.EndsWith(".error", StringComparison.Ordinal))
        {
            File.Delete(errorFile);
        }

        var start = new ProcessStartInfo
        {
            FileName = _exe,
            // Nautilus looks for its bin\ folder next to the exe.
            WorkingDirectory = Path.GetDirectoryName(_exe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-backstagehero");
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new NautilusException("Nautilus did not start.");
        if (!process.WaitForExit(Timeout))
        {
            try
            {
                process.Kill(true);
            }
            catch (Exception)
            {
            }

            throw new NautilusException("Nautilus timed out.");
        }

        if (process.ExitCode != 0)
        {
            string reason = File.Exists(errorFile) ? File.ReadAllText(errorFile).Trim() : $"exit code {process.ExitCode}";
            if (File.Exists(errorFile) && !outPath.EndsWith(".error", StringComparison.Ordinal))
            {
                File.Delete(errorFile);
            }

            throw new NautilusException("Nautilus: " + reason);
        }
    }
}

public sealed class NautilusException : Exception
{
    public NautilusException(string message) : base(message)
    {
    }
}
