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
            SlopWidgets.PageCaption(rect,
                "Sound levels for RimWorld and the colony's jukebox.");

            var body = SlopWidgets.PageBody(rect);
            body.height += SlopWidgets.BtnH + SlopWidgets.GapS;
            SlopWidgets.Card(body);
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
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH), "Tune: " + source))
                Find.WindowStack.Add(new SlopMenu(Jukebox.StationOptions()));

            string now = Radio.NowPlaying;
            SlopWidgets.Note(l, string.IsNullOrEmpty(now)
                ? "Nothing is playing."
                : "Now playing: " + now);

            l.Gap(SlopWidgets.GapS);
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH), "Random"))
                Radio.PickRandom();
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH), "Like current song"))
                Radio.Like();

            l.Gap(SlopWidgets.GapS);
            bool mute = SlopWidgets.Checkbox(l, "Mute", Radio.Muted,
                "Stop playback without downloading unheard audio.");
            if (mute != Radio.Muted) Radio.ToggleMute();

            bool stop = SlopWidgets.Checkbox(l, "Stop on exit", Radio.StopOnExit,
                "Stop the daemon's playback when RimWorld exits normally.");
            if (stop != Radio.StopOnExit) Radio.ToggleStopOnExit();

            l.End();
        }
    }
}
