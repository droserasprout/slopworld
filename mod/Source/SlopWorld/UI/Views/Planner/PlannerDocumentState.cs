using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Owns one endpoint's backlog, description drafts, revisions, and request lifetime.
    // PlannerView owns selection, field editors, and navigation/presentation lifetime.
    internal sealed partial class PlannerDocumentState
    {
        internal readonly string Endpoint;
        internal Wire.PlannerDocument Document { get; private set; }
        internal string Text = "";
        internal string Error;
        internal List<TodoEntry> Entries { get; private set; } = new List<TodoEntry>();

        internal enum RequestStage
        {
            Idle,
            Loading,
            SavingBacklog,
            SavingDescriptions,
            SavingLinks,
            LoadingTask,
        }

        internal RequestStage Stage { get; private set; }
        internal bool Busy => Stage != RequestStage.Idle;
        internal int Generation { get; private set; }
        internal bool Ready => !Busy && Document != null;
        internal bool Dirty =>
            Document != null
            && (Text != Document.Text || _descriptions.Values.Any(draft => draft.Dirty));
        internal bool NeedsIds => Entries.Any(entry => string.IsNullOrEmpty(entry.Id));
        internal event Action Rebuilt;

        internal PlannerDocumentState(string endpoint) => Endpoint = endpoint;

        internal void Parse() => Entries = TodoText.Parse(Text);

        int Begin(RequestStage stage)
        {
            Stage = stage;
            Error = null;
            return ++Generation;
        }

        void Failed(int generation, string error)
        {
            if (!Current(generation))
                return;
            Stage = RequestStage.Idle;
            Error = error;
        }

        internal bool Current(int generation) => Generation == generation;

        void Accept(Wire.PlannerDocument document, bool discard = false)
        {
            // Link-only refreshes must preserve rows, selection, and field lifetime.
            bool rebuild = Document == null || Text != document.Text || discard;
            Document = document;
            Text = document.Text;
            if (rebuild)
            {
                Parse();
                Rebuilt?.Invoke();
            }
        }

        internal void Load(Func<bool> fieldDirty)
        {
            if (Busy)
                return;
            // Background refresh must leave editing enabled.
            bool initial = Document == null;
            int generation = initial ? Begin(RequestStage.Loading) : ++Generation;
            DaemonClient.Get<Wire.PlannerDocument>(
                WireProtocol.Routes.Planner,
                value =>
                {
                    if (!Current(generation))
                        return;
                    // Keep edits made during refresh and their revision for conflict detection.
                    if (!initial && (Dirty || fieldDirty()))
                        return;
                    Error = null;
                    Accept(value);
                    Stage = RequestStage.Idle;
                },
                error => Failed(generation, error)
            );
        }

        internal void Save()
        {
            if (Endpoint != DaemonClient.BaseUrl || !Ready)
                return;
            var ids = new HashSet<string>(TodoText.Parse(Text).Select(entry => entry.Id));
            if (_descriptions.Any(pair => pair.Value.Dirty && !ids.Contains(pair.Key)))
            {
                Error =
                    "Restore the removed task and save or discard its description before deleting it.";
                return;
            }
            int generation = Begin(RequestStage.SavingBacklog);
            SaveBacklog(generation);
        }

        // Commit the backlog before descriptions so new IDs and membership are accepted.
        void SaveBacklog(int generation)
        {
            if (Text == Document.Text && !NeedsIds)
            {
                SaveDescriptions(generation);
                return;
            }
            DaemonClient.Put<Wire.PlannerDocument>(
                WireProtocol.Routes.Planner,
                new Wire.PlannerSaveReq { Text = Text, Revision = Document.Revision },
                value =>
                {
                    if (Current(generation))
                    {
                        // Keep the save active while publishing the committed backlog.
                        Accept(value);
                        SaveDescriptions(generation);
                    }
                },
                error => Failed(generation, error)
            );
        }

        internal void Discard()
        {
            foreach (var draft in _descriptions.Values)
                draft.Discard();
            Error = null;
            Accept(Document, discard: true);
        }
    }
}
