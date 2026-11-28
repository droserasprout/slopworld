namespace SlopWeb.Runtime;

// Only publish a successfully constructed runtime. Early calls can retry once private
// assemblies have arrived. The normal dispatch path uses a volatile read.
public sealed class LateRuntime<T>(Func<T> activate)
    where T : class
{
    object? gate;
    T? instance;

    public T Value => LazyInitializer.EnsureInitialized(ref instance, ref gate, activate);
}
