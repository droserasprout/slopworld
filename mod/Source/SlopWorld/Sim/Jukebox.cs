using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Map jukebox UI: Radio owns playback/catalog state; this owns menus, hover text, and input.
    // Read clicks directly because StripInteraction makes only colonists selectable.
    public class Jukebox : MapComponent
    {
        public Jukebox(Map map) : base(map) { }

        // Keep one box on load; AgentColony's latch prevents new duplicates.
        public override void FinalizeInit()
        {
            var boxes = map.listerThings.ThingsOfDef(ModDefOf.SlopJukebox);
            if (boxes.Count < 2) return;

            int extra = boxes.Count - 1;
            // Vanish duplicates; destroying from the end avoids invalidating the lister walk.
            while (boxes.Count > 1) boxes[boxes.Count - 1].Destroy(DestroyMode.Vanish);
            Log.Message($"[SlopWorld] jukebox: removed {extra} duplicate(s)");
        }

        public override void MapComponentOnGUI()
        {
            // Cutscenes, Eco's frame-only map, and an opaque terminal have no map box to click.
            if (Cutscene.Playing || Eco.Resting || TerminalWindow.Covering) return;

            var cell = UI.MouseCell();
            var box = map.thingGrid.ThingAt(cell, ModDefOf.SlopJukebox);
            if (box == null) return;

            Tip(cell);

            if (Event.current.type != EventType.MouseDown || Event.current.button != 0) return;
            if (Find.WindowStack.FloatMenu != null) return; // one menu is enough

            Event.current.Use();
            OpenMenu();
        }

        // Both box and status-bar entry points open the same menu; use OpenOverPane for either
        // map or terminal rendering.
        public static void OpenMenu()
        {
            TerminalWindow.OpenOverPane(new UiMenu(MenuOptions()));
        }

        static List<FloatMenuOption> MenuOptions()
        {
            // The sidecar cannot play daemon audio, so the jukebox door becomes the native
            // game's SlopWorld OST switch. Radio controls would only change settings that
            // nothing can consume in this runtime.
            if (!SessionHub.Instance.Capabilities.AudioPlayback)
            {
                return new List<FloatMenuOption>
                {
                    UiWidgets.MenuToggle("Mute", Radio.Muted, Radio.ToggleMute),
                    new FloatMenuOption("Settings", ModOptions.OpenAudioTab),
                };
            }

            return new List<FloatMenuOption>
            {
                new UiSubmenu(PlayRow(), StationOptions),
                RecognizeRow(),
                new FloatMenuOption("Like", Radio.Like),
                new FloatMenuOption("History", JukeboxHistoryView.Open),
                UiWidgets.MenuToggle("Mute", Radio.Muted, Radio.ToggleMute),
                UiWidgets.MenuToggle("Stop on exit", Radio.StopOnExit, Radio.ToggleStopOnExit),
                new FloatMenuOption("Settings", ModOptions.OpenAudioTab),
            };
        }

        // While a lookup runs the row cancels it and names the input; otherwise it starts one,
        // offering a retry when the last attempt left an error behind. A float menu is a
        // snapshot, so this reflects the state at the moment the menu was opened.
        static FloatMenuOption RecognizeRow()
        {
            if (Radio.Recognizing)
            {
                string input = Radio.RecognizingInput;
                string label = string.IsNullOrEmpty(input)
                    ? "Cancel recognizing\u2026"
                    : "Cancel recognizing (" + input + ")";
                return new FloatMenuOption(label, Radio.CancelRecognition);
            }

            return new FloatMenuOption(
                string.IsNullOrEmpty(Radio.RecognitionError) ? "Recognize" : "Recognize (retry)",
                Radio.Recognize);
        }

        // Hover text names the current track; empty/muted playback has no tooltip.
        const string Note = "\u266A ";

        // A `TipSignal` with no id of its own is keyed on its text, so the bubble would
        // restart its fade every time the station moved on. The cell is the box.
        void Tip(IntVec3 cell)
        {
            string now = Radio.NowPlaying;
            if (string.IsNullOrEmpty(now)) return;

            TooltipHandler.TipRegion(CellRect(cell), new TipSignal(Note + now, cell.GetHashCode()));
        }

        // The status-bar door uses the same current-track label.
        public static string IconTip()
        {
            string now = Radio.NowPlaying;
            return string.IsNullOrEmpty(now) ? "" : Note + now;
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

        static string Playing()
        {
            var on = Radio.Picked;
            return on == null ? "OST" : $"{on.Name} {Radio.RateLabel(on.Rate)}";
        }

        // The stations, one level down. Two of them were the whole of this menu until the
        // box grew settings; they are behind a row of their own now so that what is played
        // and how it is played are not one list. The OST leads because it is the one thing
        // here that is not a station and needs no network to play.
        public static List<FloatMenuOption> StationOptions()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(Mark("OST", Radio.Picked == null), Radio.PickOst),
            };
            foreach (var station in Radio.Stations)
            {
                var s = station; // the closure outlives the loop
                options.Add(new UiSubmenu(StationRow(s), () => Presets(s)));
            }
            return options;
        }

        // Show a station's active preset in its row; selecting the row opens that station's
        // preset list rather than playing it directly.
        static string StationRow(Radio.Station s) =>
            Radio.Picked == s ? $"{s.Name}  -  {Radio.RateLabel(s.Rate)}" : s.Name;

        // The second level: one row per quality that station serves. A station serving one
        // quality gets a list of one rather than a special case; what it answers on is
        // worth saying either way.
        static List<FloatMenuOption> Presets(Radio.Station s)
        {
            var options = new List<FloatMenuOption>();
            foreach (int preset in s.Rates)
            {
                var rate = preset; // the closure outlives the loop
                options.Add(new FloatMenuOption(
                    Mark(Radio.RateLabel(rate), Radio.Picked == s && s.Rate == rate),
                    () => Radio.Pick(s, rate)));
            }
            return options;
        }

        // What is playing is marked rather than greyed out: a disabled row reads as
        // broken, and picking the one already on does nothing anyway. Muted, nothing is
        // playing and nothing is marked - the tick on the Mute row is where that is said.
        static string Mark(string label, bool playing) =>
            playing && !Radio.Muted ? label + "  (playing)" : label;

        // Whether the colony has its jukebox already. AgentColony asks before it packs
        // another into a pod.
        public static bool On(Map map) =>
            map != null && map.listerThings.ThingsOfDef(ModDefOf.SlopJukebox).Count > 0;
    }
}
