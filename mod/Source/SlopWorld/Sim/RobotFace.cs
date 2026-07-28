using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Two lenses and a vented grill in metal from the hairline down, drawn over the
    // vanilla head rather than replacing it: the head, its skin colour and its hair
    // are still the game's, and a person converted reads better than the whole robot
    // head this replaced, which read as a different species. The metal is cut against
    // the skull rather than laid on it; tools/roboface.py has the geometry.
    public static class RobotFace
    {
        // No _north: a faceplate has no back, and the node below hides on that facing.
        // _west is Graphic_Multi's mirror of _east.
        public const string TexPath = "SlopWorld/RobotFace";

        // The plate's edge is a fixed line across the brow, so a cut whose hairline sits
        // above it leaves bare skin between the two. Matched by defName rather than
        // through a DefOf, so a name this game does not have is never matched instead of
        // failing at load.
        static readonly HashSet<string> ScalpHair =
            new HashSet<string> { "Bald", "Shaved", "Mohawk" };

        // Called once at generation rather than from the reconcile: nothing takes an
        // agent's hair away later, and a pawn whose every option is refused would be
        // rerolled once a second forever.
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

    // Every non-abstract subclass of DynamicPawnRenderNodeSetup is found by
    // GenTypes.AllSubclassesNonAbstract and instantiated by the game, so this needs
    // no def and no patch. It runs when a render tree is built - on load and on any
    // SetAllGraphicsDirty. The parent is handed back as null on purpose:
    // PawnRenderTree.AddChild resolves it from parentTagDef against its own
    // nodesByTag, so we never hold a node the tree has since rebuilt.
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
                // PawnRenderNode_AttachmentHead takes its mesh from GetHumanlikeHairSetForPawn,
                // the mesh vanilla hair is drawn on - so the plate lands in the same frame as the
                // hair for this head type, narrow crowns included, and needs no size of its own.
                nodeClass = typeof(PawnRenderNode_AttachmentHead),
                texPath = RobotFace.TexPath,
                parentTagDef = PawnRenderNodeTagDefOf.Head,
                // The plate arrives painted, so it takes the plain cutout shader and no tint. On
                // the skin shader it would change colour with the pawn under it.
                shaderTypeDef = ShaderTypeDefOf.Cutout,
                colorType = PawnRenderNodeProperties.AttachmentColorType.Custom,
                color = Color.white,
                // Read off the head node rather than written down: layers are absolute floats out
                // of the humanlike render tree def, and a copied number is one to get wrong next
                // version. Half a layer, so the plate cannot tie with whatever the tree puts on
                // the next layer up.
                baseLayer = head.Props.baseLayer + 0.5f,
                // A face is not visible from behind. Leaving north out shows the pawn's own head
                // there instead of a plate on the back of it.
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
