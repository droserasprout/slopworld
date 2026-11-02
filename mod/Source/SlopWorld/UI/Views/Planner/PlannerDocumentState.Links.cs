using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    internal sealed partial class PlannerDocumentState
    {
        internal void SaveLinks(Wire.PlannerItem selected, IEnumerable<Wire.PlannerLink> links)
        {
            var request = new Wire.PlannerLinksReq { Revision = selected.LinksRevision };
            request.Links.AddRange(links.Select(link => link.Clone()));
            int generation = Begin(RequestStage.SavingLinks);
            DaemonClient.Put<Wire.PlannerDocument>(
                WireProtocol.Routes.PlannerLinks.Replace(":id", Uri.EscapeDataString(selected.Id)),
                request,
                value =>
                {
                    if (!Current(generation))
                        return;
                    Accept(value);
                    Stage = RequestStage.Idle;
                },
                error => Failed(generation, error)
            );
        }

        internal void LoadTask(string id, Action<Wire.Task> accepted)
        {
            int generation = Begin(RequestStage.LoadingTask);
            DaemonClient.Get<Wire.TaskResult>(
                WireProtocol.Routes.Tasks + "/" + Uri.EscapeDataString(id),
                value =>
                {
                    if (!Current(generation))
                        return;
                    Stage = RequestStage.Idle;
                    if (value.Task != null)
                        accepted(value.Task);
                },
                error => Failed(generation, error),
                session: TaskInfo.Host
            );
        }
    }
}
