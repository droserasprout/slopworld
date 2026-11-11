// Adapt only native font access. Keep the installed layout, parser, wrapping and
// caret algorithms intact; reject changed boundaries instead of guessing IL.
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserTextLayoutPatch
{
    internal const string Description =
        "Installed TextLayout retains parsing, wrapping and caret logic; browser-owned instances obtain glyph advances and line metrics from local font bakes";

    internal static bool Apply(AssemblyDefinition target, string root)
    {
        var module = target.MainModule;
        var layout = module.GetType("RimWorldWaylith.Imgui.TextLayout");
        if (layout.Methods.Any(m => m.Name == "BrowserMetricsInstalled"))
            return false;
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var helper = adapters.MainModule.GetType("BrowserAdapters.BrowserNativeLayout");
        MethodReference Import(string name) =>
            module.ImportReference(helper.Methods.Single(m => m.Name == name));
        var glyphs = layout.NestedTypes.Single(t => t.Name == "GlyphAdvances");
        var create = layout.Methods.Single(m => m.Name == "AdvancesFor" && m.Parameters.Count == 3);
        var point = glyphs.Methods.Single(m => m.Name == "CodePoint" && m.Parameters.Count == 3);
        if (
            create.ReturnType.FullName != glyphs.FullName
            || point.ReturnType.FullName != "System.Int32"
            || point.Parameters[2].ParameterType.FullName != "System.Single&"
        )
            throw new InvalidOperationException("Native glyph advance contract changed");
        void Prefix(MethodDefinition method, string guard, string callback, bool scale)
        {
            var il = method.Body.GetILProcessor();
            var first = method.Body.Instructions[0];
            void Add(Instruction instruction) => il.InsertBefore(first, instruction);
            Add(il.Create(OpCodes.Ldarg_0));
            Add(il.Create(OpCodes.Call, Import(guard)));
            Add(il.Create(OpCodes.Brfalse, first));
            Add(il.Create(OpCodes.Ldarg_0));
            Add(il.Create(OpCodes.Ldarg_1));
            Add(il.Create(OpCodes.Ldarg_2));
            Add(il.Create(OpCodes.Ldarg_3));
            if (scale)
            {
                Add(il.Create(OpCodes.Ldarg_0));
                Add(il.Create(OpCodes.Call, layout.Methods.Single(m => m.Name == "get_Scale")));
            }
            Add(il.Create(OpCodes.Call, Import(callback)));
            if (scale)
                Add(il.Create(OpCodes.Castclass, glyphs));
            Add(il.Create(OpCodes.Ret));
        }
        Prefix(create, "OwnsLayout", "CreateAdvances", true);
        Prefix(point, "OwnsRun", "CodePoint", false);
        int hosts = 0,
            pixels = 0,
            metrics = 0;
        foreach (var method in layout.Methods.Where(m => m.HasBody && m != create))
        foreach (var instruction in method.Body.Instructions.ToArray())
        {
            if (
                instruction.OpCode == OpCodes.Ldfld
                && instruction.Operand is FieldReference field
                && field.DeclaringType.FullName == layout.FullName
                && field.Name == "host"
            )
            {
                // Leave the layout itself on the stack as the adapter context.
                instruction.OpCode = OpCodes.Nop;
                instruction.Operand = null;
                hosts++;
            }
            if (
                instruction.Operand is not MethodReference call
                || call.DeclaringType.FullName != "RimWorldWaylith.Imgui.ImGuiHost"
            )
                continue;
            if (call.Name == "PixelSizeFor")
            {
                instruction.OpCode = OpCodes.Call;
                instruction.Operand = Import("PixelSize");
                pixels++;
            }
            else if (call.Name == "LineMetricsFor")
            {
                instruction.OpCode = OpCodes.Call;
                instruction.Operand = Import("LineMetrics");
                metrics++;
                method
                    .Body.GetILProcessor()
                    .InsertAfter(
                        instruction,
                        Instruction.Create(OpCodes.Unbox_Any, call.ReturnType)
                    );
            }
            else
                throw new InvalidOperationException(
                    "Unexpected native layout host access: " + call.FullName
                );
        }
        if (hosts != 2 || pixels != 1 || metrics != 1)
            throw new InvalidOperationException("Native layout metrics boundaries changed");
        var marker = new MethodDefinition(
            "BrowserMetricsInstalled",
            MethodAttributes.Public | MethodAttributes.Static,
            module.TypeSystem.Boolean
        );
        marker.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        marker.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        layout.Methods.Add(marker);
        return true;
    }

    internal static void Refresh(string root)
    {
        var path = Path.Combine(root, "game", "RimWorld.dll");
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.Combine(root, "game"));
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
        using var target = AssemblyDefinition.ReadAssembly(
            path,
            new ReaderParameters { InMemory = true, AssemblyResolver = resolver }
        );
        if (Apply(target, root))
        {
            target.Write(path + ".partial");
            File.Move(path + ".partial", path, true);
            var manifestPath = Path.Combine(root, "game", "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsArray();
            manifest.Single(n => n!["file"]!.GetValue<string>() == "RimWorld.dll")![
                "stagedSha256"
            ] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            File.WriteAllText(manifestPath, manifest.ToJsonString());
        }
        var adaptationsPath = Path.Combine(root, "game", "adaptations.json");
        var adaptations = JsonNode.Parse(File.ReadAllText(adaptationsPath))!.AsArray();
        if (!adaptations.Any(n => n?.GetValue<string>() == Description))
            adaptations.Add(Description);
        File.WriteAllText(adaptationsPath, adaptations.ToJsonString());
    }
}
