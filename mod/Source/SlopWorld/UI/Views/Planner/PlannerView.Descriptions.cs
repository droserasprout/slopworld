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
            public string Saved { get; private set; } = "";
            public string Revision { get; private set; }
            public string Error { get; private set; }
            public bool Busy { get; private set; }
            public bool Dirty => Text != Saved;

            public bool Begin()
            {
                if (Busy)
                    return false;
                Busy = true;
                Error = null;
                return true;
            }

            public void Accept(Wire.PlannerDescription value, bool reading)
            {
                // Reload rebases a local draft onto the latest accepted revision. Clean
                // drafts follow remote text; dirty drafts remain available for save/discard.
                if (!reading || !Dirty)
                    Text = value.Text;
                Saved = value.Text;
                Revision = value.Revision;
                Busy = false;
                Error = null;
            }

            public void Fail(string error)
            {
                Busy = false;
                Error = error;
            }

            public void Discard()
            {
                Text = Saved;
                Error = null;
            }
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

        void ReloadDescription()
        {
            var draft = SelectedDescription;
            if (!IsOpenEndpoint || !Ready || draft == null)
                return;
            LoadDescription(_selected.Id, draft);
        }

        void LoadDescription(string id, DescriptionDraft draft)
        {
            if (!IsOpenEndpoint || !draft.Begin())
                return;
            DaemonClient.Get<Wire.PlannerDescription>(
                DescriptionRoute(id),
                value => draft.Accept(value, reading: true),
                error => draft.Fail(error)
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
            // Closing the view permits settlement, but another endpoint must never
            // receive a continuation containing this endpoint's private draft.
            if (_endpoint != DaemonClient.BaseUrl)
            {
                Failed(
                    generation,
                    "Endpoint changed. Return to this daemon to save the remaining descriptions."
                );
                return;
            }
            draft.Begin();
            DaemonClient.Put<Wire.PlannerDescription>(
                DescriptionRoute(pending.Key),
                new Wire.PlannerSaveReq { Text = draft.Text, Revision = draft.Revision },
                value =>
                {
                    draft.Accept(value, reading: false);
                    SaveDescriptions(generation);
                },
                error =>
                {
                    draft.Fail(error);
                    Failed(generation, "Description was not saved: " + error);
                }
            );
        }
    }
}
