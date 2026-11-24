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
            string song = "\u266A " + now;
            return Radio.OstSelected ? song : "\u25C9 " + Radio.Source + "\n" + song;
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
            else if (Radio.SharedPlaybackLabel != null)
                options.Add(new FloatMenuOption(Radio.SharedPlaybackLabel, null));
            if (!string.IsNullOrEmpty(Radio.PlaybackMessage))
                options.Add(new FloatMenuOption(Radio.PlaybackMessage, null));
            if (!Radio.LocalPlaybackSupported)
                options.Add(new FloatMenuOption("OST unavailable in this client", null));
            options.Add(TransportRow());

            // Sidecar mode uses native OST playback. Show only controls that this mode supports.
            if (!BrowserJukebox.Available && !SessionHub.Instance.Capabilities.AudioPlayback)
            {
                options.Add(new FloatMenuOption("Settings", ModOptions.OpenAudioTab));
                return options;
            }

            options.Add(new UiSubmenu("Tune", StationOptions));
            if (BrowserJukebox.Available)
                options.Add(new UiSubmenu("Output", OutputOptions));
            if (Radio.Spotify && Radio.SpotifyAvailable && Radio.SourceShown(Radio.SpotifySourceId))
                options.Add(new FloatMenuOption("Open Spotify player", JukeboxActions.OpenSpotify));
            else if (!BrowserJukebox.Available)
                options.Add(RecognizeRow());
            options.Add(new FloatMenuOption("Like", JukeboxActions.Like));
            options.Add(new FloatMenuOption("History", JukeboxHistoryView.Open));
            if (!BrowserJukebox.Available || Radio.ServerOutput)
                options.Add(
                    UiLayout.MenuToggle(
                        BrowserJukebox.Available ? "Stop shared player on exit" : "Stop on exit",
                        Radio.StopOnExit,
                        Radio.ToggleStopOnExit
                    )
                );
            options.Add(new FloatMenuOption("Settings", ModOptions.OpenAudioTab));
            return options;
        }

        static List<FloatMenuOption> OutputOptions()
        {
            string browser = BrowserJukebox.Current.BrowserName;
            string hostname = SessionHub.Instance.Capabilities.Hostname;
            string server =
                "Server — "
                + (string.IsNullOrWhiteSpace(hostname) ? "daemon" : hostname)
                + " (shared)";
            var serverOption = UiLayout.MenuToggle(
                server,
                Radio.ServerOutput,
                Radio.ServerAvailable ? () => Radio.SelectOutput(true) : null
            );
            if (!Radio.ServerAvailable)
                serverOption.tooltip = Radio.ServerUnavailable;
            return new List<FloatMenuOption>
            {
                UiLayout.MenuToggle(
                    "This device — " + (string.IsNullOrWhiteSpace(browser) ? "browser" : browser),
                    !Radio.ServerOutput,
                    () => Radio.SelectOutput(false)
                ),
                serverOption,
            };
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
                        bool enabled = Radio.TransportAvailable && (i == 1 || !Radio.Spotify);
                        bool clicked = UiLayout.Button(
                            button,
                            icons[i],
                            UiTheme.Btn.Ghost,
                            enabled
                        );
                        if (clicked)
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
            if (
                (Radio.LocalPlaybackSupported || Radio.ServerOutput)
                && Radio.SourceShown(Radio.OstSourceId)
            )
                options.Add(
                    new FloatMenuOption(
                        Mark("OST", Radio.OstSelected),
                        !Radio.ServerOutput || Radio.ServerAvailable ? Radio.PickOst : null
                    )
                );
            if (
                (Radio.SpotifyAvailable || BrowserJukebox.Available)
                && Radio.SourceShown(Radio.SpotifySourceId)
            )
                options.Add(
                    new FloatMenuOption(
                        Mark(
                            Radio.SpotifyAvailable
                                ? "Spotify (ncspot)"
                                : "Spotify · "
                                    + (
                                        Radio.ServerAvailable
                                            ? "ncspot is unavailable"
                                            : Radio.ServerUnavailable
                                    ),
                            Radio.Spotify
                        ),
                        Radio.SpotifyAvailable ? JukeboxActions.OpenSpotify : null
                    )
                );
            if (
                BrowserJukebox.Available
                && !(Radio.ServerOutput ? Radio.ServerAvailable : Radio.RelayAvailable)
            )
            {
                options.Add(
                    new FloatMenuOption(
                        Radio.ServerOutput ? Radio.ServerUnavailable : "Radio relay unavailable",
                        null
                    )
                );
                return options;
            }
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
            Radio.Picked == s ? $"{s.Name}  -  {Radio.RateLabel(Radio.PickedRate)}" : s.Name;

        // Show one row per available rate, including stations with only one rate.
        static List<FloatMenuOption> Presets(Radio.Station s)
        {
            var options = new List<FloatMenuOption>();
            foreach (int preset in s.Rates)
            {
                var rate = preset; // the closure outlives the loop
                options.Add(
                    new FloatMenuOption(
                        Mark(Radio.RateLabel(rate), Radio.Picked == s && Radio.PickedRate == rate),
                        () => Radio.Pick(s, rate)
                    )
                );
            }
            return options;
        }

        // Mark the playing source without disabling its row.
        // When paused, leave source rows unmarked.
        static string Mark(string label, bool playing) =>
            playing && !Radio.Muted ? label + "  \u266A" : label;
    }
}
