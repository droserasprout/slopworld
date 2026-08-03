using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Metal from the hairline down, drawn over the vanilla head rather than replacing it, so
    // the head, its skin colour and its hair are still the game's. tools/roboface.py has the
    // geometry.
    public static class RobotFace
    {
        // Each variant is a separate texture set (south + east) so the colour lives in the
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

        // More common colours are weighted higher, so the colony's default palette reads as
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

        // The plate's edge is a fixed line across the brow, so a cut whose hairline sits above
        // it leaves bare skin. By defName rather than through a DefOf, so a name this game does
        // not have is never matched instead of failing at load.
        static readonly HashSet<string> ScalpHair =
            new HashSet<string> { "Bald", "Shaved", "Mohawk" };

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

        // Assigns an eye colour to the pawn, once. Called at generation time; the colour is
        // stable for the pawn's life.
        public static void Assign(Pawn pawn)
        {
            if (pawn == null) return;
            if (_eyeColors.ContainsKey(pawn.thingIDNumber)) return;

            var color = ColorWeights.RandomElementByWeight(p => p.Weight).Color;
            _eyeColors[pawn.thingIDNumber] = color;
        }

        // Returns the eye colour assigned to this pawn, or the default (Blue) if unassigned.
        public static EyeColor ColorOf(Pawn pawn)
        {
            if (pawn == null) return EyeColor.Blue;
            return _eyeColors.TryGetValue(pawn.thingIDNumber, out var c) ? c : EyeColor.Blue;
        }

        // The texPath for this pawn's eye colour, falling back to Blue if unassigned.
        public static string TexPathFor(Pawn pawn)
        {
            var color = ColorOf(pawn);
            return TexPaths.TryGetValue(color, out var path) ? path : TexPaths[EyeColor.Blue];
        }

        // The one thing that would draw over the plate - beards hang on a render node
        // above the head, hair does not.
        public static void Apply(Pawn pawn)
        {
            if (pawn?.style == null) return;
            if (pawn.style.beardDef == BeardDefOf.NoBeard) return;

            pawn.style.beardDef = BeardDefOf.NoBeard;
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }

    // Every non-abstract DynamicPawnRenderNodeSetup is found by AllSubclassesNonAbstract and
    // instantiated by the game, so this needs no def and no patch. The parent is handed back
    // null: AddChild resolves it from parentTagDef against its own nodesByTag, so we never
    // hold a node the tree has since rebuilt.
    public class SlopFaceRenderNodes : DynamicPawnRenderNodeSetup
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
                // The plate arrives painted; on the skin shader it would change colour with
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