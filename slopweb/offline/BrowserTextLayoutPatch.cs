// Adapt only native font access. Keep the installed layout, parser, wrapping and
// caret algorithms intact; reject changed boundaries instead of guessing IL.
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserTextLayoutPatch
{
    internal const string Description =
        "Installed TextLayout retains parsing, wrapping and caret logic; browser-owned instances obtain glyph advances and line metrics from local font bakes";

    internal static void Apply(AssemblyDefinition target, string root)
    {
        var module = target.MainModule;
        var layout = module.GetType("RimWorldWaylith.Imgui.TextLayout");
        if (layout.Methods.Any(m => m.Name == "BrowserMetricsInstalled"))
            return;
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var helper = adapters.MainModule.GetType("BrowserAdapters.BrowserNativeLayout");
        MethodReference Import(string name) =>
            module.ImportReference(helper.Methods.Single(m => m.Name == name));
        var glyphs = layout.NestedTypes.Single(t => t.Name == "GlyphAdvances");
        var create = layout.Methods.Single(m => m.Name == "AdvancesFor" && m.Parameters.Count == 3);
        var point = glyphs.Methods.Single(m => m.Name == "CodePoint" && m.Parameters.Count == 3);
        if (
            create.IsStatic
            || !create.HasBody
            || point.IsStatic
            || !point.HasBody
            || create.FullName
                != "RimWorldWaylith.Imgui.TextLayout/GlyphAdvances RimWorldWaylith.Imgui.TextLayout::AdvancesFor(System.Int32,System.Single,UnityEngine.FontStyle)"
            || point.FullName
                != "System.Int32 RimWorldWaylith.Imgui.TextLayout/GlyphAdvances::CodePoint(System.String,System.Int32,System.Single&)"
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
            if (
                call.FullName
                == "System.Single RimWorldWaylith.Imgui.ImGuiHost::PixelSizeFor(System.Int32,System.Single)"
            )
            {
                instruction.OpCode = OpCodes.Call;
                instruction.Operand = Import("PixelSize");
                pixels++;
            }
            else if (
                call.FullName
                == "RimWorldWaylith.Imgui.LineMetrics RimWorldWaylith.Imgui.ImGuiHost::LineMetricsFor(System.Int32,System.Single)"
            )
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
    }
}
