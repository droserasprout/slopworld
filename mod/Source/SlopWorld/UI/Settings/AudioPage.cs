using UnityEngine;
using Verse;

namespace SlopWorld
{
    // RimWorld's mixer, brought into the SlopWorld group and drawn in the same chrome as
    // the other pages. These remain Prefs rather than mod settings: the game's sound
    // engine observes the setters, and Dialog_Options persists them on close.
    public class AudioPage : IOptionPage
    {
        readonly SettingsForm _form = new SettingsForm();

        public void Load() { }

        public void Draw(Rect rect)
        {
            _form.Draw(SettingsPageLayout.Body(rect, false), DrawFields);
        }

        void DrawFields(Listing_Standard l)
        {

            UiLayout.SectionHeading(l, "Volume");
            UiLayout.Note(l, "Audio changes apply immediately.");
            Prefs.VolumeMaster = UiControls.Slider(l, "MasterVolume".Translate(),
                Prefs.VolumeMaster, "MasterVolumeTooltip".Translate());
            Prefs.VolumeGame = UiControls.Slider(l, "GameVolume".Translate(),
                Prefs.VolumeGame, "GameVolumeTooltip".Translate());
            Prefs.VolumeMusic = UiControls.Slider(l, "MusicVolume".Translate(),
                Prefs.VolumeMusic, "MusicVolumeTooltip".Translate());
            Prefs.VolumeAmbient = UiControls.Slider(l, "AmbientVolume".Translate(),
                Prefs.VolumeAmbient, "AmbientVolumeTooltip".Translate());
            Prefs.VolumeUI = UiControls.Slider(l, "UIVolume".Translate(),
                Prefs.VolumeUI, "UIVolumeTooltip".Translate());

            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "Jukebox");
            if (!SessionHub.Instance.Capabilities.AudioPlayback)
            {
                UiLayout.Note(l,
                    "Jukebox playback is unavailable in slopcar. The native game keeps audio " +
                    "on this Mac; radio streaming will return in a later compatibility release.");
                return;
            }

            var picked = Radio.Picked;
            string source = Radio.Muted ? "Muted" : picked == null
                ? "OST" : $"{picked.Name} {Radio.RateLabel(picked.Rate)}";
            if (UiLayout.Button(l, "Tune: " + source))
                Find.WindowStack.Add(new UiMenu(Jukebox.StationOptions()));

            // Show the station's own line and any Shazam match as two rows, so a recognized
            // track never silently overwrites what the station actually reported.
            if (Radio.Muted)
            {
                UiLayout.Note(l, "Muted.");
            }
            else
            {
                string station = Radio.StationLine;
                UiLayout.Note(l, "Now playing: "
                    + (string.IsNullOrEmpty(station) ? "nothing" : station));
                if (Radio.Recognized)
                    UiLayout.Note(l, "Recognized: " + Radio.RecognizedLine);
            }

            l.Gap(UiTheme.GapS);
            DrawRecognition(l);

            l.Gap(UiTheme.GapS);
            if (UiLayout.Button(l, "Random"))
                Radio.PickRandom();
            if (UiLayout.Button(l, "Like current song"))
                Radio.Like();
            if (UiLayout.Button(l, "History"))
                JukeboxHistoryView.Open();

            bool mute = UiControls.Checkbox(l, "Mute", Radio.Muted,
                "Stop playback without downloading unheard audio.");
            if (mute != Radio.Muted) Radio.ToggleMute();

            bool stop = UiControls.Checkbox(l, "Stop on exit", Radio.StopOnExit,
                "Stop the daemon's playback when RimWorld exits normally.");
            if (stop != Radio.StopOnExit) Radio.ToggleStopOnExit();

        }

        // The recognition control makes the background lookup legible: a transient recognizing
        // state that names its input and can be cancelled, or a Recognize/Retry button that
        // surfaces the last failure instead of leaving it in a vanished toast.
        static void DrawRecognition(Listing_Standard l)
        {
            if (Radio.Recognizing)
            {
                if (UiLayout.Button(l, "Cancel recognition"))
                    Radio.CancelRecognition();
                string input = Radio.RecognizingInput;
                UiLayout.Note(l, string.IsNullOrEmpty(input)
                    ? "Recognizing…"
                    : "Recognizing via " + input + "…");
                return;
            }

            string error = Radio.RecognitionError;
            if (UiLayout.Button(l,
                    string.IsNullOrEmpty(error) ? "Recognize" : "Retry recognition"))
                Radio.Recognize();

            if (!string.IsNullOrEmpty(error))
            {
                GUI.color = UiTheme.Bad;
                l.Label(error);
                GUI.color = Color.white;
            }
            else if (!string.IsNullOrEmpty(Radio.RecognizingInput))
            {
                UiLayout.Note(l, "Input: " + Radio.RecognizingInput);
            }
        }
    }
}
