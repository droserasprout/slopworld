using System.Collections.Generic;
using Verse;

namespace SlopWorld
{
    // Shared jukebox controls for map, workspace and settings entry points.
    public static class JukeboxMenu
    {
        const string Note = "\u266A ";

        // Use the same current track label for the status bar tooltip.
        public static string IconTip()
        {
            string now = Radio.CachedNowPlaying;
            return string.IsNullOrEmpty(now) ? "" : Note + now;
        }

        // Open the same menu from the map jukebox and status bar.
        // OpenOverPane supports both map and terminal display.
        public static void Open()
        {
            WorkspaceWindow.OpenOverPane(new UiMenu(MenuOptions()));
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
                    ? new FloatMenuOption("Open Spotify player", JukeboxActions.OpenSpotify)
                    : RecognizeRow(),
                new FloatMenuOption("Like", JukeboxActions.Like),
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
                Radio.Recognize
            );
        }

        // Show the current source or muted state in the first row.
        static string PlayRow() => "Play  -  " + (Radio.Muted ? "muted" : Playing());

        static string Playing()
        {
            if (Radio.Spotify)
                return "Spotify";
            var on = Radio.Picked;
            return on == null ? "OST" : $"{on.Name} {Radio.RateLabel(on.Rate)}";
        }

        // List music sources in a submenu. Place OST first because it requires no network connection.
        public static List<FloatMenuOption> StationOptions()
        {
            var options = new List<FloatMenuOption>();
            if (Radio.SourceShown(Radio.OstSourceId))
                options.Add(
                    new FloatMenuOption(
                        Mark("OST", !Radio.Spotify && Radio.Picked == null),
                        Radio.PickOst
                    )
                );
            if (Radio.SpotifyAvailable && Radio.SourceShown(Radio.SpotifySourceId))
                options.Add(
                    new FloatMenuOption(
                        Mark("Spotify (ncspot)", Radio.Spotify),
                        JukeboxActions.OpenSpotify
                    )
                );
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
                options.Add(
                    new FloatMenuOption(
                        Mark(Radio.RateLabel(rate), Radio.Picked == s && s.Rate == rate),
                        () => Radio.Pick(s, rate)
                    )
                );
            }
            return options;
        }

        // Mark the playing source without disabling its row.
        // When muted, leave source rows unmarked and use the Mute toggle to show the state.
        static string Mark(string label, bool playing) =>
            playing && !Radio.Muted ? label + "  (playing)" : label;
    }
}
