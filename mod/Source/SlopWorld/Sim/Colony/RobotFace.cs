using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The plate is drawn over the vanilla head, while the agent skin override keeps exposed
    // areas under hair and apparel metallic. tools/roboface.py has the geometry.
    public static class RobotFace
    {
        static readonly Color MetalSkinColor = new Color(0.42f, 0.435f, 0.46f);

        // Each variant has south and east textures with color in the PNG files.
        // RimWorld caches textures by path. Select each pawn's variant through texPath.
        public enum EyeColor
        {
            Blue,
            Red,
            Green,
            Purple,
            Yellow,
            White,
            Missing, // two void holes where the eyes would be
        }

        // TexPath prefix for each variant. RimWorld appends _south, _east etc.
        static readonly Dictionary<EyeColor, string> TexPaths = new Dictionary<EyeColor, string>
        {
            { EyeColor.Blue,    "SlopWorld/RobotFace_Blue" },
            { EyeColor.Red,     "SlopWorld/RobotFace_Red" },
            { EyeColor.Green,   "SlopWorld/RobotFace_Green" },
            { EyeColor.Purple,  "SlopWorld/RobotFace_Purple" },
            { EyeColor.Yellow,  "SlopWorld/RobotFace_Yellow" },
            { EyeColor.White,   "SlopWorld/RobotFace_White" },
            { EyeColor.Missing, "SlopWorld/RobotFace_Missing" },
        };

        // Give common eye colors greater selection weights.
        // Keep the Missing variant rare.
        struct Weighted { public EyeColor Color; public float Weight; }
        static readonly Weighted[] ColorWeights =
        {
            new Weighted { Color = EyeColor.Blue,    Weight = 30f },
            new Weighted { Color = EyeColor.Red,     Weight = 15f },
            new Weighted { Color = EyeColor.Green,   Weight = 15f },
            new Weighted { Color = EyeColor.Purple,  Weight = 12f },
            new Weighted { Color = EyeColor.Yellow,  Weight = 10f },
            new Weighted { Color = EyeColor.White,   Weight =  8f },
            new Weighted { Color = EyeColor.Missing, Weight = 10f },
 };

        // Per-pawn assignment, stable across saves for the life of the pawn. Keyed on
        // thingIDNumber because it survives a spawn cycle.
        static readonly Dictionary<int, EyeColor> _eyeColors = new Dictionary<int, EyeColor>();

        // Omit _north because the faceplate node is hidden when facing north.
        // Graphic_Multi mirrors _east for the west view.

        // These scalp cuts are rerolled so every agent keeps a visible hair silhouette.
        static readonly HashSet<string> ScalpHair =
            new HashSet<string> { "Bald", "Shaved", "Mohawk" };

        // Select hair during generation instead of reconciliation.
        // Otherwise, a pawn with no acceptable style could trigger repeated selection attempts indefinitely.
        public static void FitHair(Pawn pawn)
        {
            if (pawn?.story == null) return;
            if (!ScalpHair.Contains(pawn.story.hairDef?.defName ?? "")) return;

            for (var i = 0; i < 8; i++)
            {
                var hair = PawnStyleItemChooser.RandomHairFor(pawn);
                if (hair == null || ScalpHair.Contains(hair.defName)) continue;
                pawn.story.hairDef = hair;
                return;
            }
            // Keep the last style after exhausting attempts so hair remains non-null.
        }

        public static void RandomizeHairColor(Pawn pawn)
        {
            if (pawn?.story == null) return;

            // Use the active ANSI palette for hair color.
            // Theme changes then affect new agents and explicit appearance changes.
            pawn.story.HairColor = TerminalTheme.Current.Ansi.RandomElement();
        }

        // Assign an eye color during generation. Keep it until an explicit appearance change.
        public static void Assign(Pawn pawn)
        {
            if (pawn == null) return;
            if (_eyeColors.ContainsKey(pawn.thingIDNumber)) return;

            var color = ColorWeights.RandomElementByWeight(p => p.Weight).Color;
            _eyeColors[pawn.thingIDNumber] = color;
        }

        // Select a new face and hair using the same hair restrictions as generation.
        // Rebuild the render tree once for the new faceplate texture, hairstyle, and color.
        public static void Reroll(Pawn pawn)
        {
            if (pawn?.story == null) return;

            _eyeColors[pawn.thingIDNumber] =
                ColorWeights.RandomElementByWeight(p => p.Weight).Color;

            for (var i = 0; i < 8; i++)
            {
                var hair = PawnStyleItemChooser.RandomHairFor(pawn);
                if (hair == null || ScalpHair.Contains(hair.defName)) continue;
                pawn.story.hairDef = hair;
                break;
            }

            RandomizeHairColor(pawn);
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        // Returns the eye color assigned to this pawn, or the default (Blue) if unassigned.
        public static EyeColor ColorOf(Pawn pawn)
        {
            if (pawn == null) return EyeColor.Blue;
            return _eyeColors.TryGetValue(pawn.thingIDNumber, out var c) ? c : EyeColor.Blue;
        }

        // The texPath for this pawn's eye color, falling back to Blue if unassigned.
        public static string TexPathFor(Pawn pawn)
        {
            var color = ColorOf(pawn);
            return TexPaths.TryGetValue(color, out var path) ? path : TexPaths[EyeColor.Blue];
        }

        // Remove beards because their render nodes would cover the faceplate.
        public static void Apply(Pawn pawn)
        {
            if (pawn?.story == null) return;

            bool dirty = false;
            if (!pawn.story.skinColorOverride.HasValue ||
                pawn.story.skinColorOverride.Value != MetalSkinColor)
            {
                pawn.story.skinColorOverride = MetalSkinColor;
                dirty = true;
            }

            if (pawn.style != null && pawn.style.beardDef != BeardDefOf.NoBeard)
            {
                pawn.style.beardDef = BeardDefOf.NoBeard;
                dirty = true;
            }

            if (dirty) pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }

    // Every non-abstract DynamicPawnRenderNodeSetup is found by AllSubclassesNonAbstract and
    // AllSubclassesNonAbstract instantiates this setup without a def or patch. AddChild resolves
    // the parent through parentTagDef against nodesByTag.
    public class RobotFaceRenderNodes : DynamicPawnRenderNodeSetup
    {
        public override bool HumanlikeOnly => true;

        public override IEnumerable<(PawnRenderNode node, PawnRenderNode parent)>
            GetDynamicNodes(Pawn pawn, PawnRenderTree tree)
        {
            if (!AgentColony.IsAgent(pawn)) yield break;

            var head = HeadNode(tree);
            if (head == null) yield break;

            var props = new PawnRenderNodeProperties
            {
                // AttachmentHead uses the hair mesh from GetHumanlikeHairSetForPawn.
                // This aligns the faceplate with this head type without a separate size setting.
                nodeClass = typeof(PawnRenderNode_AttachmentHead),
                texPath = RobotFace.TexPathFor(pawn),
                parentTagDef = PawnRenderNodeTagDefOf.Head,
                // Preserve texture colors. The skin shader would tint the faceplate with the pawn's skin color.
                shaderTypeDef = ShaderTypeDefOf.Cutout,
                colorType = PawnRenderNodeProperties.AttachmentColorType.Custom,
                color = Color.white,
                // Derive the layer from the head node's absolute layer value.
                // Add half a layer to avoid matching the next layer above it.
                baseLayer = head.Props.baseLayer + 0.5f,
                // Hide the faceplate when facing north to show the back of the pawn's head.
                visibleFacing = new List<Rot4> { Rot4.South, Rot4.East, Rot4.West },
            };

            if (!tree.ShouldAddNodeToTree(props)) yield break;

            yield return (new PawnRenderNode_AttachmentHead(pawn, props, tree), null);
        }

        // nodesByTag is private, so this walks down from the public root instead of
        // binding a field by name.
        static PawnRenderNode HeadNode(PawnRenderTree tree)
        {
            var queue = new Queue<PawnRenderNode>();
            queue.Enqueue(tree.rootNode);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                if (n == null) continue;
                if (n.Props?.tagDef == PawnRenderNodeTagDefOf.Head) return n;
                if (n.children == null) continue;
                foreach (var c in n.children) queue.Enqueue(c);
            }
            return null;
        }
    }
}
