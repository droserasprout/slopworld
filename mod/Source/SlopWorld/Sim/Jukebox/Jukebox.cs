using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Radio owns playback and catalog state. This component owns map menus, tooltips, and input.
    // Read clicks directly because StripInteraction permits selection only for colonists.
    public class Jukebox : MapComponent
    {
        public Jukebox(Map map) : base(map) { }

        // Remove duplicate jukeboxes after loading. AgentColony prevents new duplicates.
        public override void FinalizeInit()
        {
            var boxes = map.listerThings.ThingsOfDef(ModDefOf.SlopJukebox);
            if (boxes.Count < 2) return;

            int extra = boxes.Count - 1;
            // Remove items from the end because destruction changes the list.
            while (boxes.Count > 1) boxes[boxes.Count - 1].Destroy(DestroyMode.Vanish);
            Log.Message($"[SlopWorld] jukebox: removed {extra} duplicate(s)");
        }

        public override void MapComponentOnGUI()
        {
            // Skip map input during cutscenes, Eco rest, or terminal coverage.
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

        // Open the same menu from the map jukebox and status bar.
        // OpenOverPane supports both map and terminal display.
        public static void OpenMenu()
        {
            TerminalWindow.OpenOverPane(new UiMenu(MenuOptions()));
        }

        static List<FloatMenuOption> MenuOptions()
        {
            // Sidecar mode uses native OST playback. Show only controls that this mode supports.
            if (!SessionHub.Instance.Capabilities.AudioPlayback)
            {
                return new List<FloatMenuOption>
                {
                    UiLayout.MenuToggle("Mute", Radio.Muted, Radio.ToggleMute),
                    new FloatMenuOption("Settings", ModOptions.OpenAudioTab),
                };
            }

            return new List<FloatMenuOption>
            {
                new UiSubmenu(PlayRow(), StationOptions),
                Radio.Spotify && Radio.SpotifyAvailable && Radio.SourceShown(Radio.SpotifySourceId)
                    ? new FloatMenuOption("Open Spotify player", Radio.OpenSpotify) : RecognizeRow(),
                new FloatMenuOption("Like", Radio.Like),
                new FloatMenuOption("History", JukeboxHistoryView.Open),
                UiLayout.MenuToggle("Mute", Radio.Muted, Radio.ToggleMute),
                UiLayout.MenuToggle("Stop on exit", Radio.StopOnExit, Radio.ToggleStopOnExit),
                new FloatMenuOption("Settings", ModOptions.OpenAudioTab),
            };
        }

        // Show cancellation and the input label during recognition. Otherwise, offer recognition or retry.
        // Menu rows reflect state when the menu opens.
        static FloatMenuOption RecognizeRow()
        {
            if (Radio.Recognizing)
            {
                string input = Radio.RecognizingInput;
                string label = string.IsNullOrEmpty(input)
                    ? "Cancel recognition\u2026"
                    : "Cancel recognition (" + input + ")";
                return new FloatMenuOption(label, Radio.CancelRecognition);
            }

            return new FloatMenuOption(
                string.IsNullOrEmpty(Radio.RecognitionError) ? "Recognize" : "Recognize (retry)",
                Radio.Recognize);
        }

        // Show the current track in hover text. Omit the tooltip when no track label is available.
        const string Note = "\u266A ";

        // Use the cell hash as a stable tooltip identifier.
        // An identifier based on text would restart the fade whenever the track title changes.
        void Tip(IntVec3 cell)
        {
            string now = Radio.CachedNowPlaying;
            if (string.IsNullOrEmpty(now)) return;

            TooltipHandler.TipRegion(CellRect(cell), new TipSignal(Note + now, cell.GetHashCode()));
        }

        // Use the same current track label for the status bar tooltip.
        public static string IconTip()
        {
            string now = Radio.CachedNowPlaying;
            return string.IsNullOrEmpty(now) ? "" : Note + now;
        }

        // Convert opposite cell corners to screen coordinates.
        // Normalize their order because it depends on the camera.
        static Rect CellRect(IntVec3 cell)
        {
            var a = UI.MapToUIPosition(cell.ToVector3());
            var b = UI.MapToUIPosition(cell.ToVector3() + new Vector3(1f, 0f, 1f));
            return new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        }

        // Show the current source or muted state in the first row.
        static string PlayRow() => "Play  -  " + (Radio.Muted ? "muted" : Playing());

        static string Playing()
        {
            if (Radio.Spotify) return "Spotify";
            var on = Radio.Picked;
            return on == null ? "OST" : $"{on.Name} {Radio.RateLabel(on.Rate)}";
        }

        // List music sources in a submenu. Place OST first because it requires no network connection.
        public static List<FloatMenuOption> StationOptions()
        {
            var options = new List<FloatMenuOption>();
            if (Radio.SourceShown(Radio.OstSourceId))
                options.Add(new FloatMenuOption(Mark("OST", !Radio.Spotify && Radio.Picked == null), Radio.PickOst));
            if (Radio.SpotifyAvailable && Radio.SourceShown(Radio.SpotifySourceId))
                options.Add(new FloatMenuOption(Mark("Spotify (ncspot)", Radio.Spotify), Radio.OpenSpotify));
            foreach (var station in Radio.Stations)
            {
                var s = station; // the closure outlives the loop
                if (Radio.SourceShown(s.Id))
                    options.Add(new UiSubmenu(StationRow(s), () => Presets(s)));
            }
            return options;
        }

        // Show the selected station rate in its row. Open the rate list when the player selects the row.
        static string StationRow(Radio.Station s) =>
            Radio.Picked == s ? $"{s.Name}  -  {Radio.RateLabel(s.Rate)}" : s.Name;

        // Show one row per available rate, including stations with only one rate.
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

        // Mark the playing source without disabling its row.
        // When muted, leave source rows unmarked and use the Mute toggle to show the state.
        static string Mark(string label, bool playing) =>
            playing && !Radio.Muted ? label + "  (playing)" : label;

        // Check for an existing jukebox before AgentColony adds another to a pod.
        public static bool On(Map map) =>
            map != null && map.listerThings.ThingsOfDef(ModDefOf.SlopJukebox).Count > 0;
    }
}
