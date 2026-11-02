using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    internal sealed partial class PlannerDocumentState
    {
        // Drafts belong to this endpoint and stable task ID, not the current selection.
        // Replies settle their captured draft even after the view closes or changes task.
        internal sealed class DescriptionDraft
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
                // Reload preserves dirty text but updates the revision for save/discard.
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

        internal DescriptionDraft Description(string id) =>
            id != null && _descriptions.TryGetValue(id, out var draft) ? draft : null;

        internal void EnsureDescription(string id)
        {
            if (
                Endpoint != DaemonClient.BaseUrl
                || string.IsNullOrEmpty(id)
                || _descriptions.ContainsKey(id)
            )
                return;
            var draft = new DescriptionDraft();
            _descriptions.Add(id, draft);
            LoadDescription(id, draft);
        }

        static string DescriptionRoute(string id) =>
            WireProtocol.Routes.PlannerDescription.Replace(":id", Uri.EscapeDataString(id));

        internal void LoadDescription(string id, DescriptionDraft draft)
        {
            if (Endpoint != DaemonClient.BaseUrl || !draft.Begin())
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
            Stage = RequestStage.SavingDescriptions;
            var pending = _descriptions.FirstOrDefault(pair => pair.Value.Dirty);
            var draft = pending.Value;
            if (draft == null)
            {
                Stage = RequestStage.Idle;
                return;
            }
            if (draft.Busy || draft.Revision == null)
            {
                Failed(generation, "Description is still loading. Try saving again.");
                return;
            }
            // Never send remaining drafts to a different endpoint.
            if (Endpoint != DaemonClient.BaseUrl)
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
