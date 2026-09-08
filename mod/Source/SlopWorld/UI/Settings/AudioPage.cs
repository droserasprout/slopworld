using UnityEngine;
using Verse;

namespace SlopWorld
{
    // RimWorld's mixer, brought into the SlopWorld group and drawn in the same chrome as
    // the other pages. These remain Prefs rather than mod settings: the game's sound
    // engine observes the setters, and Dialog_Options persists them on close.
    public class AudioPage : IOptionPage
    {
        public void Load() { }

        public void Draw(Rect rect)
        {
            Text.Font = GameFont.Small;
            var body = UiWidgets.PageBody(rect);
            body.height += UiWidgets.BtnH + UiWidgets.GapS;
            var inner = body.ContractedBy(UiWidgets.GapM);

            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(inner);

            UiWidgets.SectionHeading(l, "Volume");
            Prefs.VolumeMaster = UiWidgets.Slider(l, "MasterVolume".Translate(),
                Prefs.VolumeMaster, "MasterVolumeTooltip".Translate());
            Prefs.VolumeGame = UiWidgets.Slider(l, "GameVolume".Translate(),
                Prefs.VolumeGame, "GameVolumeTooltip".Translate());
            Prefs.VolumeMusic = UiWidgets.Slider(l, "MusicVolume".Translate(),
                Prefs.VolumeMusic, "MusicVolumeTooltip".Translate());
            Prefs.VolumeAmbient = UiWidgets.Slider(l, "AmbientVolume".Translate(),
                Prefs.VolumeAmbient, "AmbientVolumeTooltip".Translate());
            Prefs.VolumeUI = UiWidgets.Slider(l, "UIVolume".Translate(),
                Prefs.VolumeUI, "UIVolumeTooltip".Translate());

            l.Gap(UiWidgets.GapL);
            UiWidgets.SectionHeading(l, "Jukebox");
            if (!SessionHub.Instance.Capabilities.AudioPlayback)
            {
                UiWidgets.Note(l,
                    "Jukebox playback is unavailable in slopcar. The native game keeps audio " +
                    "on this Mac; radio streaming will return in a later compatibility release.");
                l.End();
                return;
            }

            var picked = Radio.Picked;
            string source = Radio.Muted ? "Muted" : picked == null
                ? "OST" : $"{picked.Name} {Radio.RateLabel(picked.Rate)}";
            if (UiWidgets.Button(l, "Tune: " + source))
                Find.WindowStack.Add(new UiMenu(Jukebox.StationOptions()));

            // Show the station's own line and any Shazam match as two rows, so a recognized
            // track never silently overwrites what the station actually reported.
            if (Radio.Muted)
            {
                UiWidgets.Note(l, "Muted.");
            }
            else
            {
                string station = Radio.StationLine;
                UiWidgets.Note(l, "Now playing: "
                    + (string.IsNullOrEmpty(station) ? "nothing" : station));
                if (Radio.Recognized)
                    UiWidgets.Note(l, "Recognized: " + Radio.RecognizedLine);
            }

            l.Gap(UiWidgets.GapS);
            DrawRecognition(l);

            l.Gap(UiWidgets.GapS);
            if (UiWidgets.Button(l, "Random"))
                Radio.PickRandom();
            if (UiWidgets.Button(l, "Like current song"))
                Radio.Like();
            if (UiWidgets.Button(l, "History"))
                JukeboxHistoryView.Open();

            bool mute = UiWidgets.Checkbox(l, "Mute", Radio.Muted,
                "Stop playback without downloading unheard audio.");
            if (mute != Radio.Muted) Radio.ToggleMute();

            bool stop = UiWidgets.Checkbox(l, "Stop on exit", Radio.StopOnExit,
                "Stop the daemon's playback when RimWorld exits normally.");
            if (stop != Radio.StopOnExit) Radio.ToggleStopOnExit();

            l.End();
        }

        // The recognition control makes the background lookup legible: a transient recognizing
        // state that names its input and can be cancelled, or a Recognize/Retry button that
        // surfaces the last failure instead of leaving it in a vanished toast.
        static void DrawRecognition(Listing_Standard l)
        {
            if (Radio.Recognizing)
            {
                if (UiWidgets.Button(l, "Cancel recognition"))
                    Radio.CancelRecognition();
                string input = Radio.RecognizingInput;
                UiWidgets.Note(l, string.IsNullOrEmpty(input)
                    ? "Recognizing…"
                    : "Recognizing via " + input + "…");
                return;
            }

            string error = Radio.RecognitionError;
            if (UiWidgets.Button(l,
                    string.IsNullOrEmpty(error) ? "Recognize" : "Retry recognition"))
                Radio.Recognize();

            if (!string.IsNullOrEmpty(error))
            {
                GUI.color = UiWidgets.Bad;
                l.Label(error);
                GUI.color = Color.white;
            }
            else if (!string.IsNullOrEmpty(Radio.RecognizingInput))
            {
                UiWidgets.Note(l, "Input: " + Radio.RecognizingInput);
            }
        }
    }
}
