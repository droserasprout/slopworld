using System;
using NUnitLite;

namespace SlopWorld.Tests
{
    static class Program
    {
        static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--perf-bench") >= 0) return Benchmarks.Run();
            // NUnitLite otherwise writes TestResult.xml into the caller's directory.
            var runnerArgs = new string[args.Length + 1];
            runnerArgs[0] = "--work=" + AppContext.BaseDirectory;
            Array.Copy(args, 0, runnerArgs, 1, args.Length);
            return new AutoRun().Execute(runnerArgs);
        }
    }
}
