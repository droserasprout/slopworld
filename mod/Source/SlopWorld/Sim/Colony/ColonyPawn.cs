using RimWorld;
using Verse;

namespace SlopWorld
{
    // Shared generation policy. Callers own naming, appearance and placement.
    static class ColonyPawn
    {
        public static Pawn Generate()
        {
            var request = new PawnGenerationRequest(
                PawnKindDefOf.Colonist,
                Faction.OfPlayer,
                PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                allowAddictions: false,
                colonistRelationChanceFactor: 0f,
                allowGay: true);
            return PawnGenerator.GeneratePawn(request);
        }
    }
}
