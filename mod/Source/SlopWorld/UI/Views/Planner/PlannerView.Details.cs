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
                UiText.RowLabel(header, "Task details");
            inner.yMin = header.yMax + gap;
            if (_selected == null)
            {
                UiText.PlainStatusLabel(inner, "Select a task to edit its details.", UiTheme.Dim);
                return;
            }
            _details.Draw(inner, DrawTaskFields);
        }

        void DrawTaskFields(Listing_Standard l)
        {
            // Stable control names keep delayed input from reaching another task.
            string key = "planner.task." + _selected.Key;
            _draft.Title = UiControls.Area(l, 76f, key + ".title", _draft.Title, on: Ready);
            l.Gap(UiTheme.GapS);
            DrawDescription(l);
            l.Gap(UiTheme.GapM);
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
            UiLayout.Note(l, "YYYY-MM-DD. Dates organize your list.");
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
                    "Save changes",
                    UiControls.Btn.Primary,
                    on: Ready && CanSave && _draft.Error == null
                )
            )
                Save();
            if (
                UiControls.Button(
                    new Rect(actions.x + half + UiTheme.GapS, actions.y, half, actions.height),
                    "Discard edits",
                    on: Ready && Dirty
                )
            )
                Discard();
            l.Gap(UiTheme.GapL);
            DrawLinks(l);
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
                            + "”? This removes the TODO entry. Its description and any linked worker task are kept.",
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

        void DrawDescription(Listing_Standard l)
        {
            var draft = SelectedDescription;
            if (draft == null)
            {
                UiLayout.Note(l, "Save the task before adding a description.");
                return;
            }
            draft.Text = UiControls.Area(
                l,
                240f,
                "planner.description." + _selected.Id,
                draft.Text,
                on: Ready && !draft.Busy && draft.Revision != null
            );
            UiLayout.Validation(l, draft.Error);
            if (
                UiControls.Button(
                    l.GetRect(UiTheme.FieldH),
                    "Reload description",
                    on: Ready && !draft.Busy
                )
            )
                ReloadDescription();
            if (draft.Dirty)
                UiLayout.Note(l, "Reload keeps your draft and updates the revision used by Save.");
            UiLayout.Note(
                l,
                draft.Busy ? "Working…"
                    : draft.Dirty ? "Unsaved description"
                    : ""
            );
            if (
                UiControls.Button(
                    l.GetRect(UiTheme.FieldH),
                    "Preview",
                    on: Ready && !draft.Busy && draft.Text.Length > 0
                )
            )
                WorkspaceWindow.OpenContent(new MarkdownPreview(draft.Text, _selected.Title), this);
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
