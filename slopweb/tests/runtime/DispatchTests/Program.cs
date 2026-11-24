using System.Diagnostics;
using SlopWeb.Runtime;

static class DispatchChecks
{
    static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static void Main(string[] args)
    {
        InputFrameFixture.Check();
        CheckActivation();
        WorkspaceRuntimeFixture.Check();
        Console.WriteLine(
            "Workspace dispatch, activation retry, concurrent access and physical input checks passed."
        );
        if (args.Contains("--benchmark"))
            Benchmark();
    }

    interface IFrameFixture
    {
        int Update(int frame);
    }

    sealed class FrameFixture : IFrameFixture
    {
        public int Update(int frame) => frame + 1;
    }

    static void CheckActivation()
    {
        int attempts = 0;
        var runtime = new LateRuntime<IFrameFixture>(() =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new FileNotFoundException("Private assembly not staged");
            return new FrameFixture();
        });
        Expect(attempts == 0, "Bootstrap activated the runtime before use");
        try
        {
            _ = runtime.Value;
            throw new Exception("Expected an unstaged runtime");
        }
        catch (FileNotFoundException) { }

        Parallel.For(
            0,
            200,
            frame => Expect(runtime.Value.Update(frame) == frame + 1, "Lost frame argument")
        );
        Expect(attempts == 2, "Runtime activated more than once after staging");
    }

    static void Benchmark()
    {
        const int iterations = 200_000;
        var fixture = new PacketFixture();
        var cached = typeof(PacketFixture).GetMethod(nameof(PacketFixture.Configure))!;
        Func<int, int> reflected = frame =>
            (bool)cached.Invoke(fixture, [(frame & 1) == 0])! ? 1 : 0;
        Func<int, int> typed = frame => Probe.WorkspacePacketCache((frame & 1) == 0) ? 1 : 0;
        foreach (
            var (name, operation) in new[]
            {
                ("cached reflection", reflected),
                ("typed runtime", typed),
            }
        )
        {
            for (int i = 0; i < 10_000; i++)
                operation(i);
            var watch = new Stopwatch();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            watch.Start();
            long sum = 0;
            for (int i = 0; i < iterations; i++)
                sum += operation(i);
            watch.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Expect(sum == iterations / 2, "Benchmark changed result");
            Console.WriteLine(
                $"{name}: {iterations} calls, {watch.Elapsed.TotalMilliseconds:F1} ms, {allocated / (double)iterations:F1} bytes/call"
            );
        }
        Console.WriteLine("Desktop dispatch microbenchmark.");
    }
}

sealed class PacketFixture
{
    bool enabled;

    public bool Configure(bool enable)
    {
        bool previous = enabled;
        enabled = enable;
        return previous;
    }
}
