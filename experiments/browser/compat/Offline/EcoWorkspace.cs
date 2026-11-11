using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        // Browser viewport configuration already supplies physical pixels and
        // the complete CSS-to-physical scale. Desktop DPI/minimum-resolution
        // inference would apply a second scale and shrink small browser windows.
        var resolveScale = game
            .MainModule.GetType("RimWorld.ResolutionUtility")
            .Methods.Single(m => m.Name == "ResolveUIScale");
        if (
            !resolveScale.IsStatic
            || resolveScale.Parameters.Count != 1
            || resolveScale.Parameters[0].ParameterType.FullName != "System.Single"
            || resolveScale.ReturnType.FullName != "System.Single"
        )
            throw new InvalidOperationException("Browser UI scale boundary changed");
        resolveScale.Body = new MethodBody(resolveScale);
        resolveScale.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        resolveScale.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        BrowserHttpPatch.Apply(mod, root);
        BrowserSocketPatch.Apply(mod, root);
        BrowserSidebarPatch.Apply(mod, root);
        BrowserKeysPatch.Apply(mod, root);
        BrowserKeyBindingsPatch.Apply(game, root);
        BrowserClipboardPatch.Apply(mod, root);
        BrowserCursorPatch.Apply(mod, root);
        BrowserTerminalSizePatch.Apply(mod, root);
        BrowserPreferencesPatch.Apply(mod, root);
        using (var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll")))
            BrowserClipboardPatch.Redirect(
                mod.MainModule.GetType("SlopWorld.FontconfigFonts"),
                adapters.MainModule.GetType("BrowserAdapters.BrowserFontBank"),
                "Families",
                "Families"
            );
        // The browser has no audio decoder. MusicUpdate otherwise throws from
        // Root_Play.Update and prevents a clean workspace frame. Keep this silence
        // policy isolated to Eco staging, with the rest of Root_Play untouched.
        var music = game
            .MainModule.GetType("RimWorld.MusicManagerPlay")
            .Methods.Single(m => m.Name == "MusicUpdate");
        music.Body = new MethodBody(music);
        music.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var hooks = new List<string>();
        BrowserOptionsPatch.Apply(game, mod, hooks);
        TypeDefinition ModType(string name) =>
            mod.MainModule.GetType(name)
            ?? throw new InvalidOperationException("Missing mod type " + name);
        void Prefix(
            string targetType,
            string targetName,
            string prefixType,
            bool instance,
            bool conditional
        )
        {
            var target = game
                .MainModule.GetType(targetType)
                .Methods.Single(m => m.Name == targetName);
            var prefix = ModType(prefixType).Methods.Single(m => m.Name == "Prefix");
            if (
                target.ReturnType.FullName != "System.Void"
                || !target.HasBody
                || prefix.Parameters.Count != (instance ? 1 : 0)
                || !prefix.IsStatic
                || prefix.ReturnType.FullName != (conditional ? "System.Boolean" : "System.Void")
            )
                throw new InvalidOperationException(
                    "Eco hook signature changed: " + target.FullName
                );
            if (
                target.Body.Instructions.Any(i =>
                    i.Operand is MethodReference m && m.DeclaringType.FullName == prefixType
                )
            )
                throw new InvalidOperationException("Duplicate Eco hook");
            prefix.IsPublic = true;
            var type = prefix.DeclaringType;
            while (type.IsNested)
            {
                type.IsNestedPublic = true;
                type = type.DeclaringType;
            }
            type.IsPublic = true;
            var il = target.Body.GetILProcessor();
            var first = target.Body.Instructions[0];
            if (instance)
                il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(
                first,
                il.Create(OpCodes.Call, game.MainModule.ImportReference(prefix))
            );
            if (conditional)
            {
                il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
                il.InsertBefore(first, il.Create(OpCodes.Ret));
            }
            hooks.Add(target.FullName + " <- " + prefix.FullName);
        }
        Prefix(
            "Verse.TickManager",
            "TickManagerUpdate",
            "SlopWorld.Patch_EcoTickBoundary",
            true,
            false
        );
        Prefix(
            "RimWorld.MapInterface",
            "HandleMapClicks",
            "SlopWorld.Eco/Patch_MapClicks",
            false,
            true
        );
        Prefix(
            "RimWorld.MapInterface",
            "MapInterfaceUpdate",
            "SlopWorld.Eco/Patch_WorldSpace",
            false,
            true
        );
        Prefix("Verse.Pawn", "DrawGUIOverlay", "SlopWorld.Eco/Patch_PawnLabels", false, true);
        Prefix(
            "Verse.MapDrawer",
            "DrawMapMesh",
            "SlopWorld.PaneOverDraw/Patch_MapMesh",
            true,
            true
        );
        Prefix(
            "Verse.DynamicDrawManager",
            "DrawDynamicThings",
            "SlopWorld.PaneOverDraw/Patch_DynamicThings",
            false,
            true
        );
        Prefix(
            "RimWorld.Selector",
            "HandleMapClicks",
            "SlopWorld.EcoMapInput/Selection",
            true,
            true
        );
        // The browser owns startup integration. Keep the real ModEntry constructor
        // and profile settings load; bypass only its desktop startup installer.
        var early = ModType("SlopWorld.EarlyBootstrap").Methods.Single(m => m.Name == "Install");
        early.Body = new MethodBody(early);
        early.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        Directory.CreateDirectory(Path.Combine(root, "mod"));
        mod.Write(Path.Combine(root, "mod", "SlopWorld.dll"));
        game.Write(targetPath + ".partial");
        File.Move(targetPath + ".partial", targetPath, true);
        var manifestPath = Path.Combine(root, "game", "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsArray();
        manifest.Single(n => n!["file"]!.GetValue<string>() == "Assembly-CSharp.dll")![
            "stagedSha256"
        ] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(targetPath)));
        File.WriteAllText(manifestPath, manifest.ToJsonString());
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
