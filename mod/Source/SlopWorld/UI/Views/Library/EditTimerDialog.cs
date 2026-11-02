using System.Linq;
using Verse;

namespace SlopWorld
{
    public sealed class EditTimerDialog : EditLibraryItemDialog
    {
        internal EditTimerDialog(LibraryItemInfo existing, bool copy)
            : base(existing, copy, LibraryItemKind.Timer)
        {
            if (_s.Timer == null)
                _s.Timer = new Wire.TimerConfig { Schedule = "*/15 * * * *" };
        }

        protected override string TitleNoun => "timer";
        protected override string TextHeading =>
            "Runs in slopd while the game is closed. Busy or unready agents are skipped.";

        protected override void DrawKindFields(Listing_Standard l)
        {
            bool enabled = _s.Timer.Enabled;
            l.CheckboxLabeled("Enabled", ref enabled);
            _s.Timer.Enabled = enabled;
            l.Label("Cron schedule (UTC): minute hour day month weekday (0 = Sunday)");
            _s.Timer.Schedule = UiControls.Field(l, "timer.schedule", _s.Timer.Schedule);
            var agents = SessionHub
                .Instance.Sessions.Where(s => !s.Host && !s.Ephemeral && !s.Worker)
                .ToList();
            var target = agents.FirstOrDefault(s => s.StateId == _s.Timer.Target);
            UiControls.Select(
                l,
                "Target",
                string.IsNullOrEmpty(_s.Timer.Target)
                    ? "Choose an agent"
                    : target?.Name ?? "Missing agent",
                agents.Select(s => new SelectorOption(s.Name, () => _s.Timer.Target = s.StateId)),
                out _
            );
            UiControls.Select(
                l,
                "Library Prompt",
                _s.Timer.Prompt,
                SessionHub
                    .Instance.Library.Where(p => p.Kind == LibraryItemKind.Prompt)
                    .Select(p => new SelectorOption(p.Name, () => _s.Timer.Prompt = p.Name)),
                out _
            );
            DrawExplanation(
                l,
                "Missed occurrences are not replayed. Edit the selected Prompt in the Library to change future deliveries."
            );
        }
    }
}
