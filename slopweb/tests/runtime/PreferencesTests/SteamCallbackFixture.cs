using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class SteamCallbackFixture
{
    internal static void Check()
    {
        using var module = ModuleDefinition.CreateModule("SteamFixture", ModuleKind.Dll);
        var owner = new TypeDefinition("Steamworks", "Callbacks", TypeAttributes.Public);
        module.Types.Add(owner);
        var nested = new TypeDefinition("", "Nested", TypeAttributes.NestedPrivate);
        owner.NestedTypes.Add(nested);
        foreach (var type in new[] { owner, nested })
        {
            var method = new MethodDefinition(
                "InternalOnServerResponded",
                MethodAttributes.Static,
                module.TypeSystem.Void
            );
            type.Methods.Add(method);
            method.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            foreach (var name in new[] { "MonoPInvokeCallbackAttribute", "PreserveAttribute" })
            {
                var attribute = new TypeReference("Steamworks", name, module, module);
                var constructor = new MethodReference(".ctor", module.TypeSystem.Void, attribute)
                {
                    HasThis = true,
                };
                method.CustomAttributes.Add(new CustomAttribute(constructor));
            }
        }
        if (BrowserSteamPatch.RemoveCallbackMarkers(module) != 2)
            throw new Exception("Desktop callback markers must be removed, including nested types");
        using var bytes = new MemoryStream();
        module.Write(bytes);
        bytes.Position = 0;
        using var patched = ModuleDefinition.ReadModule(bytes);
        foreach (var method in patched.GetTypes().SelectMany(t => t.Methods))
        {
            if (
                method.CustomAttributes.Single().AttributeType.Name != "PreserveAttribute"
                || method.Parameters.Single().ParameterType.FullName != "System.Object"
                || method.Body.Instructions.Single().OpCode != OpCodes.Ret
            )
                throw new Exception(
                    "Callback stripping must preserve other metadata and method bodies"
                );
        }
        if (BrowserSteamPatch.RemoveCallbackMarkers(patched) != 0)
            throw new Exception("Callback stripping must be repeatable");
    }
}
