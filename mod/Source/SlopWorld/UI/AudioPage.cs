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
            var body = SlopWidgets.PageBody(rect);
            body.height += SlopWidgets.BtnH + SlopWidgets.GapS;
            var inner = body.ContractedBy(SlopWidgets.GapM);

            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(inner);

            SlopWidgets.SectionHeading(l, "Volume");
            Prefs.VolumeMaster = SlopWidgets.Slider(l, "MasterVolume".Translate(),
                Prefs.VolumeMaster, "MasterVolumeTooltip".Translate());
            Prefs.VolumeGame = SlopWidgets.Slider(l, "GameVolume".Translate(),
                Prefs.VolumeGame, "GameVolumeTooltip".Translate());
            Prefs.VolumeMusic = SlopWidgets.Slider(l, "MusicVolume".Translate(),
                Prefs.VolumeMusic, "MusicVolumeTooltip".Translate());
            Prefs.VolumeAmbient = SlopWidgets.Slider(l, "AmbientVolume".Translate(),
                Prefs.VolumeAmbient, "AmbientVolumeTooltip".Translate());
            Prefs.VolumeUI = SlopWidgets.Slider(l, "UIVolume".Translate(),
                Prefs.VolumeUI, "UIVolumeTooltip".Translate());

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Jukebox");

            var picked = Radio.Picked;
            string source = Radio.Muted ? "Muted" : picked == null
                ? "OST" : $"{picked.Name} {Radio.RateLabel(picked.Rate)}";
            if (SlopWidgets.Button(l, "Tune: " + source))
                Find.WindowStack.Add(new SlopMenu(Jukebox.StationOptions()));

            // Show the station's own line and any Shazam match as two rows, so a recognized
            // track never silently overwrites what the station actually reported.
            if (Radio.Muted)
            {
                SlopWidgets.Note(l, "Muted.");
            }
            else
            {
                string station = Radio.StationLine;
                SlopWidgets.Note(l, "Now playing: "
                    + (string.IsNullOrEmpty(station) ? "nothing" : station));
                if (Radio.Recognized)
                    SlopWidgets.Note(l, "Recognized: " + Radio.RecognizedLine);
            }

            l.Gap(SlopWidgets.GapS);
            DrawRecognition(l);

            l.Gap(SlopWidgets.GapS);
            if (SlopWidgets.Button(l, "Random"))
                Radio.PickRandom();
            if (SlopWidgets.Button(l, "Like current song"))
                Radio.Like();
            if (SlopWidgets.Button(l, "History"))
                JukeboxHistoryView.Open();

            l.Gap(SlopWidgets.GapS);
            bool mute = SlopWidgets.Checkbox(l, "Mute", Radio.Muted,
                "Stop playback without downloading unheard audio.");
            if (mute != Radio.Muted) Radio.ToggleMute();

            bool stop = SlopWidgets.Checkbox(l, "Stop on exit", Radio.StopOnExit,
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
                if (SlopWidgets.Button(l, "Cancel recognition"))
                    Radio.CancelRecognition();
                string input = Radio.RecognizingInput;
                SlopWidgets.Note(l, string.IsNullOrEmpty(input)
                    ? "Recognizing…"
                    : "Recognizing via " + input + "…");
                return;
            }

            string error = Radio.RecognitionError;
            if (SlopWidgets.Button(l,
                    string.IsNullOrEmpty(error) ? "Recognize" : "Retry recognition"))
                Radio.Recognize();

            if (!string.IsNullOrEmpty(error))
            {
                GUI.color = SlopWidgets.Bad;
                l.Label(error);
                GUI.color = Color.white;
            }
            else if (!string.IsNullOrEmpty(Radio.RecognizingInput))
            {
                SlopWidgets.Note(l, "Input: " + Radio.RecognizingInput);
            }
        }
    }
}
