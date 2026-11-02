using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class PlannerView
    {
        static string FilterName(TodoFilter filter) =>
            filter == TodoFilter.Active ? "My tasks"
            : filter == TodoFilter.All ? "All tasks"
            : filter.ToString();

        void DrawBoard(Rect r)
        {
            float h = UiTheme.FieldH,
                gap = UiTheme.GapS;
            float tasksWidth = UiTheme.Wide("Tasks") + UiTheme.GapXS * 2f;
            float rawWidth = UiTheme.Wide("Raw") + UiTheme.GapXS * 2f;
            float separatorWidth = UiTheme.Wide("|") + UiTheme.GapXS * 2f;
            float switcherWidth = tasksWidth + separatorWidth + rawWidth;
            float x = r.xMax - 195f - gap - switcherWidth;
            var title = new Rect(r.x, r.y, Mathf.Max(80f, x - r.x - gap), h);
            UiText.RowLabel(title, "Planner", TextAnchor.MiddleLeft);
            using (WidgetState.Save())
            {
                GUI.enabled = Ready;
                bool tasks = ToggleCell.DrawText(
                    new Rect(x, r.y, tasksWidth, h),
                    "Tasks",
                    !_source,
                    "Show the task list"
                );
                x += tasksWidth;
                GUI.color = UiTheme.Dim;
                UiText.RowLabel(new Rect(x, r.y, separatorWidth, h), "|", TextAnchor.MiddleCenter);
                GUI.color = Color.white;
                x += separatorWidth;
                bool raw = ToggleCell.DrawText(
                    new Rect(x, r.y, rawWidth, h),
                    "Raw",
                    _source,
                    "Edit the raw todo.txt text"
                );
                if (Ready && ((_source && tasks) || (!_source && raw)))
                    ToggleSource();
                x += rawWidth + gap;
            }
            if (
                UiControls.Button(
                    new Rect(x, r.y, 90f, h),
                    Dirty ? "Save changes" : "Refresh",
                    on: !_busy && _document != null
                )
            )
            {
                if (Dirty)
                    Save();
                else
                    Load();
            }
            x += 95f;
            if (UiControls.Button(new Rect(x, r.y, 100f, h), "Discard", on: Ready && Dirty))
                Discard();
            float top = r.y + h + gap;
            string status = _busy
                ? "Working…"
                : _error
                    ?? (
                        Dirty
                            ? "Unsaved changes"
                            : "Saved · "
                                + _entries.Count(entry => !entry.Completed)
                                + " active tasks"
                    );
            var footer = new Rect(r.x, r.yMax - h, r.width, h);
            GUI.color = _error == null ? UiTheme.Dim : UiTheme.Bad;
            UiText.FadedRowLabel(footer, status);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(footer, _error ?? _document?.Path ?? "");
            var content = new Rect(r.x, top, r.width, Mathf.Max(0f, footer.y - top - gap));
            if (_document == null)
            {
                UiText.RowLabel(
                    content,
                    _busy ? "Loading tasks…" : "Could not load tasks. Use Refresh to try again."
                );
                if (!_busy && UiControls.Button(new Rect(content.x, content.y, 120f, h), "Retry"))
                    Load();
                return;
            }
            if (_source)
            {
                _text = UiText.Area(content, "planner.source", _text, on: Ready);
                return;
            }
            bool wide = content.width >= 980f;
            bool rail = content.width >= 760f;
            if (!wide && _detailOpen)
            {
                DrawDetails(content, true);
                return;
            }
            if (rail)
            {
                var navigation = new Rect(content.x, content.y, 165f, content.height);
                DrawNavigation(navigation);
                content.xMin = navigation.xMax + gap;
            }
            Rect details = default;
            bool showDetails = wide && _detailOpen;
            if (showDetails)
            {
                float detailW = Mathf.Clamp(content.width * 0.39f, 305f, 400f);
                details = new Rect(content.xMax - detailW, content.y, detailW, content.height);
                content.xMax = details.x - gap;
            }
            DrawTaskList(content, rail);
            if (showDetails)
                DrawDetails(details, false);
        }

        void DrawNavigation(Rect rect)
        {
            Slab.Fill(rect, UiTheme.Panel);
            _navigation.Draw(
                rect.ContractedBy(UiTheme.GapS),
                listing =>
                {
                    UiLayout.SectionHeading(listing, "TASKS");
                    foreach (TodoFilter filter in Enum.GetValues(typeof(TodoFilter)))
                    {
                        int count = _entries.Count(entry =>
                            TodoText.Matches(entry, filter, "", "", DateTime.Today)
                        );
                        var row = listing.GetRect(UiTheme.RowH);
                        if (
                            NavRow(
                                row,
                                FilterName(filter),
                                count,
                                _filter == filter
                                    && _projectFilter.Length == 0
                                    && _contextFilter.Length == 0
                            )
                        )
                        {
                            _filter = filter;
                            _projectFilter = "";
                            _contextFilter = "";
                            _listScroll.JumpTo(Vector2.zero);
                        }
                    }
                    listing.Gap(UiTheme.GapL);
                    UiLayout.SectionHeading(listing, "PROJECTS");
                    foreach (
                        string project in _entries
                            .SelectMany(entry => TodoText.Words(entry.Projects))
                            .Distinct()
                            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    )
                    {
                        int count = _entries.Count(entry =>
                            !entry.Completed && TodoText.Words(entry.Projects).Contains(project)
                        );
                        if (
                            NavRow(
                                listing.GetRect(UiTheme.RowH),
                                "+" + project,
                                count,
                                _projectFilter == project
                            )
                        )
                            FocusTagFilter(project: project);
                    }
                    if (_entries.All(entry => entry.Projects.Length == 0))
                        UiLayout.Note(listing, "Add projects in task details.");
                    listing.Gap(UiTheme.GapL);
                    UiLayout.SectionHeading(listing, "CONTEXTS");
                    foreach (
                        string context in _entries
                            .SelectMany(entry => TodoText.Words(entry.Contexts))
                            .Distinct()
                            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    )
                    {
                        int count = _entries.Count(entry =>
                            !entry.Completed && TodoText.Words(entry.Contexts).Contains(context)
                        );
                        if (
                            NavRow(
                                listing.GetRect(UiTheme.RowH),
                                "@" + context,
                                count,
                                _contextFilter == context
                            )
                        )
                            FocusTagFilter(context: context);
                    }
                    if (_entries.All(entry => entry.Contexts.Length == 0))
                        UiLayout.Note(listing, "Add contexts in task details.");
                }
            );
        }

        void FocusTagFilter(string project = "", string context = "")
        {
            _projectFilter = project;
            _contextFilter = context;
            _filter = TodoFilter.Active;
            _listScroll.JumpTo(Vector2.zero);
        }

        static bool NavRow(Rect rect, string name, int count, bool selected)
        {
            if (selected)
                Slab.Fill(rect, UiTheme.Well);
            GUI.color = selected ? UiTheme.Lead : UiTheme.Name;
            UiText.FadedRowLabel(
                new Rect(rect.x + 5f, rect.y, rect.width - 36f, rect.height),
                name
            );
            GUI.color = UiTheme.Dim;
            UiText.RowLabel(
                new Rect(rect.xMax - 30f, rect.y, 25f, rect.height),
                count.ToString(System.Globalization.CultureInfo.CurrentCulture),
                TextAnchor.MiddleRight
            );
            GUI.color = Color.white;
            return UiControls.NavigationPress(rect);
        }

        void DrawTaskList(Rect rect, bool rail)
        {
            float h = UiTheme.FieldH,
                gap = UiTheme.GapS,
                y = rect.y;
            string heading =
                _contextFilter.Length > 0 ? "@" + _contextFilter
                : _projectFilter.Length > 0 ? "+" + _projectFilter
                : FilterName(_filter);
            UiText.RowLabel(new Rect(rect.x, y, Mathf.Max(80f, rect.width - 125f), h), heading);
            UiSelector.DrawInline(
                new Rect(rect.xMax - 120f, y, 120f, h),
                "Sort: " + _sort.ToString().ToLowerInvariant(),
                () =>
                    Enum.GetValues(typeof(TodoSort))
                        .Cast<TodoSort>()
                        .Select(sort => new FloatMenuOption(sort.ToString(), () => _sort = sort))
                        .ToList()
            );
            y += h + gap;
            if (!rail)
            {
                UiSelector.DrawInline(
                    new Rect(rect.x, y, rect.width / 2 - gap, h),
                    FilterName(_filter),
                    () =>
                        Enum.GetValues(typeof(TodoFilter))
                            .Cast<TodoFilter>()
                            .Select(filter => new FloatMenuOption(
                                FilterName(filter),
                                () => _filter = filter
                            ))
                            .ToList()
                );
                UiSelector.DrawInline(
                    new Rect(rect.x + rect.width / 2, y, rect.width / 2, h),
                    _projectFilter.Length == 0 ? "All projects" : "+" + _projectFilter,
                    () =>
                        new[] { new FloatMenuOption("All projects", () => _projectFilter = "") }
                            .Concat(
                                _entries
                                    .SelectMany(entry => TodoText.Words(entry.Projects))
                                    .Distinct()
                                    .Select(project => new FloatMenuOption(
                                        "+" + project,
                                        () => _projectFilter = project
                                    ))
                            )
                            .ToList()
                );
                y += h + gap;
                UiSelector.DrawInline(
                    new Rect(rect.x, y, rect.width, h),
                    _contextFilter.Length == 0 ? "All contexts" : "@" + _contextFilter,
                    () =>
                        new[] { new FloatMenuOption("All contexts", () => _contextFilter = "") }
                            .Concat(
                                _entries
                                    .SelectMany(entry => TodoText.Words(entry.Contexts))
                                    .Distinct()
                                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                                    .Select(context => new FloatMenuOption(
                                        "@" + context,
                                        () => _contextFilter = context
                                    ))
                            )
                            .ToList()
                );
                y += h + gap;
            }
            _search = UiText.FieldWithPlaceholder(
                new Rect(rect.x, y, rect.width, h),
                "planner.search",
                _search,
                "Search tasks, projects and contexts…"
            );
            y += h + gap;
            _quickAdd = UiText.FieldWithPlaceholder(
                new Rect(rect.x, y, rect.width - 70f - gap, h),
                "planner.add",
                _quickAdd,
                "Add a task…",
                on: Ready && !Dirty
            );
            if (
                UiControls.Button(
                    new Rect(rect.xMax - 70f, y, 70f, h),
                    "+ Add",
                    UiControls.Btn.Primary,
                    on: Ready && !Dirty && !string.IsNullOrWhiteSpace(_quickAdd)
                )
            )
                Add();
            if (
                Ready
                && !Dirty
                && !string.IsNullOrWhiteSpace(_quickAdd)
                && GUI.GetNameOfFocusedControl() == "planner.add"
                && Event.current.type == EventType.KeyDown
                && (
                    Event.current.keyCode == KeyCode.Return
                    || Event.current.keyCode == KeyCode.KeypadEnter
                )
            )
            {
                Event.current.Use();
                Add();
            }
            y += h + gap;
            var frame = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y));
            var entries = TodoText
                .Sort(
                    _entries.Where(entry =>
                        TodoText.Matches(
                            entry,
                            _filter,
                            _projectFilter,
                            _search,
                            DateTime.Today,
                            _contextFilter
                        )
                    ),
                    _sort
                )
                .ToList();
            if (entries.Count == 0)
            {
                GUI.color = UiTheme.Dim;
                UiText.RowLabel(
                    new Rect(frame.x, frame.y + gap, frame.width, h),
                    _entries.Count == 0
                        ? "A clear place to start. Add your first task above."
                        : "No tasks match this view."
                );
                GUI.color = Color.white;
                return;
            }
            float rowH = UiTheme.LineHOf(GameFont.Small) + UiTheme.LineHOf(GameFont.Tiny) + 3 * gap;
            float groupH = UiTheme.TinyRowH + gap;
            var rows = new List<(TodoEntry Entry, string Group, float Y)>();
            string last = null;
            float total = 0;
            foreach (var entry in entries)
            {
                string group = _sort == TodoSort.Due ? TodoText.Group(entry, DateTime.Today) : null;
                if (group != last)
                {
                    rows.Add((null, group, total));
                    total += groupH;
                    last = group;
                }
                rows.Add((entry, null, total));
                total += rowH;
            }
            var geometry = UiScrollBody.Measure(frame, total, UiScrollbarReservation.WhenNeeded);
            using (_listScroll.Scope(frame, geometry.View))
            {
                if (SmoothScroll.WheelOnly)
                    return;
                foreach (var row in rows)
                {
                    float height = row.Entry == null ? groupH : rowH;
                    if (
                        row.Y + height < _listScroll.Position.y
                        || row.Y > _listScroll.Position.y + frame.height
                    )
                        continue;
                    var bounds = new Rect(
                        0f,
                        row.Y,
                        geometry.View.width - geometry.ScrollbarHitInset,
                        height
                    );
                    if (row.Entry == null)
                    {
                        Text.Font = GameFont.Tiny;
                        GUI.color = row.Group == "Overdue" ? UiTheme.Bad : UiTheme.Dim;
                        UiText.RowLabel(bounds, row.Group);
                        GUI.color = Color.white;
                        Text.Font = GameFont.Small;
                    }
                    else
                        DrawTaskRow(bounds, row.Entry);
                }
            }
        }

        void DrawTaskRow(Rect row, TodoEntry entry)
        {
            float gap = UiTheme.GapS;
            bool selected = _selected?.Key == entry.Key;
            if (selected || Mouse.IsOver(row))
                Slab.Fill(row, UiTheme.Well);
            var check = new Rect(row.x + gap, row.y + gap, 24f, 24f);
            var tint =
                entry.Completed ? UiTheme.Dim
                : entry.Priority == "A" ? UiTheme.Bad
                : entry.Priority == "B" ? UiTheme.Warn
                : UiTheme.Off;
            Slab.Box(check.ContractedBy(4f), UiTheme.Panel, tint);
            if (entry.Completed)
            {
                GUI.color = UiTheme.Lead;
                GUI.DrawTexture(check.ContractedBy(3f), Icons.Check);
                GUI.color = Color.white;
            }
            TooltipHandler.TipRegion(check, entry.Completed ? "Mark incomplete" : "Complete task");
            if (UiControls.RowButton(check, Ready && !Dirty))
            {
                Toggle(entry);
                return;
            }
            float left = check.xMax + gap;
            var title = new Rect(
                left,
                row.y + gap,
                row.xMax - left - gap,
                UiTheme.LineHOf(GameFont.Small)
            );
            GUI.color = entry.Completed ? UiTheme.Dim : UiTheme.Name;
            UiText.FadedRowLabel(
                title,
                string.IsNullOrEmpty(entry.Title) ? "Untitled task" : entry.Title
            );
            TooltipHandler.TipRegion(title, entry.Title);
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            float x = left,
                y = title.yMax + gap;
            if (entry.Due.Length > 0)
                Chip(
                    ref x,
                    y,
                    row.xMax,
                    entry.DueDate == DateTime.Today ? "Today" : entry.Due,
                    entry.DueDate < DateTime.Today && !entry.Completed ? UiTheme.Bad : UiTheme.Dim
                );
            if (entry.Priority.Length > 0)
                Chip(ref x, y, row.xMax, "Priority " + entry.Priority, tint);
            bool tagPressed = false;
            foreach (var project in TodoText.Words(entry.Projects))
                if (Chip(ref x, y, row.xMax, "+" + project, UiTheme.Info, clickable: true))
                {
                    FocusTagFilter(project: project);
                    tagPressed = true;
                }
            foreach (var context in TodoText.Words(entry.Contexts))
                if (Chip(ref x, y, row.xMax, "@" + context, UiTheme.Dim, clickable: true))
                {
                    FocusTagFilter(context: context);
                    tagPressed = true;
                }
            var linked = Linked(entry);
            if (linked != null && linked.Links.Count > 0)
                Chip(ref x, y, row.xMax, linked.Links.Count + " links", UiTheme.Lead);
            Text.Font = GameFont.Small;
            Slab.Hairline(new Rect(left, row.yMax - 1f, row.xMax - left, 1f), UiTheme.Edge);
            if (
                !tagPressed
                && UiControls.NavigationPress(
                    new Rect(left, row.y, row.xMax - left, row.height),
                    Ready
                )
            )
                Select(entry);
        }

        static bool Chip(
            ref float x,
            float y,
            float right,
            string text,
            Color color,
            bool clickable = false
        )
        {
            float width = UiTheme.Wide(text) + 10f;
            if (x + width > right - UiTheme.GapS)
                return false;
            var rect = new Rect(x, y, width, UiTheme.TinyRowH);
            bool hovered = clickable && Mouse.IsOver(rect);
            Slab.Box(rect, hovered ? UiTheme.Well : UiTheme.Panel, UiTheme.Edge);
            GUI.color = hovered ? UiTheme.Lead : color;
            UiText.RowLabel(rect.ContractedBy(4f, 0f), text);
            GUI.color = Color.white;
            x += width + UiTheme.GapXS;
            if (clickable)
                TooltipHandler.TipRegion(rect, "Filter tasks by " + text);
            return clickable && UiControls.NavigationPress(rect);
        }
    }
}
