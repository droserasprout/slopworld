using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class PlannerView
    {
        void DrawDetails(Rect frame, bool narrow)
        {
            Slab.Fill(frame, UiTheme.Panel);
            var inner = frame.ContractedBy(UiTheme.GapM);
            float h = UiTheme.FieldH,
                gap = UiTheme.GapS;
            var header = new Rect(inner.x, inner.y, inner.width, h);
            var close = new Rect(header.xMax - h, header.y, h, h);
            TooltipHandler.TipRegion(close, "Close task details");
            if (UiLayout.IconButton(close, Icons.Cross))
            {
                _detailOpen = false;
                UiMenu.CloseAll();
                return;
            }
            header.xMax = close.x - gap;
            if (narrow)
            {
                if (
                    UiControls.Button(
                        new Rect(header.x, header.y, 110f, h),
                        "‹ Task list",
                        UiControls.Btn.Ghost
                    )
                )
                    _detailOpen = false;
            }
            else
                UiText.RowLabel(
                    new Rect(header.x, header.y, header.width - 100f, h),
                    _assistant ? "Assistant" : "Task details"
                );
            if (
                _output.Length > 0
                && UiControls.Button(
                    new Rect(header.xMax - 95f, header.y, 95f, h),
                    _assistant ? "Task details" : "Assistant",
                    UiControls.Btn.Ghost
                )
            )
                _assistant = !_assistant;
            inner.yMin = header.yMax + gap;
            if (_assistant)
            {
                UiText.Area(inner, "planner.result", _output, on: false);
                return;
            }
            if (_selected == null)
            {
                UiText.PlainStatusLabel(
                    inner,
                    "Select a task to edit its details or send it to an agent.",
                    UiTheme.Dim
                );
                return;
            }
            _details.Draw(inner, DrawTaskFields);
        }

        void DrawTaskFields(Listing_Standard l)
        {
            // Controls are named by stable task identity so delayed text input cannot target
            // a different task after selecting a new row.
            string key = "planner.task." + _selected.Key;
            l.Label("Task");
            _draft.Title = UiControls.Area(l, 76f, key + ".title", _draft.Title, on: Ready);
            _draft.Completed = UiControls.Checkbox(
                l.GetRect(UiTheme.RowH),
                "Completed",
                _draft.Completed,
                locked: !Ready
            );
            l.Gap(UiTheme.GapM);
            l.Label("Due date");
            _draft.Due = UiControls.Field(l, key + ".due", _draft.Due, on: Ready);
            var dates = l.GetRect(UiTheme.FieldH);
            float gap = UiTheme.GapXS,
                w = (dates.width - gap * 3) / 4;
            string[] labels = { "No date", "Today", "Tomorrow", "+7 days" };
            int[] days = { 0, 0, 1, 7 };
            for (int i = 0; i < labels.Length; i++)
                if (
                    UiControls.Button(
                        new Rect(dates.x + i * (w + gap), dates.y, w, dates.height),
                        labels[i],
                        UiControls.Btn.Ghost,
                        on: Ready
                    )
                )
                    _draft.Due = i == 0 ? "" : TodoText.Stamp(DateTime.Today.AddDays(days[i]));
            UiLayout.Note(l, "YYYY-MM-DD. Dates organize your list; execution is manual.");
            l.Gap(UiTheme.GapM);
            l.Label("Priority");
            using (WidgetState.Save())
            {
                GUI.enabled = Ready;
                UiSelector.DrawInline(
                    l.GetRect(UiTheme.FieldH),
                    _draft.Priority.Length == 0
                        ? "None"
                        : _draft.Priority + PriorityLabel(_draft.Priority),
                    PriorityOptions
                );
            }
            l.Gap(UiTheme.GapM);
            DrawTagSelector(l, true);
            DrawTagSelector(l, false);
            UiLayout.Note(l, "Select projects and contexts, or type a new name and press Enter.");
            UiLayout.Validation(l, _draft.Error);
            var actions = l.GetRect(UiTheme.FieldH);
            float half = (actions.width - UiTheme.GapS) / 2;
            if (
                UiControls.Button(
                    new Rect(actions.x, actions.y, half, actions.height),
                    "Save task",
                    UiControls.Btn.Primary,
                    on: Ready && Dirty && _draft.Error == null
                )
            )
                Save();
            if (
                UiControls.Button(
                    new Rect(actions.x + half + UiTheme.GapS, actions.y, half, actions.height),
                    "Discard edits",
                    on: Ready && DraftDirty
                )
            )
            {
                _draft = new TodoDraft(_selected);
                _error = null;
            }
            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "AGENT");
            var linked = Linked(_selected);
            string taskId = !string.IsNullOrEmpty(linked?.TaskId)
                ? linked.TaskId
                : _selected.TaskId;
            if (taskId.Length > 0)
            {
                UiLayout.Note(l, "Worker status: " + (linked?.Status ?? "unavailable"));
                var task = SessionHub.Instance.Tasks.FirstOrDefault(value => value.Id == taskId);
                if (!string.IsNullOrWhiteSpace(task?.Note))
                    UiLayout.Note(l, task.Note);
                if (
                    taskId != "pending"
                    && UiControls.Button(l.GetRect(UiTheme.FieldH), "Open worker task", on: Ready)
                )
                    OpenTask(taskId);
                if (taskId == "pending")
                    UiLayout.Note(
                        l,
                        "Dispatch is reserved. Refresh and inspect Tasks before retrying."
                    );
            }
            else
            {
                l.Label("Project checkout");
                using (WidgetState.Save())
                {
                    GUI.enabled = Ready;
                    UiSelector.DrawInline(
                        l.GetRect(UiTheme.FieldH),
                        SessionHub
                            .Instance.Projects.FirstOrDefault(project => project.Id == _project)
                            ?.Name
                            ?? "Choose project…",
                        () =>
                            SessionHub
                                .Instance.Projects.Select(project => new FloatMenuOption(
                                    project.Name,
                                    EditSelected(() => _project = project.Id)
                                ))
                                .ToList()
                    );
                    l.Gap(UiTheme.GapS);
                    l.Label("Worker template");
                    UiSelector.DrawInline(
                        l.GetRect(UiTheme.FieldH),
                        _template.Length == 0 ? "Choose template…" : _template,
                        () =>
                            SessionHub
                                .Instance.Templates.Select(template => new FloatMenuOption(
                                    template.DisplayLabel,
                                    EditSelected(() => _template = template.Name)
                                ))
                                .ToList()
                    );
                }
                l.Gap(UiTheme.GapS);
                if (
                    UiControls.Button(
                        l.GetRect(UiTheme.FieldH),
                        "Execute",
                        UiControls.Btn.Primary,
                        on: Ready
                            && !Dirty
                            && !_selected.Completed
                            && _selected.Id.Length > 0
                            && _project.Length > 0
                            && _template.Length > 0
                    )
                )
                    Run("execute");
                UiLayout.Note(
                    l,
                    "Prepare a work order and start a worker in the project's main checkout."
                );
            }
            l.Gap(UiTheme.GapL);
            if (
                UiControls.Button(
                    l.GetRect(UiTheme.FieldH),
                    "Delete task…",
                    UiControls.Btn.Danger,
                    on: Ready && !Dirty
                )
            )
            {
                string selected = _selected.Key;
                int generation = _generation;
                int presentation = _presentation;
                Find.WindowStack.Add(
                    ConfirmDialog.Create(
                        "Delete “"
                            + _selected.Title
                            + "”? This removes the TODO entry. Any linked worker task is kept.",
                        () =>
                        {
                            if (
                                Current(generation)
                                && IsOpenEndpoint
                                && presentation == _presentation
                                && _selected?.Key == selected
                                && Ready
                                && !Dirty
                            )
                                DeleteSelected();
                        },
                        destructive: true
                    )
                );
            }
        }

        void DrawTagSelector(Listing_Standard listing, bool projects)
        {
            listing.Label(projects ? "Projects" : "Contexts");
            using (WidgetState.Save())
            {
                GUI.enabled = Ready;
                var box = listing.GetRect(UiTheme.FieldH);
                string value = projects ? _draft.Projects : _draft.Contexts;
                string prefix = projects ? "+" : "@";
                if (
                    UiControls.SelectInline(
                        box,
                        value.Length == 0
                            ? projects
                                ? "Choose projects…"
                                : "Choose contexts…"
                            : string.Join(", ", TodoText.Words(value).Select(name => prefix + name))
                    )
                )
                    OpenTagMenu(UiSelector.ScreenBox(box), projects);
            }
        }

        void OpenTagMenu(Rect anchor, bool projects)
        {
            var selected = new HashSet<string>(
                TodoText.Words(projects ? _draft.Projects : _draft.Contexts)
            );
            var names = _entries
                .SelectMany(entry => TodoText.Words(projects ? entry.Projects : entry.Contexts))
                .Concat(
                    projects
                        ? SessionHub.Instance.Projects.Select(project => project.Name)
                        : Enumerable.Empty<string>()
                )
                .Concat(selected)
                .Where(name => !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsWhiteSpace))
                .Distinct()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
            var options = names
                .Select(name =>
                    UiLayout.MenuToggle(
                        (projects ? "+" : "@") + name,
                        selected.Contains(name),
                        EditSelected(() =>
                        {
                            SetTag(name, projects, !selected.Contains(name));
                            OpenTagMenu(anchor, projects);
                        })
                    )
                )
                .ToList();
            // Bind submission to this draft before the menu starts accepting text.
            string newName = "";
            var add = EditSelected(() =>
            {
                SetTag(newName, projects, true);
                OpenTagMenu(anchor, projects);
            });
            options.Add(
                new UiMenuInput(
                    "New",
                    name =>
                    {
                        if (name.Length == 0)
                            return "Enter a name.";
                        return
                            name.Any(char.IsWhiteSpace)
                            || name.Contains(":")
                            || name[0] == '+'
                            || name[0] == '@'
                            ? "Use one name without spaces, +, @ or colons."
                            : null;
                    },
                    name =>
                    {
                        newName = name;
                        add();
                    }
                )
            );
            Find.WindowStack.Add(UiSelector.Menu(options, anchor));
        }

        void SetTag(string name, bool projects, bool selected)
        {
            var values = TodoText.Words(projects ? _draft.Projects : _draft.Contexts).ToList();
            values.RemoveAll(value => value == name);
            if (selected)
                values.Add(name);
            if (projects)
                _draft.Projects = string.Join(" ", values);
            else
                _draft.Contexts = string.Join(" ", values);
        }

        static string PriorityLabel(string priority) =>
            priority == "A" ? " · High"
            : priority == "B" ? " · Medium"
            : priority == "C" ? " · Low"
            : "";

        List<FloatMenuOption> PriorityOptions()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("None", EditSelected(() => _draft.Priority = "")),
            };
            for (char letter = 'A'; letter <= 'Z'; letter++)
            {
                string value = letter.ToString();
                options.Add(
                    new FloatMenuOption(
                        value + PriorityLabel(value),
                        EditSelected(() => _draft.Priority = value)
                    )
                );
            }
            return options;
        }
    }
}
