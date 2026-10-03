using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A deliberately small editor for user station definitions. Each stream keeps its own
    // bitrate and URL, so multi-bitrate presets survive an edit and can be extended here.
    public sealed class AudioSourceDialog : UiWindow
    {
        readonly bool _isNew;
        readonly string _originalId;
        readonly JukeboxPresetInfo _source;
        readonly List<string> _rates = new List<string>();
        readonly ScrollableListing _listing = new ScrollableListing(420f);
        string _error;
        int _removeIndex = -1;

        AudioSourceDialog(JukeboxPresetInfo source, bool isNew, string originalId)
        {
            resizeable = true;
            _source = source;
            _isNew = isNew;
            _originalId = originalId;
            EnsureStreams();
            foreach (var stream in _source.Streams)
                _rates.Add(stream.Rate.ToString(CultureInfo.InvariantCulture));
            AcceptOnEnter(Save);
        }

        public static void OpenNew() =>
            TerminalWindow.OpenOverPane(NewDialog());

        public static void Open(JukeboxPresetInfo source)
        {
            if (source == null)
            {
                UiLayout.Fail("source is no longer available");
                JukeboxPresetStore.Refresh();
                return;
            }
            TerminalWindow.OpenOverPane(new AudioSourceDialog(source.Copy(), false, source.Id));
        }

        static AudioSourceDialog NewDialog()
        {
            var source = new JukeboxPresetInfo
            {
                Id = JukeboxPresetInfo.NewId(JukeboxPresetStore.Items),
                Name = "New source",
                DefaultRate = 128,
            };
            source.Streams.Add(new JukeboxStreamInfo
            {
                Rate = 128,
                Key = source.Id + "-128",
            });
            return new AudioSourceDialog(source, true, null);
        }

        void EnsureStreams()
        {
            if (_source.Streams == null) _source.Streams = new List<JukeboxStreamInfo>();
            if (_source.Streams.Count == 0)
                _source.Streams.Add(new JukeboxStreamInfo { Rate = 128, Key = _source.Id + "-128" });
        }

        public override Vector2 InitialSize => new Vector2(600f, 520f);

        protected override Vector2 MinimumSize => new Vector2(520f, 340f);

        protected override void DoBody(Rect rect)
        {
            UiLayout.Title(TitleRect(rect), _isNew ? "Add source" : "Edit source");
            var form = new Rect(rect.x, rect.y + UiTheme.HeaderH + UiTheme.GapM,
                rect.width, rect.height - UiTheme.HeaderH - UiTheme.GapM - UiTheme.BtnH - UiTheme.GapS);
            _removeIndex = -1;
            _listing.Draw(form, DrawFields);
            if (_removeIndex >= 0)
            {
                _source.Streams.RemoveAt(_removeIndex);
                _rates.RemoveAt(_removeIndex);
                return;
            }

            if (!string.IsNullOrEmpty(_error))
                UiText.PlainStatusLabel(new Rect(rect.x, rect.yMax - UiTheme.BtnH - UiTheme.GapS -
                    UiTheme.LineH, rect.width, UiTheme.LineH), _error, UiTheme.Bad);

            var foot = new UiLayout.Bar(UiLayout.FooterBar(rect));
            if (foot.Left("Cancel", UiTheme.Btn.Ghost)) Close();
            if (foot.Right("Save", UiTheme.Btn.Primary)) Save();
        }

        void DrawFields(Listing_Standard l)
        {
            l.Label("Name");
            _source.Name = UiControls.Field(l, "jukebox.source.name", _source.Name ?? "");
            l.Label("Title regex (optional)");
            _source.TitleRegex = UiControls.Field(l, "jukebox.source.regex", _source.TitleRegex ?? "");
            l.Gap(UiTheme.GapS);
            UiLayout.SectionHeading(l, "Bitrate presets");

            for (int i = 0; i < _source.Streams.Count; i++)
            {
                var stream = _source.Streams[i];
                l.Label("Preset " + (i + 1));
                _rates[i] = UiControls.Field(l, "jukebox.source.rate." + i,
                    _rates[i] ?? stream.Rate.ToString(CultureInfo.InvariantCulture));
                l.Label("Stream URL");
                stream.Url = UiControls.Field(l, "jukebox.source.url." + i, stream.Url ?? "");
                if (i > 0 && UiLayout.Button(l, "Remove preset", UiTheme.Btn.Ghost))
                {
                    _removeIndex = i;
                    return;
                }
            }

            if (UiLayout.Button(l, "Add bitrate preset", UiTheme.Btn.Ghost))
            {
                uint rate = 128;
                _source.Streams.Add(new JukeboxStreamInfo
                {
                    Rate = rate,
                    // Allocate a stable key only once the edited bitrate is valid.
                    Key = "",
                });
                _rates.Add(rate.ToString(CultureInfo.InvariantCulture));
            }
            UiLayout.Note(l, "SlopWorld stores user sources in ~/.config/slopworld/jukebox/.");
        }

        void Save()
        {
            if (!AudioSourceDraft.TryPrepare(_source, _rates, out var candidate, out _error)) return;
            JukeboxPresetStore.Save(candidate, _isNew, _originalId,
                () => Close(), error => _error = error);
        }
    }
}
