using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    private delegate int ScheduleAction(Action action, ReadOnlySpan<int> dependencies, int cost);
    private delegate int ScheduleBatch(Action[] actions, ReadOnlySpan<int> dependencies, int cost);

    [JSExport]
    public static string Jobs()
    {
        Results.Clear();
        var type = Type("Waylith", "Waylith.Jobs.WaylithJobRuntime");
        object? Call(string name, params object[] args) =>
            Invoke("Waylith", type.FullName!, name, args);
        void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
        var schedule = type.GetMethods()
            .Single(m => m.Name == "Schedule" && !m.IsGenericMethod)
            .CreateDelegate<ScheduleAction>();
        var batch = type.GetMethod("ScheduleParallelBatch")!.CreateDelegate<ScheduleBatch>();
        var errors = new List<string>();
        Check(
            "jobs.serial-lane",
            () =>
            {
                Call("SetErrorHandler", (Action<string>)errors.Add);
                var workers = (Thread[])
                    type.GetField("Workers", BindingFlags.NonPublic | BindingFlags.Static)!
                        .GetValue(null)!;
                Require(workers.Length == 0, "Background workers exist");
                Require(
                    (int)type.GetProperty("WorkerCount")!.GetValue(null)! == 1,
                    "Missing serial lane"
                );
                Require((int)Call("IdealBatchCount", 16)! == 4, "Invalid serial batch sizing");
                return "zero threads; one execution lane";
            }
        );
        Check(
            "jobs.dependencies-and-repeat-complete",
            () =>
            {
                var order = new List<int>();
                int first = schedule(() => order.Add(1), [], 1);
                int second = schedule(() => order.Add(2), [first], 1);
                int third = schedule(() => order.Add(3), [first, second], 1);
                Call("Complete", third);
                Call("Complete", third);
                Require(
                    order.SequenceEqual(new[] { 1, 2, 3 }),
                    "Dependency ordering or repeated completion failed"
                );
                return string.Join(",", order);
            }
        );
        Check(
            "jobs.chunks-and-nested-work",
            () =>
            {
                var seen = new int[37];
                int handle = (int)
                    Call("ScheduleFor", 0, seen.Length, (Action<int>)(i => seen[i]++), 4, 0)!;
                Call("Complete", handle);
                Require(seen.All(count => count == 1), "Chunks lost or duplicated");
                int nested = 0;
                int parent = schedule(
                    () =>
                    {
                        int child = schedule(() => nested++, [], 1);
                        Call("Complete", child);
                    },
                    [],
                    1
                );
                Call("Complete", parent);
                Require(nested == 1, "Nested completion failed");
                return "37 iterations once; nested child completed";
            }
        );
        Check(
            "jobs.failure-and-recovery",
            () =>
            {
                int failed = schedule(
                    () => throw new InvalidOperationException("browser-job-sentinel"),
                    [],
                    1
                );
                var caught = false;
                try
                {
                    Call("Complete", failed);
                }
                catch (TargetInvocationException error)
                    when (error.InnerException?.Message == "browser-job-sentinel")
                {
                    caught = true;
                }
                Require(
                    caught && errors.Any(e => e.Contains("browser-job-sentinel")),
                    "Job failure was swallowed"
                );
                // Installed semantics allow dependents of a faulted job to run.
                int recovered = 0;
                int next = batch([() => recovered++, () => recovered++], [failed], 1);
                Call("Complete", next);
                Require(
                    recovered == 2 && !(bool)type.GetProperty("HasPendingWork")!.GetValue(null)!,
                    "Recovery left pending work"
                );
                return "failure rethrown; dependent batch completed";
            }
        );
        return JsonSerializer.Serialize(new { checks = Results });
    }
}
