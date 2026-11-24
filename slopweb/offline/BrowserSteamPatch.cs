using Mono.Cecil;

// Steam startup is disabled by CoreBootstrap. Its retained dependency must not
// register reverse P/Invoke wrappers for desktop-only callbacks during WASM AOT.
internal static class BrowserSteamPatch
{
    internal static int RemoveCallbackMarkers(ModuleDefinition module)
    {
        var removed = 0;
        foreach (var type in module.GetTypes())
        foreach (var method in type.Methods)
        {
            for (var i = method.CustomAttributes.Count - 1; i >= 0; i--)
            {
                if (method.CustomAttributes[i].AttributeType.Name != "MonoPInvokeCallbackAttribute")
                    continue;
                method.CustomAttributes.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }

    internal static void Apply(string root, IAssemblyResolver resolver)
    {
        const string filename = "com.rlabrecque.steamworks.net.dll";
        var path = Path.Combine(root, "game", filename);
        using var assembly = AssemblyDefinition.ReadAssembly(
            path,
            new ReaderParameters { InMemory = true, AssemblyResolver = resolver }
        );
        var removed = RemoveCallbackMarkers(assembly.MainModule);
        StagedAssembly.Write(assembly, path);
        StagedAssembly.RecordAdaptations(
            root,
            $"Desktop Steam disabled: removed {removed} native callback markers"
        );
    }
}
