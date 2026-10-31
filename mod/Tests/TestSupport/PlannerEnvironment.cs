using System;
using System.Collections.Generic;
using UnityEngine;

namespace Verse
{
    static partial class Text
    {
        public static bool WordWrap;
    }

    static class PlannerRectExtensions
    {
        public static Rect ContractedBy(this Rect rect, float amount) =>
            new Rect(
                rect.x + amount,
                rect.y + amount,
                rect.width - 2 * amount,
                rect.height - 2 * amount
            );
    }
}

namespace SlopWorld
{
    // Exercise the real request/draft lifecycle without linking IMGUI rendering.
    sealed class ScrollableListing { }

    static class TaskDetailView
    {
        public static TaskInfo Shown;

        public static void Open(TaskInfo task) => Shown = task;
    }

    sealed partial class SessionHub
    {
        public readonly TaskStore TaskStore = new TaskStore(() => 0, error => throw error);
        public List<TaskInfo> Tasks => TaskStore.Tasks;
    }

    public sealed partial class PlannerView
    {
        internal static PlannerView CreateTest(string endpoint) => new PlannerView(endpoint);

        internal bool BusyTest => _busy;
        internal bool ReadyTest => Ready;
        internal string TextTest => _text;
        internal string OutputTest => _output;
        internal string ErrorTest => _error;
        internal string PriorityTest => _draft.Priority;

        internal Action PriorityEditTest(string value) =>
            EditSelected(() => _draft.Priority = value);

        internal void SelectTest(int index) => Select(_entries[index]);

        internal Wire.PlannerDocument DocumentTest => _document;

        internal void SaveTest(string text)
        {
            _text = text;
            Save();
        }

        internal void RunTest(string action) => Run(action);

        internal void LoadTest() => Load();

        internal void OpenTaskTest(string id) => OpenTask(id);

        void DrawBoard(Rect body)
        {
            // Rendering is excluded; retain reads of rendering-only fields.
            _ = (_assistant, _filter, _search, _detailOpen, _sort);
        }
    }
}
