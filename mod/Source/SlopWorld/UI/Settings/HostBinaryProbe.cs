using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SlopWorld
{
    // Locate commands in the game process environment. Daemon discovery uses /api/whereis.
    internal static class HostBinaryProbe
    {
        internal static string Locate(string command)
        {
            string path = LocateWithWhich(command);
            return !string.IsNullOrEmpty(path) ? path : LocateOnPath(command);
        }

        static string LocateWithWhich(string command)
        {
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var process = Process.Start(start))
                {
                    if (process == null) return null;
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0) return null;
                    return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault()?.Trim();
                }
            }
            catch
            {
                return null;
            }
        }

        static string LocateOnPath(string command)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                string root = string.IsNullOrEmpty(directory) ? "." : directory;
                string candidate;
                try { candidate = Path.Combine(root, command); }
                catch { continue; }
                if (File.Exists(candidate))
                {
                    try { return Path.GetFullPath(candidate); }
                    catch { return candidate; }
                }
            }
            return null;
        }
    }
}
