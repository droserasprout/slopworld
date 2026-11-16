using Mono.Cecil;
using Mono.Cecil.Cil;

static class ScalePatchFixture
{
    public static void Check()
    {
        foreach (bool preference in new[] { false, true })
        {
            using var game = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("ScaleFixture", new Version(1, 0)),
                "ScaleFixture",
                ModuleKind.Dll
            );
            var module = game.MainModule;
            var resolution = new TypeDefinition(
                "RimWorld",
                "ResolutionUtility",
                TypeAttributes.Public,
                module.TypeSystem.Object
            );
            module.Types.Add(resolution);
            var policy = new MethodDefinition(
                preference ? "get_DpiScale" : "ResolveUIScale",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Single
            );
            if (!preference)
                policy.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
            policy.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R4, 2f));
            policy.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            resolution.Methods.Add(policy);
            var prefs = new TypeDefinition(
                "Verse",
                "Prefs",
                TypeAttributes.Public,
                module.TypeSystem.Object
            );
            module.Types.Add(prefs);
            var getter = new MethodDefinition(
                "get_UIScale",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Single
            );
            getter.Body.Instructions.Add(Instruction.Create(OpCodes.Call, policy));
            getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            prefs.Methods.Add(getter);
            var native = new MethodDefinition(
                "set_UIScale",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void
            );
            native.Parameters.Add(new ParameterDefinition(module.TypeSystem.Single));
            native.Body.Instructions.Add(Instruction.Create(OpCodes.Call, policy));
            native.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
            native.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            prefs.Methods.Add(native);
            var property = new PropertyDefinition(
                "UIScale",
                PropertyAttributes.None,
                module.TypeSystem.Single
            )
            {
                GetMethod = getter,
                SetMethod = native,
            };
            prefs.Properties.Add(property);
            BrowserScalePatch.Apply(game);
            if (
                policy.Body.Instructions[0].OpCode
                != (preference ? OpCodes.Ldc_R4 : OpCodes.Ldarg_0)
            )
                throw new Exception("Browser scale still applies desktop policy");
            if (!preference)
                continue;
            if (!Equals(policy.Body.Instructions[0].Operand, 1f))
                throw new Exception("Browser DPI was applied twice");
            if (property.SetMethod != native || native.Body.Instructions[0].Operand != policy)
                throw new Exception("Scale patch changed the native preference accessor");
            native.Body.Instructions.Clear();
            native.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            try
            {
                BrowserScalePatch.Apply(game);
                throw new Exception("Unknown scale accessor accepted");
            }
            catch (InvalidOperationException) { }
        }
    }
}
