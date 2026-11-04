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
        public static IContentView ReturnTo;

        public static void Open(TaskInfo task, IContentView returnTo = null)
        {
            Shown = task;
            ReturnTo = returnTo;
        }
    }

    sealed partial class SessionHub
    {
        public readonly TaskStore TaskStore = new TaskStore(() => 0, error => throw error);
        public List<TaskInfo> Tasks => TaskStore.Tasks;
    }

    public sealed partial class PlannerView
    {
        internal static PlannerView CreateTest(string endpoint) => new PlannerView(endpoint);

        internal bool BusyTest => _state.Busy;
        internal object DraftIdentityTest => _draft;
        internal object FieldsIdentityTest => _fields;
        internal bool ReadyTest => Ready;
        internal string TextTest => _state.Text;
        internal string ErrorTest => _state.Error;
        internal string PriorityTest => _draft.Priority;

        internal Action PriorityEditTest(string value) =>
            EditSelected(() => _draft.Priority = value);

        internal void SelectTest(int index) => Select(_state.Entries[index]);

        internal Wire.PlannerDocument DocumentTest => _state.Document;

        internal void SaveTest(string text)
        {
            _state.Text = text;
            Save();
        }

        internal void LoadTest() => Load();

        internal void DiscardTest() => Discard();

        internal void AddTest(string text, string project = "", string context = "")
        {
            _quickAdd = text;
            _projectFilter = project;
            _contextFilter = context;
            Add();
        }

        internal string QuickAddTest => _quickAdd;
        internal string SelectedTitleTest => _selected?.Title;
        internal bool DetailOpenTest => _detailOpen;
        internal bool SourceTest => _source;

        internal void ToggleTest(int index) => Toggle(_state.Entries[index]);

        internal void DeleteTest() => DeleteSelected();

        internal void ToggleSourceTest() => ToggleSource();

        internal bool DirtyTest => Dirty;
        internal bool CanSaveTest => CanSave;

        internal void OpenTaskTest(string id) => OpenTask(id);

        internal string DescriptionTest
        {
            get => SelectedDescription.Text;
            set => SelectedDescription.Text = value;
        }
        internal string DescriptionErrorTest => SelectedDescription.Error;
        internal bool DescriptionBusyTest => SelectedDescription.Busy;
        internal bool DescriptionDirtyTest => SelectedDescription.Dirty;

        internal void SaveDescriptionTest() => Save();

        internal void ReloadDescriptionTest() => ReloadDescription();

        internal string DescriptionRevisionTest => SelectedDescription.Revision;

        internal void RemoveLinkTest(int index) => RemoveLink(Linked(_selected).Links[index]);

        internal void AddLinkTest(string kind, string id) => AddLink(kind, id, id);

        void DrawBoard(Rect body)
        {
            // Rendering is excluded; retain reads of rendering-only fields.
            _ = (_filter, _search, _detailOpen, _sort);
        }
    }
}
