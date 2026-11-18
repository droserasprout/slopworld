using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Loader;
using System.Runtime.Versioning;

[SupportedOSPlatform("browser")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1050",
    Justification = "The global Probe name is the managed export and offline bootstrap ABI."
)]
public partial class Probe
{
    public static void Main() { }

    private static readonly List<object> Results = new();

    [JSExport]
    public static string LoadAssembly(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return AssemblyLoadContext.Default.LoadFromStream(stream).FullName!;
    }

    [JSExport]
    public static string LoadPublishedAssembly(string name) => Assembly.Load(name).FullName!;

    // Runtime owns only successful reflection resolutions for this runtime. Bounds keep
    // diagnostic names/signatures from retaining arbitrary metadata indefinitely.
    private const int ReflectionCacheLimit = 256;
    private const int SignatureCacheLimit = 16;
    private static readonly object ReflectionGate = new();
    private static readonly Dictionary<(string Assembly, string Name), Type> ResolvedTypes = new();
    private static readonly Dictionary<(Type Owner, string Name), MethodDispatch> Dispatches =
        new();

    private static Type Type(string assembly, string type)
    {
        lock (ReflectionGate)
        {
            var key = (assembly, type);
            if (ResolvedTypes.TryGetValue(key, out var resolved))
                return resolved;
            // Do not retain failures: staged assemblies may not have been loaded yet.
            resolved = Assembly.Load(assembly).GetType(type, true)!;
            if (ResolvedTypes.Count == ReflectionCacheLimit)
                ResolvedTypes.Clear();
            ResolvedTypes.Add(key, resolved);
            return resolved;
        }
    }

    private static object? Invoke(string assembly, string type, string method, params object[] args)
    {
        MethodInfo target;
        lock (ReflectionGate)
        {
            var key = (Type(assembly, type), method);
            if (!Dispatches.TryGetValue(key, out var dispatch))
            {
                dispatch = new MethodDispatch(key.Item1, method);
                if (Dispatches.Count == ReflectionCacheLimit)
                    Dispatches.Clear();
                Dispatches.Add(key, dispatch);
            }
            target = dispatch.Resolve(args);
        }
        // Native callbacks may reenter Probe. Never hold cache ownership while invoking.
        return target.Invoke(null, args);
    }

    private sealed class MethodDispatch
    {
        private readonly (MethodInfo Method, ParameterInfo[] Parameters)[] candidates;
        private readonly List<(Type?[] Types, MethodInfo Method)> signatures = new();

        internal MethodDispatch(Type owner, string name)
        {
            candidates = owner
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => method.Name == name)
                .Select(method => (method, method.GetParameters()))
                .ToArray();
        }

        internal MethodInfo Resolve(object[] args)
        {
            foreach (var signature in signatures)
            {
                if (signature.Types.Length != args.Length)
                    continue;
                int i = 0;
                while (i < args.Length && signature.Types[i] == args[i]?.GetType())
                    i++;
                if (i == args.Length)
                    return signature.Method;
            }

            // Keep Single's no-match/ambiguity behavior and IsInstanceOfType semantics
            // (including null, nullable values, interfaces and base types).
            var target = candidates
                .Single(candidate =>
                    candidate.Parameters.Length == args.Length
                    && candidate
                        .Parameters.Zip(args)
                        .All(pair => pair.First.ParameterType.IsInstanceOfType(pair.Second))
                )
                .Method;
            if (signatures.Count == SignatureCacheLimit)
                signatures.RemoveAt(0);
            signatures.Add((args.Select(arg => arg?.GetType()).ToArray(), target));
            return target;
        }
    }

    private static void Check(string name, Func<object?> operation)
    {
        try
        {
            Results.Add(
                new
                {
                    name,
                    ok = true,
                    value = operation()?.ToString(),
                }
            );
        }
        catch (Exception error)
        {
            var chain = new List<string>();
            for (Exception? current = error; current != null; current = current.InnerException)
                chain.Add($"{current.GetType().FullName}: {current.Message}");
            Results.Add(
                new
                {
                    name,
                    ok = false,
                    errors = chain,
                    stack = error.ToString(),
                }
            );
        }
    }

    private static object? InstanceCall(object owner, string method, params object[] args) =>
        owner
            .GetType()
            .GetMethods()
            .Single(m =>
                m.Name == method
                && !m.IsGenericMethod
                && m.GetParameters().Length == args.Length
                && m.GetParameters()
                    .Zip(args)
                    .All(pair => pair.First.ParameterType.IsInstanceOfType(pair.Second))
            )
            .Invoke(owner, args);

    private static object Get(object owner, string name) =>
        owner.GetType().GetProperty(name)!.GetValue(owner)!;

    private static object? DrawCall(string name, params object[] args) =>
        Invoke("Waylith", "Waylith.Rendering.MeshDrawSubmission", name, args);
}
