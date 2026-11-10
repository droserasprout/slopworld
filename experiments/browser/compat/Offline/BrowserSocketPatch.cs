using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserSocketPatch
{
    internal static void Apply(AssemblyDefinition mod, string root)
    {
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var owner = adapters.MainModule.GetType("BrowserAdapters.BrowserSocket");
        void Replace(string type, string method, string replacement)
        {
            var target = mod.MainModule.GetType(type).Methods.Single(m => m.Name == method);
            var adapter = owner.Methods.Single(m => m.Name == replacement);
            if (
                target.IsStatic
                || adapter.Parameters.Count != target.Parameters.Count + 1
                || target.ReturnType.FullName != adapter.ReturnType.FullName
            )
                throw new InvalidOperationException("Socket adapter signature changed");
            target.Body = new MethodBody(target);
            var il = target.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            foreach (var parameter in target.Parameters)
                il.Emit(OpCodes.Ldarg, parameter);
            il.Emit(OpCodes.Call, mod.MainModule.ImportReference(adapter));
            il.Emit(OpCodes.Ret);
        }
        Replace("SlopWorld.HubTransport", "OpenSocket", "Open");
        Replace("SlopWorld.MiniWebSocket", "SendBinary", "Send");
        Replace("SlopWorld.MiniWebSocket", "FlushOutgoing", "Flush");
        Replace("SlopWorld.MiniWebSocket", "Dispose", "Dispose");
    }
}
