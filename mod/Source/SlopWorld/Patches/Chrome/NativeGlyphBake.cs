using System;
using System.Collections.Generic;
using System.Reflection;
using Brutal.ImGuiApi;
using HarmonyLib;

namespace SlopWorld
{
    [HarmonyPatch]
    static class Patch_NativeGlyphBake
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("RimWorldWaylith.Imgui.ImGuiTextMeshProBackend"),
                "BakeGlyph"
            );

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var lookup = AccessTools.Method(
                typeof(ImFontBakedExtensions),
                nameof(ImFontBakedExtensions.FindGlyphNoFallback)
            );
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(lookup))
                {
                    instruction.operand = AccessTools.Method(
                        typeof(Patch_NativeGlyphBake),
                        nameof(Lookup)
                    );
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1)
                throw new InvalidOperationException("Unexpected native font bake lookup");
        }

        static unsafe ImFontGlyphPtr Lookup(ImFontBakedPtr baked, uint codepoint)
        {
            var glyph = baked.FindGlyphNoFallback(codepoint);
            if (glyph.IsNull())
                return default;

            // A lookup may grow the glyph vector. Validate against its storage after
            // lookup, before BakeGlyph reads X0. A miss uses the game's fallback path.
            ref var storage = ref baked.Glyphs;
            var start = (nuint)storage.DataRaw;
            var offset = unchecked((nuint)glyph.Ptr - start);
            return
                start != 0
                && storage.Count > 0
                && storage.Count <= storage.Capacity
                && offset % (nuint)sizeof(ImFontGlyph) == 0
                && offset / (nuint)sizeof(ImFontGlyph) < (nuint)storage.Count
                ? glyph
                : default;
        }
    }
}
