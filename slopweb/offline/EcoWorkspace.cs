using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Explicit Eco hooks only. No runtime detours and no blanket Harmony suppression.
internal static class EcoWorkspace
{
    internal static void Apply(string root, string modPath)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.Combine(root, "game"));
        resolver.AddSearchDirectory(Path.GetDirectoryName(modPath));
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
        var options = new ReaderParameters { InMemory = true, AssemblyResolver = resolver };
        var targetPath = Path.Combine(root, "game", "Assembly-CSharp.dll");
        using var game = AssemblyDefinition.ReadAssembly(targetPath, options);
        using var mod = AssemblyDefinition.ReadAssembly(modPath, options);
        BrowserScalePatch.Apply(game);
        BrowserKeyBindingsPatch.Apply(game, root);
        // Native Unity audio is disabled: MusicUpdate would throw from Root_Play.Update.
        // BrowserAudio owns authored OST decoding through the DOM transport; keep
        // this native-manager suppression isolated from the rest of Root_Play.
        var music = game
            .MainModule.GetType("RimWorld.MusicManagerPlay")
            .Methods.Single(m => m.Name == "MusicUpdate");
        if (
            music.IsStatic
            || !music.HasBody
            || music.Parameters.Count != 0
            || music.ReturnType.MetadataType != MetadataType.Void
        )
            throw new InvalidOperationException(
                "Expected instance void RimWorld.MusicManagerPlay.MusicUpdate()"
            );
        music.Body = new MethodBody(music);
        music.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var hooks = new List<string>();
        BrowserOptionsPatch.Apply(game, mod, hooks);
        EcoPrefixes.Apply(game, mod, hooks);
        BrowserActionButtonsPatch.Apply(game, mod, hooks);
        Directory.CreateDirectory(Path.Combine(root, "mod"));
        mod.Write(Path.Combine(root, "mod", "SlopWorld.dll"));
        StagedAssembly.Write(game, targetPath);
        File.WriteAllText(
            Path.Combine(root, "mod", "eco.json"),
            JsonSerializer.Serialize(
                new
                {
                    hooks,
                    desktopStartupSkipped = true,
                    fullBootstrap = false,
                }
            )
        );
    }
}
