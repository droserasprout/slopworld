// Only TopBar's presentation entry points admit the colony-free workspace.
// Map presence/arrival and CoreTip simulation contracts remain with the mod.
using Mono.Cecil;

internal static class BrowserWorkspaceDoorsPatch
{
    internal static void Apply(AssemblyDefinition mod, string root)
    {
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var helper = adapters.MainModule.GetType("BrowserAdapters.BrowserWorkspaceDoors");
        var top = mod.MainModule.GetType("SlopWorld.TopBar");
        foreach (
            var (owner, name, replacement) in new[]
            {
                ("SlopWorld.CoreTip", "On", "Available"),
                ("SlopWorld.Jukebox", "On", "Available"),
                ("SlopWorld.CoreTip", "OpenMenu", "OpenCore"),
            }
        )
        {
            var call = top
                .Methods.Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Single(i =>
                    i.Operand is MethodReference target
                    && target.DeclaringType.FullName == owner
                    && target.Name == name
                );
            var original = (MethodReference)call.Operand;
            var target = helper.Methods.Single(m => m.Name == replacement);
            if (
                original.ReturnType.FullName != target.ReturnType.FullName
                || !original
                    .Parameters.Select(p => p.ParameterType.FullName)
                    .SequenceEqual(target.Parameters.Select(p => p.ParameterType.FullName))
            )
                throw new InvalidOperationException("Workspace door contract changed");
            call.Operand = mod.MainModule.ImportReference(target);
        }
    }
}
