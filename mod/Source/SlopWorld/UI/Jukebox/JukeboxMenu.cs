using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared jukebox controls for map, workspace and settings entry points.
    public static class JukeboxMenu
    {
        // Use the same current track label for the status bar tooltip.
        public static string IconTip()
        {
            string now = Radio.CachedNowPlaying;
            if (string.IsNullOrEmpty(now))
                return "Jukebox";
            // Keep tooltip text in the UI face: decorative glyphs can force an entire
            // label onto a fallback face whose advances differ from native measurement.
            string song = "Now playing: " + now;
            return !Radio.Spotify && Radio.Picked == null
                ? song
                : "Source: " + Radio.Source + "\n" + song;
        }

        // Open the same menu from the map jukebox and status bar.
        // OpenOverPane supports both map and terminal display.
        public static void Open()
        {
            WorkspaceWindow.OpenOverPane(new UiMenu(MenuOptions()));
        }

        static List<FloatMenuOption> MenuOptions()
        {
            var options = new List<FloatMenuOption>();
            string now = Radio.NowPlaying;
            if (!string.IsNullOrEmpty(now))
                options.Add(new FloatMenuOption(now, null));
            if (!Radio.LocalPlaybackSupported)
                options.Add(new FloatMenuOption("OST unavailable in this client", null));
            options.Add(TransportRow());

            // Sidecar mode uses native OST playback. Show only controls that this mode supports.
            if (!SessionHub.Instance.Capabilities.AudioPlayback)
            {
                options.Add(new FloatMenuOption("Settings", ModOptions.OpenAudioTab));
                return options;
            }

            options.AddRange(
                new FloatMenuOption[]
                {
                    new UiSubmenu("Tune", StationOptions),
                    Radio.Spotify
                    && Radio.SpotifyAvailable
                    && Radio.SourceShown(Radio.SpotifySourceId)
                        ? new FloatMenuOption("Open Spotify player", JukeboxActions.OpenSpotify)
                        : RecognizeRow(),
                    new FloatMenuOption("Like", JukeboxActions.Like),
                    new FloatMenuOption("History", JukeboxHistoryView.Open),
                    UiLayout.MenuToggle("Stop on exit", Radio.StopOnExit, Radio.ToggleStopOnExit),
                    new FloatMenuOption("Settings", ModOptions.OpenAudioTab),
                }
            );
            return options;
        }

        // FloatMenuOption substitutes a diagnostic label for an empty string.
        static FloatMenuOption TransportRow() =>
            new FloatMenuOption(" ", () => { })
            {
                extraPartWidth = 160f,
                extraPartRightJustified = false,
                extraPartOnGUI = rect =>
                {
                    // These symbols default to text; native labels render VS15 as a box.
                    var icons = new[]
                    {
                        "\u23EE",
                        Radio.Muted ? "\u25B6" : "\u23F8",
                        "\u23ED",
                        "\u2928",
                    };
                    var tips = new[]
                    {
                        "Previous",
                        Radio.Muted ? "Play" : "Pause",
                        "Next",
                        "Shuffle",
                    };
                    System.Action[] actions =
                    {
                        Radio.Previous,
                        Radio.ToggleMute,
                        Radio.Next,
                        Radio.PickRandom,
                    };
                    for (int i = 0; i < actions.Length; i++)
                    {
                        var button = new Rect(
                            rect.x + i * rect.width / 4f,
                            rect.y,
                            rect.width / 4f,
                            rect.height
                        );
                        TooltipHandler.TipRegion(button, tips[i]);
                        if (
                            UiLayout.Button(
                                button,
                                icons[i],
                                UiTheme.Btn.Ghost,
                                Radio.TransportAvailable && (i == 1 || !Radio.Spotify)
                            )
                        )
                        {
                            UiMenu.CloseAll();
                            actions[i]();
                        }
                    }
                    return false;
                },
            };

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

        // List music sources in a submenu. Place OST first because it requires no network connection.
        public static List<FloatMenuOption> StationOptions()
        {
            var options = new List<FloatMenuOption>();
            if (Radio.LocalPlaybackSupported && Radio.SourceShown(Radio.OstSourceId))
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
        // When paused, leave source rows unmarked.
        static string Mark(string label, bool playing) =>
            playing && !Radio.Muted ? label + "  (playing)" : label;
    }
}
