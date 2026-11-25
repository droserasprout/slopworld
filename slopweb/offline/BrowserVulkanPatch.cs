using Mono.Cecil;
using Mono.Cecil.Cil;

// Mono specializes this generic desktop command even in mixed AOT. Its indirect
// Vulkan call requires a native wrapper the browser linker cannot resolve.
internal static class BrowserVulkanPatch
{
    internal static void Apply(string root, IAssemblyResolver resolver)
    {
        var path = Path.Combine(root, "game", "Brutal.Vulkan.Abstractions.dll");
        using var assembly = AssemblyDefinition.ReadAssembly(
            path,
            new ReaderParameters { InMemory = true, AssemblyResolver = resolver }
        );
        var module = assembly.MainModule;
        var method = module
            .GetType("Brutal.VulkanApi.Abstractions.CommandBufferExtensions")
            .Methods.Single(m =>
                m.Name == "PushConstants"
                && m.HasGenericParameters
                && m.HasBody
                && m.Body.Instructions.Any(i => i.OpCode == OpCodes.Calli)
            );
        method.Body = new MethodBody(method);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldstr, "Vulkan command buffers are unavailable in the browser.");
        il.Emit(
            OpCodes.Newobj,
            module.ImportReference(
                typeof(PlatformNotSupportedException).GetConstructor([typeof(string)])
            )
        );
        il.Emit(OpCodes.Throw);
        StagedAssembly.Write(assembly, path);
        StagedAssembly.RecordAdaptations(root, "Desktop Vulkan push constants are unavailable");
    }
}
