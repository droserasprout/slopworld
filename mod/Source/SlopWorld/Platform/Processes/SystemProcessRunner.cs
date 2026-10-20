using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SlopWorld
{
    // Describe one command so tests can supply a result without starting a process.
    public sealed class ProcessSpec
    {
        public string FileName;
        public string Arguments;
        public int TimeoutMs;
    }

    // Store the result of one process invocation. Started is false if the process cannot start.
    public sealed class ProcessRun
    {
        public bool Started;
        public bool TimedOut;
        public bool Canceled;
        public int ExitCode;
        public string StandardOutput = "";
        public string StandardError = "";
    }

    // Use a process runner interface so tests can supply results without starting child processes.
    public interface IProcessRunner
    {
        ProcessRun Run(ProcessSpec spec, CancellationToken cancel);
    }

    // Read standard output and standard error while the child process runs. Stop it on timeout or cancellation.
    // Keep this implementation independent of Unity so tests can run local helper commands without audio capture.
    public sealed class SystemProcessRunner : IProcessRunner
    {
        public ProcessRun Run(ProcessSpec spec, CancellationToken cancel)
        {
            var run = new ProcessRun();
            var info = new ProcessStartInfo
            {
                FileName = spec.FileName,
                Arguments = spec.Arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            try
            {
                using (var process = new Process { StartInfo = info })
                {
                    try
                    {
                        if (!process.Start())
                            return run;
                    }
                    catch
                    {
                        // Return Started as false if process startup throws an exception.
                        return run;
                    }

                    run.Started = true;
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    Task<string> error = process.StandardError.ReadToEndAsync();

                    using (cancel.Register(() => Kill(process)))
                    {
                        if (!process.WaitForExit(spec.TimeoutMs))
                        {
                            Kill(process);
                            try
                            {
                                process.WaitForExit(1000);
                            }
                            catch { }
                            run.TimedOut = true;
                        }
                    }

                    run.Canceled = cancel.IsCancellationRequested;
                    run.StandardOutput = Drain(output);
                    run.StandardError = Drain(error);
                    try
                    {
                        run.ExitCode = process.ExitCode;
                    }
                    catch { }
                    return run;
                }
            }
            catch
            {
                return run;
            }
        }

        static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch { }
        }

        static string Drain(Task<string> task)
        {
            try
            {
                return task.GetAwaiter().GetResult() ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}
