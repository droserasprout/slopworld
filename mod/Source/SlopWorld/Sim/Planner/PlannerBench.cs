using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Presence owns cargo identity; this component delivers missing benches and routes map input.
    public partial class PlannerBench
    {
        public void EnsureArrived(IntVec3 cell)
        {
            var bench = ArrivalCargo();
            if (bench == null)
                return;
            try
            {
                ArrivalDrops.Drop(cell, map, new[] { bench }, 110);
            }
            catch (Exception error)
            {
                Log.Warning($"[SlopWorld] planner delivery: {error.Message}");
                // A partial delivery may already own the bench. Never duplicate its cargo.
                if (!bench.Destroyed && !bench.Spawned && bench.ParentHolder == null)
                {
                    bench.Destroy(DestroyMode.Vanish);
                    EnsurePlaced(cell);
                }
            }
        }

        public override void MapComponentOnGUI()
        {
            if (Cutscene.Playing || Eco.Resting || WorkspaceWindow.Covering)
                return;
            var cell = UI.MouseCell();
            if (map.thingGrid.ThingAt(cell, ModDefOf.SlopPlannerBench) == null)
                return;
            var a = UI.MapToUIPosition(cell.ToVector3());
            var b = UI.MapToUIPosition(cell.ToVector3() + new Vector3(1f, 0f, 1f));
            var rect = new Rect(
                Mathf.Min(a.x, b.x),
                Mathf.Min(a.y, b.y),
                Mathf.Abs(b.x - a.x),
                Mathf.Abs(b.y - a.y)
            );
            TooltipHandler.TipRegion(rect, "Planner · TODO.txt");
            if (
                Event.current.type != EventType.MouseDown
                || Event.current.button != 0
                || Find.WindowStack.FloatMenu != null
            )
                return;
            Event.current.Use();
            PlannerView.Open();
        }
    }
}
