using System.Diagnostics;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    internal static class ViewCommandFixture
    {
        internal static string Run(string command, int expected = 0, string bashEnv = null)
        {
            var start = new ProcessStartInfo("bash")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(command);
            if (bashEnv != null) start.Environment["BASH_ENV"] = bashEnv;
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000))
                {
                    process.Kill(true);
                    Assert.Fail("View command fixture timed out");
                }
                Assert.That(process.ExitCode, Is.EqualTo(expected), error.GetAwaiter().GetResult());
                return output.GetAwaiter().GetResult();
            }
        }
    }
}
