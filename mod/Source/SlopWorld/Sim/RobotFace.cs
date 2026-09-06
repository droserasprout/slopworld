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

        // Each variant is a separate texture set (south + east) so the color lives in the
        // PNG rather than in a shader parameter. RimWorld's texture loader caches by path, so
        // the per-pawn texPath is the only thing that changes.
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

        // More common colors are weighted higher, so the colony's default palette reads as
        // working machines. Missing is rare: a story beat rather than an everyday look.
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

        // No _north: a faceplate has no back, and the node below hides on that facing. _west is
        // Graphic_Multi's mirror of _east.

        // These scalp cuts are rerolled so every agent keeps a visible hair silhouette.
        static readonly HashSet<string> ScalpHair =
            new HashSet<string> { "Bald", "Shaved", "Mohawk" };

        const float WildHairChance = 0.35f;
        static readonly Color[] WildHairColors =
        {
            new Color(0.95f, 0.12f, 0.55f), // hot pink
            new Color(0.10f, 0.55f, 1.00f), // electric blue
            new Color(0.35f, 0.95f, 0.15f), // neon green
            new Color(0.65f, 0.20f, 1.00f), // vivid purple
            new Color(1.00f, 0.35f, 0.05f), // orange
            new Color(0.05f, 0.90f, 0.85f), // cyan
        };

        // Once at generation rather than from the reconcile: nothing takes an agent's hair away
        // later, and a pawn whose every option is refused would be rerolled forever.
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
            // Out of tries: a scalp is better than the null hair a blank would be.
        }

        public static void RandomizeHairColor(Pawn pawn)
        {
            if (pawn?.story == null) return;

            pawn.story.HairColor = Rand.Chance(WildHairChance)
                ? WildHairColors.RandomElement()
                : PawnHairColors.RandomHairColor(
                    pawn, pawn.story.SkinColor, pawn.ageTracker.AgeBiologicalYears);
        }

        // Assigns an eye color to the pawn, once. Called at generation time; the color is
        // stable for the pawn's life.
        public static void Assign(Pawn pawn)
        {
            if (pawn == null) return;
            if (_eyeColors.ContainsKey(pawn.thingIDNumber)) return;

            var color = ColorWeights.RandomElementByWeight(p => p.Weight).Color;
            _eyeColors[pawn.thingIDNumber] = color;
        }

        // Give an existing agent a new face. Hair keeps the same no-scalp rule as generation,
        // and one dirty rebuild picks up the plate texture, cut and color together.
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

        // The one thing that would draw over the plate - beards hang on a render node
        // above the head, hair does not.
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
                // AttachmentHead takes its mesh from GetHumanlikeHairSetForPawn, the mesh
                // vanilla hair is drawn on, so the plate lands in the same frame as the hair
                // for this head type and needs no size of its own.
                nodeClass = typeof(PawnRenderNode_AttachmentHead),
                texPath = RobotFace.TexPathFor(pawn),
                parentTagDef = PawnRenderNodeTagDefOf.Head,
                // The plate arrives painted; on the skin shader it would change color with
                // the pawn under it.
                shaderTypeDef = ShaderTypeDefOf.Cutout,
                colorType = PawnRenderNodeProperties.AttachmentColorType.Custom,
                color = Color.white,
                // Read off the head node rather than written down: layers are absolute floats
                // out of the humanlike render tree def. Half a layer, so the plate cannot tie
                // with whatever the tree puts above.
                baseLayer = head.Props.baseLayer + 0.5f,
                // Leaving north out shows the pawn's own head there rather than a plate on the
                // back of it.
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
