using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserHttpPatch
{
    internal static void Apply(AssemblyDefinition mod, string root)
    {
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var owner = adapters.MainModule.GetType("BrowserAdapters.BrowserTransport");
        var ctor = mod
            .MainModule.GetType("SlopWorld.DaemonHttpTransport")
            .Methods.Single(m => m.IsConstructor && !m.IsStatic);
        var instructions = ctor.Body.Instructions;
        var first = instructions.Single(i =>
            i.OpCode == OpCodes.Newobj
            && i.Operand is MethodReference m
            && m.DeclaringType.FullName == "System.Net.Http.SocketsHttpHandler"
        );
        var last = instructions.Single(i =>
            i.OpCode == OpCodes.Newobj
            && i.Operand is MethodReference m
            && m.DeclaringType.FullName == "System.Net.Http.HttpClient"
        );
        var from = instructions.IndexOf(first);
        var to = instructions.IndexOf(last);
        if (to <= from)
            throw new InvalidOperationException("HTTP constructor pattern changed");
        // Replace only handler/client construction. Retain timeout, request admission,
        // deadlines, protobuf validation, and main-thread completion delivery.
        for (var i = to; i >= from; i--)
            instructions.RemoveAt(i);
        instructions.Insert(
            from,
            Instruction.Create(
                OpCodes.Call,
                mod.MainModule.ImportReference(owner.Methods.Single(m => m.Name == "CreateHttp"))
            )
        );
        var baseUrl = mod
            .MainModule.GetType("SlopWorld.ConnectionInfo")
            .Methods.Single(m => m.Name == "get_BaseUrl");
        baseUrl.Body = new MethodBody(baseUrl);
        baseUrl.Body.Instructions.Add(
            Instruction.Create(
                OpCodes.Call,
                mod.MainModule.ImportReference(owner.Methods.Single(m => m.Name == "get_BaseUrl"))
            )
        );
        baseUrl.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }
}
