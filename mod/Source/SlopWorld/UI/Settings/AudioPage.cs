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

        public void Load() => JukeboxPresetStore.Refresh();

        public void Draw(Rect rect)
        {
            _form.Draw(SettingsPageLayout.Body(rect), DrawFields);
            var foot = new UiLayout.Bar(SettingsPageLayout.Footer(rect));
            if (foot.Left("Add source", UiTheme.Btn.Primary)) AudioSourceDialog.OpenNew();
        }

        void DrawFields(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Volume");
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
            DrawSources(l);

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

        static void DrawSources(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Sources");
            UiLayout.Note(l, "Choose which sources appear in the jukebox.");
            l.Gap(UiTheme.GapS);

            // Keep the table inside the listing so its rows participate in the page's measured
            // scroll extent instead of relying on a fixed page width.
            var header = l.GetRect(UiTheme.RowH);
            DrawHeader(header);
            DrawSourceRow(l, "OST", Radio.OstSourceId, true, null);
            DrawSourceRow(l, "Spotify", Radio.SpotifySourceId, Radio.SpotifyAvailable, null);

            if (JukeboxPresetStore.Loading && JukeboxPresetStore.Items.Count == 0)
                UiLayout.Note(l, "Loading user sources...");
            else if (!string.IsNullOrEmpty(JukeboxPresetStore.Error) &&
                     JukeboxPresetStore.Items.Count == 0)
                UiLayout.Validation(l, JukeboxPresetStore.Error);

            foreach (var preset in JukeboxPresetStore.Items)
                DrawSourceRow(l, preset.Name, preset.Id, true, preset);
        }

        static void DrawHeader(Rect r)
        {
            float actionsW = ActionsWidth();
            float showX = r.xMax - actionsW - UiTheme.GapS - ShowWidth;
            using (WidgetState.Save())
            {
                GUI.color = UiTheme.Faint;
                UiText.RowLabel(new Rect(r.x, r.y, Mathf.Max(0f, showX - r.x), r.height), "Name");
                UiText.RowLabel(new Rect(showX, r.y, ShowWidth, r.height), "Show");
                UiText.RowLabel(new Rect(r.xMax - actionsW, r.y, actionsW, r.height), "Actions");
            }
        }

        const float ShowWidth = 84f;

        static float EditWidth => UiLayout.BtnW("Edit", 72f);
        static float RemoveWidth => UiLayout.BtnW("Remove", 90f);
        static float ActionsWidth() => EditWidth + UiTheme.GapS + RemoveWidth;

        static void DrawSourceRow(Listing_Standard l, string name, string id, bool available,
                                  JukeboxPresetInfo preset)
        {
            var row = l.GetRect(UiTheme.RowH);
            float actionsW = ActionsWidth();
            float showW = ShowWidth;
            float nameW = Mathf.Max(0f, row.width - actionsW - showW - UiTheme.GapS * 2f);
            var nameRect = new Rect(row.x, row.y, nameW, row.height);
            var showRect = new Rect(nameRect.xMax + UiTheme.GapS, row.y, showW, row.height);
            var editRect = new Rect(row.xMax - actionsW, row.y, EditWidth, row.height);
            var removeRect = new Rect(editRect.xMax + UiTheme.GapS, row.y,
                RemoveWidth, row.height);

            using (WidgetState.Save())
            {
                GUI.color = available ? UiTheme.Name : UiTheme.Faint;
                UiText.RowLabel(nameRect, name);
                GUI.color = Color.white;
            }

            bool shown = Radio.SourceShown(id);
            bool next = UiControls.Checkbox(showRect, "", shown,
                available ? null : "ncspot is not available on the daemon host", !available);
            if (available && next != shown) Radio.SetSourceShown(id, next);

            if (preset != null)
            {
                if (UiButtons.Button(editRect, "Edit", UiTheme.Btn.Ghost))
                    AudioSourceDialog.Open(preset);
                if (UiButtons.Button(removeRect, "Remove", UiTheme.Btn.Danger))
                    ConfirmRemove(preset);
            }
        }

        static void ConfirmRemove(JukeboxPresetInfo preset)
        {
            Find.WindowStack.Add(ConfirmDialog.Create(
                $"Remove source '{preset.Name}'? This deletes its user preset from the daemon.",
                () => JukeboxPresetStore.Remove(preset.Id, null, UiLayout.Fail),
                destructive: true));
        }

        // The recognition control makes the background lookup legible. A transient recognizing
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
