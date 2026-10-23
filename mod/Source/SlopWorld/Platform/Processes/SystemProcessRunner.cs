using System;
using System.Diagnostics;
using System.IO;
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
        public string StandardInput;

        // Positive values select bounded binary stdout capture instead of text decoding.
        public int BinaryOutputLimit;

        // Selection owners fork children which keep inherited pipes open.
        public bool InheritOutput;
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
        public byte[] BinaryOutput = Array.Empty<byte>();
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
            var run = new ProcessRun { ExitCode = -1 };
            var info = new ProcessStartInfo
            {
                FileName = spec.FileName,
                Arguments = spec.Arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = !spec.InheritOutput,
                RedirectStandardError = !spec.InheritOutput,
                RedirectStandardInput = spec.StandardInput != null,
                StandardOutputEncoding = spec.InheritOutput
                    ? null
                    : new System.Text.UTF8Encoding(false, true),
            };

#pragma warning disable CA2000 // Finally disposes directly or transfers ownership to ClosePipes for bounded teardown.
            var process = new Process { StartInfo = info };
#pragma warning restore CA2000
            Task pipes = Task.CompletedTask;
            try
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
                var elapsed = Stopwatch.StartNew();
                Task<byte[]> binary =
                    spec.BinaryOutputLimit > 0
                        ? ReadBinary(process, spec.BinaryOutputLimit)
                        : Task.FromResult(Array.Empty<byte>());
                Task<string> output =
                    spec.InheritOutput || spec.BinaryOutputLimit > 0
                        ? Task.FromResult("")
                        : process.StandardOutput.ReadToEndAsync();
                Task<string> error = spec.InheritOutput
                    ? Task.FromResult("")
                    : process.StandardError.ReadToEndAsync();
                // Write off-thread so a child that never reads stdin still obeys the deadline.
                Task input =
                    spec.StandardInput == null
                        ? Task.CompletedTask
                        : Task.Run(() =>
                        {
                            // Unity/Mono's default stdin writer may use a legacy encoding
                            // and replace non-ASCII characters with '?'. Write UTF-8 bytes
                            // directly, without a BOM, independently of the process locale.
                            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(spec.StandardInput);
                            using (var stream = process.StandardInput.BaseStream)
                            {
                                stream.Write(bytes, 0, bytes.Length);
                            }
                        });

                pipes = Task.WhenAll(input, output, error, binary);
                using (cancel.Register(() => Kill(process)))
                {
                    bool exited = process.WaitForExit(Remaining(spec, elapsed));
                    bool drained = exited && Wait(pipes, Remaining(spec, elapsed), cancel);
                    run.Canceled = cancel.IsCancellationRequested;
                    run.TimedOut = !run.Canceled && (!exited || !drained);
                    if (!exited || !drained)
                    {
                        Kill(process);
                        // Bounded teardown grace lets ordinary children release their pipes
                        // and preserves partial output. Inherited handles may never reach EOF.
                        try
                        {
                            process.WaitForExit(100);
                        }
                        catch { }
                        Wait(pipes, 100, CancellationToken.None);
                    }
                }

                run.StandardOutput = Drain(output);
                run.StandardError = Drain(error);
                if (binary.Status == TaskStatus.RanToCompletion)
                    run.BinaryOutput = binary.Result;
                else if (binary.IsFaulted)
                    run.StandardError = binary.Exception.GetBaseException().Message;
                run.ExitCode = -1;
                if (!run.TimedOut && !run.Canceled && pipes.Status == TaskStatus.RanToCompletion)
                    run.ExitCode = process.ExitCode;
                return run;
            }
            catch
            {
                Kill(process);
                return run;
            }
            finally
            {
                // Observe delayed pipe failures even when a descendant retains an inherited
                // handle. Closing streams can block on Mono, so teardown must not hold up
                // the caller (or the next serialized clipboard operation).
                _ = pipes.ContinueWith(
                    t =>
                    {
                        _ = t.Exception;
                    },
                    TaskScheduler.Default
                );
                if (pipes.IsCompleted)
                    process.Dispose();
                else
                    _ = Task.Run(() => ClosePipes(process, spec));
            }
        }

        static int Remaining(ProcessSpec spec, Stopwatch elapsed) =>
            spec.TimeoutMs < 0
                ? -1
                : (int)Math.Max(0, spec.TimeoutMs - elapsed.ElapsedMilliseconds);

        static bool Wait(Task task, int timeout, CancellationToken cancel)
        {
            try
            {
                return task.Wait(timeout, cancel);
            }
            catch (AggregateException)
            {
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        static void ClosePipes(Process process, ProcessSpec spec)
        {
            try
            {
                if (spec.StandardInput != null)
                    process.StandardInput.BaseStream.Dispose();
                if (!spec.InheritOutput)
                {
                    process.StandardOutput.BaseStream.Dispose();
                    process.StandardError.BaseStream.Dispose();
                }
            }
            catch { }
            finally
            {
                process.Dispose();
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

        static async Task<byte[]> ReadBinary(Process process, int limit)
        {
            try
            {
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int count;
#pragma warning disable CA1835 // Unity's .NET Framework runtime has no Memory<byte> overload.
                    while (
                        (
                            count = await process
                                .StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length)
                                .ConfigureAwait(false)
                        ) != 0
                    )
                    {
                        if (output.Length + count > limit)
                            throw new IOException(
                                "binary process output exceeds " + limit + " bytes"
                            );
                        output.Write(buffer, 0, count);
                    }
#pragma warning restore CA1835
                    return output.ToArray();
                }
            }
            catch
            {
                Kill(process);
                throw;
            }
        }

        static string Drain(Task<string> task)
        {
            try
            {
                return task.Status == TaskStatus.RanToCompletion ? task.Result ?? "" : "";
            }
            catch
            {
                return "";
            }
        }
    }
}
