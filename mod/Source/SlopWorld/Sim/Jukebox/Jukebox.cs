using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Radio owns playback and catalog state. This component owns map presence, tooltips, and input.
    // Read clicks directly because StripInteraction permits selection only for colonists.
    public class Jukebox : MapComponent
    {
        public Jukebox(Map map)
            : base(map) { }

        // Initial placement belongs to the player's arrival, not agent sessions.
        public Thing ArrivalCargo() => On(map) ? null : ThingMaker.MakeThing(ModDefOf.SlopJukebox);

        // Repair loaded colonies after the player lands. Never place a second box
        // while its arrival cargo is still inside the player's pod.
        public void EnsurePlaced(IntVec3 cell)
        {
            var box = ArrivalCargo();
            if (box == null)
                return;
            if (!GenPlace.TryPlaceThing(box, cell, map, ThingPlaceMode.Near))
            {
                box.Destroy(DestroyMode.Vanish);
                Log.Warning("[SlopWorld] Could not place the map jukebox.");
            }
        }

        public override void FinalizeInit()
        {
            var boxes = map.listerThings.ThingsOfDef(ModDefOf.SlopJukebox);
            if (boxes.Count < 2)
                return;

            int extra = boxes.Count - 1;
            // Remove items from the end because destruction changes the list.
            while (boxes.Count > 1)
                boxes[boxes.Count - 1].Destroy(DestroyMode.Vanish);
            Log.Message($"[SlopWorld] jukebox: removed {extra} duplicate(s)");
        }

        public override void MapComponentOnGUI()
        {
            // Skip map input during cutscenes, Eco rest, or terminal coverage.
            if (Cutscene.Playing || Eco.Resting || WorkspaceWindow.Covering)
                return;

            var cell = UI.MouseCell();
            var box = map.thingGrid.ThingAt(cell, ModDefOf.SlopJukebox);
            if (box == null)
                return;

            Tip(cell);

            if (Event.current.type != EventType.MouseDown || Event.current.button != 0)
                return;
            if (Find.WindowStack.FloatMenu != null)
                return; // one menu is enough

            Event.current.Use();
            JukeboxMenu.Open();
        }

        // Show the current track in hover text. Omit the tooltip when no track label is available.
        const string Note = "\u266A ";

        // Use the cell hash as a stable tooltip identifier.
        // An identifier based on text would restart the fade whenever the track title changes.
        void Tip(IntVec3 cell)
        {
            string now = Radio.CachedNowPlaying;
            if (string.IsNullOrEmpty(now))
                return;

            TooltipHandler.TipRegion(CellRect(cell), new TipSignal(Note + now, cell.GetHashCode()));
        }

        // Convert opposite cell corners to screen coordinates.
        // Normalize their order because it depends on the camera.
        static Rect CellRect(IntVec3 cell)
        {
            var a = UI.MapToUIPosition(cell.ToVector3());
            var b = UI.MapToUIPosition(cell.ToVector3() + new Vector3(1f, 0f, 1f));
            return new Rect(
                Mathf.Min(a.x, b.x),
                Mathf.Min(a.y, b.y),
                Mathf.Abs(b.x - a.x),
                Mathf.Abs(b.y - a.y)
            );
        }

        // Pending pod cargo is intentionally absent until it lands on the map.
        public static bool On(Map map) =>
            map != null && map.listerThings.ThingsOfDef(ModDefOf.SlopJukebox).Count > 0;
    }
}
