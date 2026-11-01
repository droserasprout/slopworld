using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Places workspace arrival pods across nearby ground, rather than clustering at one cell.
    // Callers retain cargo identity across delivery and save/load.
    internal static class ArrivalDrops
    {
        const float Radius = 5f;
        const int SpacingSquared = 16;

        internal static void Drop(IntVec3 anchor, Map map, IEnumerable<Thing> cargo, int openDelay)
        {
            var occupied = new List<IntVec3> { anchor };
            foreach (
                var def in new[]
                {
                    ModDefOf.Ship_ComputerCore,
                    ModDefOf.SlopJukebox,
                    ModDefOf.SlopPlannerBench,
                }
            )
            foreach (var thing in map.listerThings.ThingsOfDef(def))
                occupied.Add(thing.Position);

            var candidates = new List<IntVec3>();
            foreach (var cell in GenRadial.RadialCellsAround(anchor, Radius, true))
                if (
                    cell.InBounds(map)
                    && DropCellFinder.IsGoodDropSpot(
                        cell,
                        map,
                        allowFogged: true,
                        canRoofPunch: true
                    )
                    && cell.GetFirstPawn(map) == null
                )
                    candidates.Add(cell);

            foreach (var thing in cargo)
            {
                var spaced = candidates.FindAll(cell =>
                    occupied.TrueForAll(other => cell.DistanceToSquared(other) >= SpacingSquared)
                );
                var choices = spaced.Count > 0 ? spaced : candidates;
                if (choices.Count == 0)
                {
                    // Constrained terrain still gets the vanilla safe-placement fallback.
                    DropPodUtility.DropThingsNear(
                        anchor,
                        map,
                        new[] { thing },
                        openDelay: openDelay,
                        canInstaDropDuringInit: false,
                        leaveSlag: false,
                        canRoofPunch: true,
                        forbid: false,
                        allowFogged: true,
                        faction: Faction.OfPlayer
                    );
                    continue;
                }
                var target = choices.RandomElement();
                occupied.Add(target);
                candidates.Remove(target);
                var pod = new ActiveTransporterInfo { openDelay = openDelay, leaveSlag = false };
                if (!pod.innerContainer.TryAdd(thing))
                    throw new System.InvalidOperationException("Could not load arrival cargo.");
                try
                {
                    DropPodUtility.MakeDropPodAt(target, map, pod, Faction.OfPlayer);
                }
                catch
                {
                    // Release uncommitted cargo so the caller can retry or place it directly.
                    if (pod.parent == null)
                        pod.innerContainer.Remove(thing);
                    throw;
                }
            }
        }
    }
}
