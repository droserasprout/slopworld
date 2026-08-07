using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The jukebox: LMB opens Play, Mute, Stop on exit and Settings; Play opens the two
    // stations, and the streaming one opens the quality presets it serves. Whatever is
    // playing is marked, and hovering the box names the song. Radio owns the sound; this
    // owns the box it comes out of.
    //
    // The click is read here rather than through selection because nothing on this map is
    // selectable but a colonist - StripInteraction turns every other Select away - so the
    // jukebox would never see one. Same shape as CoreTip, which reads the core's click for
    // the same reason.
    public class Jukebox : MapComponent
    {
        public Jukebox(Map map) : base(map) { }

        // A colony has one box. AgentColony's latch is what keeps a batch of pods from
        // each bringing their own; this is the swept floor underneath it, so a save made
        // before that latch existed comes back with one rather than with a row of them.
        public override void FinalizeInit()
        {
            var boxes = map.listerThings.ThingsOfDef(SlopDefOf.SlopJukebox);
            if (boxes.Count < 2) return;

            int extra = boxes.Count - 1;
            // Vanish: they are not wreckage and there is nobody to explain them to.
            // Destroy shortens the lister's own list, so the last one is taken each time.
            while (boxes.Count > 1) boxes[boxes.Count - 1].Destroy(DestroyMode.Vanish);
            Log.Message($"[SlopWorld] jukebox: removed {extra} duplicate(s)");
        }

        public override void MapComponentOnGUI()
        {
            if (Cutscene.Playing) return; // a scene plays bare

            var cell = UI.MouseCell();
            var box = map.thingGrid.ThingAt(cell, SlopDefOf.SlopJukebox);
            if (box == null) return;

            Tip(cell);

            if (Event.current.type != EventType.MouseDown || Event.current.button != 0) return;
            if (Find.WindowStack.FloatMenu != null) return; // one menu is enough

            Event.current.Use();
            OpenMenu();
        }

        // The box's own menu, from the box or from the status bar's icon. Nothing in it is
        // about where the press came from - the four rows are the colony's one radio either
        // way - so the whole of it is static and the two callers differ only in what they
        // had to do to be heard. OpenOverPane rather than a plain Add because the status bar
        // is drawn over a terminal as well as over the map.
        public static void OpenMenu()
        {
            TerminalWindow.OpenOverPane(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption(PlayRow(), Stations),
                SlopWidgets.MenuToggle("Mute", Radio.Muted, Radio.ToggleMute),
                SlopWidgets.MenuToggle("Stop on exit", Radio.StopOnExit, Radio.ToggleStopOnExit),
                new FloatMenuOption("Settings", SlopOptions.OpenAudioTab),
            }));
        }

        // What is playing, on the box itself, in vanilla's own bubble - the thing is not
        // selectable and has no inspect pane to put it in, and this is the question a
        // hovering pointer is asking. Nothing is drawn when there is nothing to say: muted,
        // or a station a second into a pick and still unnamed.
        //
        // The note is escaped rather than typed in. The file is UTF-8 either way; what an
        // escape rules out is a toolchain that decides otherwise.
        const string Note = "\u266A ";

        // A `TipSignal` with no id of its own is keyed on its text, so the bubble would
        // restart its fade every time the station moved on. The cell is the box.
        void Tip(IntVec3 cell)
        {
            string now = Radio.NowPlaying;
            if (string.IsNullOrEmpty(now)) return;

            TooltipHandler.TipRegion(CellRect(cell), new TipSignal(Note + now, cell.GetHashCode()));
        }

        // The same question asked of the status bar's icon, which is a door onto this box and
        // so owes the same answer. The name leads rather than standing alone, the way the
        // other doors up there name what they open; the song follows when there is one, and
        // with nothing playing the row is what it always was.
        public static string IconTip()
        {
            string now = Radio.NowPlaying;
            return string.IsNullOrEmpty(now) ? "Jukebox" : "Jukebox\n" + Note + now;
        }

        // The cell in screen coordinates. Two opposite corners mapped and squared up, since
        // which way round they come out is the camera's business rather than ours.
        static Rect CellRect(IntVec3 cell)
        {
            var a = UI.MapToUIPosition(cell.ToVector3());
            var b = UI.MapToUIPosition(cell.ToVector3() + new Vector3(1f, 0f, 1f));
            return new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        }

        // The first row carries what is on, so the common question is answered without
        // opening anything. Muted, nothing is on, and saying so is what the row is for.
        static string PlayRow() => "Play  -  " + (Radio.Muted ? "muted" : Playing());

        static string Playing() =>
            Radio.Picked == Radio.Station.Paradise
                ? $"{Radio.StationName} {Radio.RateLabel(Radio.Rate)}"
                : "OST";

        // The stations, one level down. The two of them were the whole of this menu until
        // the box grew settings; they are behind a row of their own now so that what is
        // played and how it is played are not one list.
        static void Stations()
        {
            TerminalWindow.OpenOverPane(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption(Mark("OST", Radio.Picked == Radio.Station.Ost),
                    Radio.PickOst),
                new FloatMenuOption(StationRow(), Presets),
            }));
        }

        // The station's row carries the preset it is on, in the separator the rest of the
        // interface uses for "this, at that". Clicking it opens the presets rather than
        // playing anything, because which one is a question the row cannot answer.
        static string StationRow() =>
            Radio.Picked == Radio.Station.Paradise
                ? $"{Radio.StationName}  -  {Radio.RateLabel(Radio.Rate)}"
                : Radio.StationName;

        // The second level: one row per quality the station serves. A FloatMenuOption
        // holds no children of its own, so a nested list is a second menu opened from the
        // first - which is also what the palette and the shortcut rows do.
        static void Presets()
        {
            var options = new List<FloatMenuOption>();
            foreach (int rate in Radio.Rates)
            {
                options.Add(new FloatMenuOption(
                    Mark(Radio.RateLabel(rate),
                        Radio.Picked == Radio.Station.Paradise && Radio.Rate == rate),
                    () => Radio.PickParadise(rate)));
            }
            TerminalWindow.OpenOverPane(new FloatMenu(options));
        }

        // What is playing is marked rather than greyed out: a disabled row reads as
        // broken, and picking the one already on does nothing anyway. Muted, nothing is
        // playing and nothing is marked - the tick on the Mute row is where that is said.
        static string Mark(string label, bool playing) =>
            playing && !Radio.Muted ? label + "  (playing)" : label;

        // Whether the colony has its jukebox already. AgentColony asks before it packs
        // another into a pod.
        public static bool On(Map map) =>
            map != null && map.listerThings.ThingsOfDef(SlopDefOf.SlopJukebox).Count > 0;
    }
}
