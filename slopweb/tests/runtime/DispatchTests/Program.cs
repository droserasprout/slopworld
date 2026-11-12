using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

static class DispatchChecks
{
    private static readonly string AssemblyName = typeof(Fixture).Assembly.GetName().Name!;
    private static readonly string Owner = typeof(Fixture).FullName!;

    static object? Baseline(string method, object[] args) =>
        Assembly
            .Load(AssemblyName)
            .GetType(Owner, true)!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m =>
                m.Name == method
                && m.GetParameters().Length == args.Length
                && m.GetParameters()
                    .Zip(args)
                    .All(pair => pair.First.ParameterType.IsInstanceOfType(pair.Second))
            )
            .Invoke(null, args);

    static string Outcome(Func<object?> action)
    {
        try
        {
            return $"value:{action()}";
        }
        catch (Exception error)
        {
            return $"{error.GetType()}:{error.Message}|{error.InnerException?.GetType()}:{error.InnerException?.Message}";
        }
    }

    static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static void Main(string[] args)
    {
        var cases = new (string Method, object[] Args)[]
        {
            ("Zero", []),
            ("Overload", [7]),
            ("Overload", ["text"]),
            ("Base", [new Derived()]),
            ("Interface", [new Derived()]),
            ("Nullable", [7]),
            ("Enum", [DayOfWeek.Monday]),
            ("Private", [3]),
            ("Inherited", [3]),
            ("Ambiguous", ["text"]),
            ("Missing", []),
            ("Instance", []),
            ("Optional", []),
            ("Overload", [3L]),
            ("Overload", [null!]),
            ("Nullable", [null!]),
            ("ByRef", [3]),
            ("Generic", []),
            ("Throws", []),
        };
        for (int pass = 0; pass < 3; pass++)
            foreach (var test in cases)
            {
                string expected = Outcome(() => Baseline(test.Method, test.Args));
                string actual = Outcome(() =>
                    Probe.Dispatch(AssemblyName, Owner, test.Method, test.Args)
                );
                Expect(actual == expected, $"{test.Method}: {actual} != {expected}");
            }
        // Repeated successful dispatch still invokes the target with the current value.
        Expect(
            (int)Probe.Dispatch(AssemblyName, Owner, "Overload", [91])! == 92,
            "Stale arguments"
        );
        Expect((int)Probe.Dispatch(AssemblyName, Owner, "Counter", [])! == 1, "First call skipped");
        Expect((int)Probe.Dispatch(AssemblyName, Owner, "Counter", [])! == 2, "Result was cached");

        // A missing assembly and then a missing type must remain retryable as staging proceeds.
        string lateName = "DispatchLate" + Guid.NewGuid().ToString("N");
        Expect(
            Outcome(() => Probe.Resolve(lateName, "Late"))
                .StartsWith("System.IO.FileNotFoundException:", StringComparison.Ordinal),
            "Expected missing assembly"
        );
        var late = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(lateName),
            AssemblyBuilderAccess.Run
        );
        ResolveEventHandler resolver = (_, request) =>
            new AssemblyName(request.Name).Name == lateName ? late : null;
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            Expect(
                Outcome(() => Probe.Resolve(lateName, "Late"))
                    .StartsWith("System.TypeLoadException:", StringComparison.Ordinal),
                "Expected missing type"
            );
            var module = late.DefineDynamicModule("LateModule");
            var type = module.DefineType("Late").CreateType()!;
            Expect(
                Probe.Resolve(lateName, "Late") == type,
                "Cached failure prevented staged type resolution"
            );
            // Many distinct actual runtime types exercise signature eviction without
            // changing assignability; many method names exercise metadata eviction.
            for (int i = 0; i < 280; i++)
            {
                var extra = module.DefineType("Extra" + i).CreateType()!;
                Probe.Resolve(lateName, extra.FullName!);
                Expect(
                    (string)
                        Probe.Dispatch(
                            AssemblyName,
                            Owner,
                            "Object",
                            [Activator.CreateInstance(extra)!]
                        )! == extra.Name,
                    "Signature eviction changed dispatch"
                );
                Probe.Dispatch(AssemblyName, Owner, "missing" + i, [], expectFailure: true);
            }
            Probe.CheckBounds();
            Expect(
                (int)Probe.Dispatch(AssemblyName, Owner, "Overload", [4])! == 5,
                "Eviction lost dispatch"
            );
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
        Parallel.For(
            0,
            200,
            i =>
                Expect(
                    (int)Probe.Dispatch(AssemblyName, Owner, "Overload", [i])! == i + 1,
                    "Concurrent dispatch"
                )
        );
        Console.WriteLine(
            "Probe dispatch: exact source agrees with original selection/errors; late loading, eviction, fresh arguments/results and concurrency passed."
        );
        if (args.Contains("--benchmark"))
            Benchmark();
    }

    static void Benchmark()
    {
        const int iterations = 200_000;
        object[] arguments = [7];
        Func<object?> baseline = () => Baseline("Overload", arguments);
        Func<object?> cached = () => Probe.Dispatch(AssemblyName, Owner, "Overload", arguments);
        foreach (var (name, operation) in new[] { ("original", baseline), ("cached", cached) })
        {
            for (int i = 0; i < 10_000; i++)
                operation();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            int sum = 0;
            for (int i = 0; i < iterations; i++)
                sum += (int)operation()!;
            watch.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Expect(sum == iterations * 8, "Benchmark changed result");
            Console.WriteLine(
                $"{name}: {iterations} calls, {watch.Elapsed.TotalMilliseconds:F1} ms, {allocated / (double)iterations:F1} bytes/call"
            );
        }
        Console.WriteLine(
            "Desktop reflection microbenchmark only; WASM frame CPU requires separate profiling."
        );
    }
}

public partial class Probe
{
    internal static Type Resolve(string assembly, string type) => Type(assembly, type);

    internal static object? Dispatch(
        string assembly,
        string type,
        string method,
        object[] args,
        bool expectFailure = false
    )
    {
        if (!expectFailure)
            return Invoke(assembly, type, method, args);
        try
        {
            Invoke(assembly, type, method, args);
            throw new Exception("Expected missing dispatch");
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal static void CheckBounds()
    {
        if (ResolvedTypes.Count > ReflectionCacheLimit || Dispatches.Count > ReflectionCacheLimit)
            throw new Exception("Metadata cache unbounded");
        foreach (var dispatch in Dispatches.Values)
        {
            var signatures = (System.Collections.ICollection)
                typeof(MethodDispatch)
                    .GetField("signatures", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(dispatch)!;
            if (signatures.Count > SignatureCacheLimit)
                throw new Exception("Signature cache unbounded");
        }
    }
}

public interface IFixture { }

public class Parent
{
    public static int Inherited(int value) => value;
}

public class Derived : IFixture { }

public class Fixture : Parent
{
    private static int count;

    public static int Zero() => 42;

    public static int Overload(int value) => value + 1;

    public static string Overload(string value) => value;

    public static string Base(object value) => value.GetType().Name;

    public static string Interface(IFixture value) => value.GetType().Name;

    public static string Object(object value) => value.GetType().Name;

    public static int Nullable(int? value) => value ?? -1;

    public static DayOfWeek Enum(DayOfWeek value) => value;

    private static int Private(int value) => value;

    public static string Ambiguous(object value) => "object";

    public static string Ambiguous(string value) => "string";

    public int Instance() => 0;

    public static int Optional(int value = 3) => value;

    public static int ByRef(ref int value) => value;

    public static T Generic<T>() => default!;

    public static int Throws() => throw new ArgumentException("fixture");

    public static int Counter() => ++count;
}
