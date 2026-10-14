using RimWorld;
using Verse;

namespace SlopWorld
{
    // Factionless population generation and spawning. Callers own kind and cell selection.
    static class NeutralPawn
    {
        public static void Spawn(Map map, PawnKindDef kind, IntVec3 cell, string context)
        {
            try
            {
                var request = new PawnGenerationRequest(
                    kind,
                    null,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true
                );
                GenSpawn.Spawn(PawnGenerator.GeneratePawn(request), cell, map);
            }
            catch (System.Exception e)
            {
                ReportFailure(context, e);
            }
        }

        public static void ReportFailure(string context, System.Exception exception)
        {
            Log.Warning($"[SlopWorld] {context}: {exception.Message}");
        }
    }
}
