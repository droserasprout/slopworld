using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public sealed partial class PlannerView
    {
        // Drafts belong to this endpoint and stable task ID, not the current selection.
        // Replies settle their captured draft even after the view closes or changes task.
        sealed class DescriptionDraft
        {
            public string Text = "";
            public string Saved = "";
            public string Revision;
            public string Error;
            public bool Busy;
            public bool Dirty => Text != Saved;
        }

        readonly Dictionary<string, DescriptionDraft> _descriptions =
            new Dictionary<string, DescriptionDraft>();

        DescriptionDraft SelectedDescription =>
            _selected != null && _descriptions.TryGetValue(_selected.Id, out var draft)
                ? draft
                : null;

        static string DescriptionRoute(string id) =>
            WireProtocol.Routes.PlannerDescription.Replace(":id", Uri.EscapeDataString(id));

        void EnsureDescription()
        {
            string id = _selected?.Id;
            if (!IsOpenEndpoint || string.IsNullOrEmpty(id) || _descriptions.ContainsKey(id))
                return;
            var draft = new DescriptionDraft();
            _descriptions.Add(id, draft);
            LoadDescription(id, draft);
        }

        void LoadDescription(string id, DescriptionDraft draft)
        {
            if (draft.Busy || !IsOpenEndpoint)
                return;
            draft.Busy = true;
            draft.Error = null;
            DaemonClient.Get<Wire.PlannerDescription>(
                DescriptionRoute(id),
                value => AcceptDescription(draft, value),
                error => DescriptionFailed(draft, error)
            );
        }

        // Save every cached draft so switching tasks cannot hide unsaved descriptions.
        // Advance each revision only after its write succeeds; failures remain retryable.
        void SaveDescriptions(int generation)
        {
            if (!Current(generation))
                return;
            var pending = _descriptions.FirstOrDefault(pair => pair.Value.Dirty);
            var draft = pending.Value;
            if (draft == null)
            {
                _busy = false;
                return;
            }
            if (draft.Busy || draft.Revision == null)
            {
                Failed(generation, "Description is still loading. Try saving again.");
                return;
            }
            draft.Busy = true;
            draft.Error = null;
            DaemonClient.Put<Wire.PlannerDescription>(
                DescriptionRoute(pending.Key),
                new Wire.PlannerSaveReq { Text = draft.Text, Revision = draft.Revision },
                value =>
                {
                    AcceptDescription(draft, value);
                    SaveDescriptions(generation);
                },
                error =>
                {
                    DescriptionFailed(draft, error);
                    Failed(generation, "Description was not saved: " + error);
                }
            );
        }

        static void AcceptDescription(DescriptionDraft draft, Wire.PlannerDescription value)
        {
            draft.Text = draft.Saved = value.Text;
            draft.Revision = value.Revision;
            draft.Busy = false;
            draft.Error = null;
        }

        static void DescriptionFailed(DescriptionDraft draft, string error)
        {
            draft.Busy = false;
            draft.Error = error;
        }
    }
}
