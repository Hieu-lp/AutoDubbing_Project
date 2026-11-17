using System;
using System.Diagnostics;
using System.IO;
using System.Text;

// Compiled as WindowsApplication: Task Scheduler starts no console for this launcher.
internal static class RunTeamUpdateHidden
{
    private static readonly object LogLock = new object();

    private static string Quote(string argument)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        result.Append('\\', slashes * 2);
        return result.Append('"').ToString();
    }

    private static void Log(string path, string line)
    {
        if (line == null) return;
        lock (LogLock)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
            {
                string previous = path + ".previous";
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(path, previous);
            }
            File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + line + Environment.NewLine, Encoding.UTF8);
        }
    }

    private static void SafeLog(string path, string line)
    {
        try { Log(path, line); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments.Length < 2) return 2;
        string logPath = arguments[0];
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath)));
            var command = new StringBuilder("-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ");
            for (int index = 1; index < arguments.Length; index++)
                command.Append(Quote(arguments[index])).Append(' ');
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                                        @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = command.ToString(),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(arguments[1]))
            };
            using (var child = new Process { StartInfo = start })
            {
                child.OutputDataReceived += (sender, data) => SafeLog(logPath, data.Data);
                child.ErrorDataReceived += (sender, data) => SafeLog(logPath, data.Data);
                child.Start();
                child.BeginOutputReadLine();
                child.BeginErrorReadLine();
                child.WaitForExit();
                return child.ExitCode;
            }
        }
        catch (Exception error)
        {
            // Don't echo the command, config contents or raw external exception text.
            SafeLog(logPath, "Hidden updater launcher failed: " + error.GetType().Name);
            return 1;
        }
    }
}
