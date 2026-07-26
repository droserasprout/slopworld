using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// An agent is a process wearing a colonist, so it gets a machine face: two
    /// lenses and a vented grill in metal from the hairline down, drawn over the
    /// vanilla head the pawn was generated with rather than replacing it. The
    /// head, its skin colour and its hair are all still the game's, which is the
    /// point - a person converted reads better than a whole robot head, which
    /// read as a different species.
    ///
    /// The metal is cut against the skull rather than laid on it as a shape of its
    /// own; tools/roboface.py has the geometry and the reasons. What matters on
    /// this side is that the cut is a fixed line across the brow, which is why the
    /// hair a pawn is generated with is not left entirely to the game.
    /// </summary>
    public static class RobotFace
    {
        /// <summary>The plate's texture, without the rotation suffix. There is no
        /// _north: a faceplate has no back, and the node below hides on that
        /// facing. _west is Graphic_Multi's mirror of _east.</summary>
        public const string TexPath = "SlopWorld/RobotFace";

        /// <summary>Hair that shows scalp. The plate's own edge is a fixed line
        /// across the brow, so a cut whose hairline sits above it leaves a band of
        /// bare skin between the two, and one that only covers a strip leaves skin
        /// at the sides. Matched by defName rather than through a DefOf, so a name
        /// this game does not have is simply never matched instead of failing at
        /// load - which is what makes the list safe to add to on sight.</summary>
        static readonly HashSet<string> ScalpHair =
            new HashSet<string> { "Bald", "Shaved", "Mohawk" };

        /// <summary>Rerolls a generated pawn's hair until it is something the plate
        /// can sit under. Called once, at generation, rather than from the reconcile:
        /// nothing takes an agent's hair away later, and a pawn whose every option
        /// is refused would otherwise be rerolled once a second forever.</summary>
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

        /// <summary>Takes off the beard, which is the one thing that would draw
        /// over the plate - beards hang on a render node above the head, hair does
        /// not. Cheap to call on every reconcile: it only ever touches a pawn
        /// once.</summary>
        public static void Apply(Pawn pawn)
        {
            if (pawn?.style == null) return;
            if (pawn.style.beardDef == BeardDefOf.NoBeard) return;

            pawn.style.beardDef = BeardDefOf.NoBeard;
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }

    /// <summary>
    /// Hangs the faceplate on every agent's head.
    ///
    /// Every non-abstract subclass of DynamicPawnRenderNodeSetup is found by
    /// GenTypes.AllSubclassesNonAbstract and instantiated by the game, so this
    /// needs no def and no patch - it is picked up the way a GameComponent is.
    /// It runs when a pawn's render tree is built, which is on load and on any
    /// SetAllGraphicsDirty, so an agent that gains its body mid-session gets the
    /// plate as soon as something dirties it.
    ///
    /// The parent is handed back as null on purpose: PawnRenderTree.AddChild
    /// resolves it from props.parentTagDef against its own nodesByTag, and doing
    /// it that way means we never hold a node the tree has since rebuilt.
    /// </summary>
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
                // PawnRenderNode_AttachmentHead takes its mesh from
                // HumanlikeMeshPoolUtility.GetHumanlikeHairSetForPawn, which is
                // the mesh vanilla hair is drawn on - so the plate lands in the
                // same frame as the hair for this head type, narrow crowns
                // included, and needs no drawSize or offset of its own. The
                // default worker is the one hair uses too.
                nodeClass = typeof(PawnRenderNode_AttachmentHead),
                texPath = RobotFace.TexPath,
                parentTagDef = PawnRenderNodeTagDefOf.Head,
                // The plate arrives painted, so it takes the plain cutout shader
                // and no tint. Left on the skin shader it would be a suntanned
                // faceplate that changed colour with the pawn under it.
                shaderTypeDef = ShaderTypeDefOf.Cutout,
                colorType = PawnRenderNodeProperties.AttachmentColorType.Custom,
                color = Color.white,
                // Just above the head we are covering, read off that node rather
                // than written down here: layers are absolute floats out of the
                // humanlike render tree def, and a number copied from it would be
                // a number to get wrong on the next version. Half a layer rather
                // than a whole one so the plate cannot tie with, or climb over,
                // whatever the tree puts on the next layer up - hair included.
                baseLayer = head.Props.baseLayer + 0.5f,
                // A face is not visible from behind. Leaving north out shows the
                // pawn's own head there instead of a plate on the back of it.
                visibleFacing = new List<Rot4> { Rot4.South, Rot4.East, Rot4.West },
            };

            if (!tree.ShouldAddNodeToTree(props)) yield break;

            yield return (new PawnRenderNode_AttachmentHead(pawn, props, tree), null);
        }

        /// <summary>The tree's head node. nodesByTag is private, so this walks
        /// down from the public root instead of binding a field by name.</summary>
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
