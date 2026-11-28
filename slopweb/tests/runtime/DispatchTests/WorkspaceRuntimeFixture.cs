using System.Reflection;
using System.Reflection.Emit;
using SlopWeb.Runtime;

// Load a synthetic Adapters assembly only after exercising Probe's early-call path.
// Unused interface members throw so a forwarding error cannot pass silently.
static class WorkspaceRuntimeFixture
{
    internal static void Check()
    {
        try
        {
            Probe.WorkspacePacketCache(true);
            throw new Exception("Expected an unstaged workspace runtime");
        }
        catch (FileNotFoundException) { }

        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Adapters"),
            AssemblyBuilderAccess.Run
        );
        var type = assembly
            .DefineDynamicModule("Adapters")
            .DefineType(
                "BrowserAdapters.WorkspaceRuntime",
                TypeAttributes.Public | TypeAttributes.Sealed
            );
        type.AddInterfaceImplementation(typeof(IWorkspaceRuntime));
        var enabled = type.DefineField("enabled", typeof(bool), FieldAttributes.Private);
        foreach (var contract in typeof(IWorkspaceRuntime).GetMethods())
        {
            var method = type.DefineMethod(
                contract.Name,
                MethodAttributes.Public | MethodAttributes.Virtual,
                contract.ReturnType,
                contract.GetParameters().Select(parameter => parameter.ParameterType).ToArray()
            );
            var il = method.GetILGenerator();
            if (contract.Name == nameof(IWorkspaceRuntime.WorkspacePacketCache))
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, enabled);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, enabled);
                il.Emit(OpCodes.Ret);
            }
            else
            {
                il.Emit(
                    OpCodes.Newobj,
                    typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!
                );
                il.Emit(OpCodes.Throw);
            }
            type.DefineMethodOverride(method, contract);
        }
        type.CreateType();
        ResolveEventHandler resolver = (_, request) =>
            new AssemblyName(request.Name).Name == "Adapters" ? assembly : null;
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            if (Probe.WorkspacePacketCache(true) || !Probe.WorkspacePacketCache(false))
                throw new Exception(
                    "Workspace exports lost runtime state or forwarded stale arguments"
                );
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }
}
