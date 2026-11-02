using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SlopWorld
{
    public sealed class EditTimerDialog : EditLibraryItemDialog
    {
        string _days = "Mon..Fri",
            _time = "09:00";
        string _preview;
        Wire.TimerConfig _previewConfig;
        int _previewGeneration;
        bool _closed;

        internal EditTimerDialog(LibraryItemInfo existing, bool copy)
            : base(existing, copy, LibraryItemKind.Timer)
        {
            if (_s.Timer == null)
                _s.Timer = new Wire.TimerConfig
                {
                    Action = "prompt",
                    Trigger = "interval",
                    Schedule = "15min",
                    Timezone = TimeZonePreference.Resolve(Settings.TimeZone).Id,
                };
            if (_s.Timer.Trigger == "calendar")
            {
                var parts = _s.Timer.Schedule.Split(
                    new[] { ' ' },
                    StringSplitOptions.RemoveEmptyEntries
                );
                _time = parts.LastOrDefault() ?? "09:00";
                _days = parts.Length > 1 ? parts[0] : "*";
            }
        }

        protected override string TitleNoun => "timer";
        protected override string TextHeading => "Runs while the game is closed.";

        internal static string TriggerLabel(string trigger) =>
            trigger == "calendar" ? "Calendar"
            : trigger == "startup" ? "After daemon startup"
            : trigger == "active" ? "After activation"
            : trigger == "interval" ? "Repeat interval"
            : "Choose a trigger";

        protected override void DrawKindFields(Listing_Standard l)
        {
            _s.Timer.Enabled = UiControls.Checkbox(l, "Enabled", _s.Timer.Enabled);
            var kinds = new List<string> { "calendar", "startup", "active", "interval" };
            UiControls.Select(
                l,
                "When",
                TriggerLabel(_s.Timer.Trigger),
                kinds.Select(kind => new SelectorOption(
                    TriggerLabel(kind),
                    () =>
                    {
                        _s.Timer.Trigger = kind;
                        _s.Timer.Schedule = kind == "calendar" ? _days + " " + _time : "15min";
                        if (string.IsNullOrEmpty(_s.Timer.Timezone))
                            _s.Timer.Timezone = TimeZonePreference.Resolve(Settings.TimeZone).Id;
                    }
                )),
                out _
            );
            if (_s.Timer.Trigger == "calendar")
            {
                l.Label("Days: * for daily; Mon..Fri or Mon,Wed,Fri");
                _days = UiControls.Field(l, "timer.days", _days);
                l.Label("Time (24-hour HH:MM)");
                _time = UiControls.Field(l, "timer.time", _time);
                _s.Timer.Schedule = _days + " " + _time;
                if (UiControls.Button(UiControls.FieldRect(l), "Time zone: " + _s.Timer.Timezone))
                    WorkspaceWindow.OpenOverPane(new UiMenu(ZoneOptions().ToList()));
            }
            else
            {
                l.Label(
                    _s.Timer.Trigger == "interval" ? "Every (for example 15min or 1h 30min)"
                    : _s.Timer.Trigger == "startup" || _s.Timer.Trigger == "active"
                        ? "Delay (for example 30s or 5min)"
                    : "Choose a trigger above"
                );
                _s.Timer.Schedule = UiControls.Field(l, "timer.schedule", _s.Timer.Schedule);
            }
            DrawAction(l);
            if (UiControls.Button(UiControls.FieldRect(l), "Preview schedule"))
                Preview();
            if (_preview != null && _s.Timer.Equals(_previewConfig))
                DrawExplanation(l, _preview);
            DrawExplanation(
                l,
                "Prompt actions skip busy or stopped agents. Missed runs are skipped. Activation delays and intervals restart on enable, timer edits, or daemon restart."
            );
        }

        internal static string ActionLabel(string action) =>
            action == "prompt" ? "Prompt"
            : action == "worker" ? "Worker"
            : action == "shell" ? "Shell script"
            : "Choose an action";

        void DrawAction(Listing_Standard l)
        {
            UiControls.Select(
                l,
                "Action",
                ActionLabel(_s.Timer.Action),
                new[] { "prompt", "worker", "shell" }.Select(action => new SelectorOption(
                    ActionLabel(action),
                    () =>
                    {
                        if (_s.Timer.Action == action)
                            return;
                        _s.Timer.Action = action;
                        _s.Timer.Target = "";
                        _s.Timer.Template = "";
                        _s.Timer.Project = "";
                        _s.Timer.Prompt = "";
                        _s.Timer.Shell = "";
                    }
                )),
                out _
            );
            if (_s.Timer.Action == "prompt")
            {
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
                    TargetOptions(agents),
                    out _
                );
            }
            if (_s.Timer.Action == "worker")
            {
                UiControls.Select(
                    l,
                    "Template",
                    string.IsNullOrEmpty(_s.Timer.Template)
                        ? "Choose a template"
                        : _s.Timer.Template,
                    SessionHub.Instance.Catalog.Templates.Select(t => new SelectorOption(
                        t.Name,
                        () => _s.Timer.Template = t.Name
                    )),
                    out _
                );
                UiControls.Select(
                    l,
                    "Project",
                    string.IsNullOrEmpty(_s.Timer.Project) ? "Choose a project" : _s.Timer.Project,
                    SessionHub.Instance.Projects.Select(p => new SelectorOption(
                        p.Name,
                        () => _s.Timer.Project = p.Name
                    )),
                    out _
                );
            }
            if (_s.Timer.Action == "prompt" || _s.Timer.Action == "worker")
                UiControls.Select(
                    l,
                    _s.Timer.Action == "worker" ? "Task from prompt" : "Library Prompt",
                    string.IsNullOrEmpty(_s.Timer.Prompt) ? "Choose a prompt" : _s.Timer.Prompt,
                    PromptOptions(),
                    out _
                );
            if (_s.Timer.Action == "shell")
            {
                UiControls.Select(
                    l,
                    "Shell script",
                    string.IsNullOrEmpty(_s.Timer.Shell) ? "Choose a shell script" : _s.Timer.Shell,
                    SessionHub
                        .Instance.Library.Where(p => p.Kind == LibraryItemKind.Shell)
                        .Select(p => new SelectorOption(p.Name, () => _s.Timer.Shell = p.Name)),
                    out _
                );
                DrawExplanation(
                    l,
                    "Uses the script's saved location and execution settings. Scripts set to Ask need a saved location."
                );
            }
        }

        IEnumerable<FloatMenuOption> TargetOptions(IEnumerable<SessionInfo> agents)
        {
            foreach (
                var group in agents
                    .GroupBy(s => s.Project)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            )
                yield return new UiSubmenu(
                    string.IsNullOrEmpty(group.Key) ? "No project" : group.Key,
                    () =>
                        group
                            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                            .Select(s => new FloatMenuOption(
                                s.Name,
                                () => _s.Timer.Target = s.StateId
                            ))
                            .ToList()
                );
        }

        IEnumerable<SelectorOption> PromptOptions()
        {
            foreach (
                var prompt in SessionHub.Instance.Library.Where(p =>
                    p.Kind == LibraryItemKind.Prompt
                )
            )
                yield return new SelectorOption(prompt.Name, () => _s.Timer.Prompt = prompt.Name);
            yield return new SelectorOption(
                "New",
                () =>
                {
                    string action = _s.Timer.Action;
                    string previous = _s.Timer.Prompt;
                    var dialog = New(LibraryItemKind.Prompt);
                    dialog.Saved = name =>
                    {
                        if (!_closed && _s.Timer.Action == action && _s.Timer.Prompt == previous)
                            _s.Timer.Prompt = name;
                    };
                    WorkspaceWindow.OpenOverPane(dialog);
                }
            );
        }

        IEnumerable<FloatMenuOption> ZoneOptions()
        {
            string preferred = TimeZonePreference.Resolve(Settings.TimeZone).Id;
            yield return new FloatMenuOption(
                "Use Settings time zone (" + preferred + ")",
                () => _s.Timer.Timezone = preferred
            );
            yield return new FloatMenuOption("UTC", () => _s.Timer.Timezone = "UTC");
            foreach (
                var group in TimeZoneInfo
                    .GetSystemTimeZones()
                    .OrderBy(zone => zone.Id)
                    .Where(zone => zone.Id != "UTC")
                    .GroupBy(zone => zone.Id.Contains("/") ? zone.Id.Split('/')[0] : "Other")
            )
                yield return new UiSubmenu(
                    group.Key,
                    () =>
                        group
                            .Select(zone => new FloatMenuOption(
                                zone.Id,
                                () => _s.Timer.Timezone = zone.Id
                            ))
                            .ToList()
                );
        }

        void Preview()
        {
            int generation = ++_previewGeneration;
            _previewConfig = _s.Timer.Clone();
            _preview = "Loading preview…";
            DaemonClient.Post<Wire.TimerPreview>(
                WireProtocol.Routes.TimerPreview,
                _previewConfig,
                result =>
                {
                    if (generation != _previewGeneration)
                        return;
                    _preview =
                        "If activated now:\n"
                        + string.Join(
                            "\n",
                            result.NextAt.Select(at =>
                                Settings.TimeStamp(
                                    DateTimeOffset.FromUnixTimeSeconds((long)at).UtcDateTime
                                )
                            )
                        );
                },
                error =>
                {
                    if (generation == _previewGeneration)
                        _preview = error;
                }
            );
        }

        public override void PostClose()
        {
            _closed = true;
            ++_previewGeneration;
            base.PostClose();
        }
    }
}
