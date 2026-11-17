using Mono.Cecil;
using Mono.Cecil.Cil;

static class DialogsPatchFixture
{
    public static void Check()
    {
        using var game = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Game", new Version(1, 0)),
            "Game",
            ModuleKind.Dll
        );
        using var mod = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Mod", new Version(1, 0)),
            "Mod",
            ModuleKind.Dll
        );
        var window = new TypeDefinition(
            "Verse",
            "Window",
            TypeAttributes.Public,
            game.MainModule.TypeSystem.Object
        );
        var stack = new TypeDefinition(
            "Verse",
            "WindowStack",
            TypeAttributes.Public,
            game.MainModule.TypeSystem.Object
        );
        game.MainModule.Types.Add(window);
        game.MainModule.Types.Add(stack);
        var add = new MethodDefinition(
            "Add",
            MethodAttributes.Public,
            game.MainModule.TypeSystem.Void
        );
        add.Parameters.Add(new ParameterDefinition(window));
        stack.Methods.Add(add);
        var original = Instruction.Create(OpCodes.Ret);
        add.Body.Instructions.Add(original);
        var policy = new TypeDefinition(
            "SlopWorld",
            "Patch_DialogsOverChrome",
            TypeAttributes.Public,
            mod.MainModule.TypeSystem.Object
        );
        mod.MainModule.Types.Add(policy);
        var prefix = new MethodDefinition(
            "Prefix",
            MethodAttributes.Private | MethodAttributes.Static,
            mod.MainModule.TypeSystem.Void
        );
        prefix.Parameters.Add(new ParameterDefinition(mod.MainModule.ImportReference(window)));
        policy.Methods.Add(prefix);
        prefix.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var hooks = new List<string>();
        BrowserDialogsPatch.Apply(game, mod, hooks);
        if (
            !prefix.IsPublic
            || hooks.Count != 1
            || add.Body.Instructions.Count != 3
            || add.Body.Instructions[0].OpCode != OpCodes.Ldarg_1
            || add.Body.Instructions[1].Operand is not MethodReference called
            || called.Name != "Prefix"
            || !ReferenceEquals(add.Body.Instructions[2], original)
        )
            throw new Exception("Dialog promotion must run before native stack insertion");
        add.Parameters.Clear();
        try
        {
            BrowserDialogsPatch.Apply(game, mod, hooks);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new Exception("Changed dialog signature was accepted");
    }
}
