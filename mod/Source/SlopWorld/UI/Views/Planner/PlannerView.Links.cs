using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public sealed partial class PlannerView
    {
        internal static SessionInfo LinkedWorker(IEnumerable<SessionInfo> sessions, string id) =>
            sessions.FirstOrDefault(session => session.Worker && session.StateId == id);

        void SaveLinks(IEnumerable<Wire.PlannerLink> links)
        {
            var selected = Linked(_selected);
            if (!IsOpenEndpoint || !Ready || Dirty || selected == null || selected.Id.Length == 0)
                return;
            var request = new Wire.PlannerLinksReq { Revision = selected.LinksRevision };
            request.Links.AddRange(links.Select(link => link.Clone()));
            int generation = Begin();
            DaemonClient.Put<Wire.PlannerDocument>(
                WireProtocol.Routes.PlannerLinks.Replace(":id", Uri.EscapeDataString(selected.Id)),
                request,
                value =>
                {
                    if (Current(generation))
                        Accept(value);
                },
                error => Failed(generation, error)
            );
        }

        void RemoveLink(Wire.PlannerLink link)
        {
            var item = Linked(_selected);
            if (item != null)
                SaveLinks(
                    item.Links.Where(value => value.Kind != link.Kind || value.Id != link.Id)
                );
        }

        void AddLink(string kind, string id, string label)
        {
            var item = Linked(_selected);
            if (item == null || item.Links.Any(link => link.Kind == kind && link.Id == id))
                return;
            SaveLinks(
                item.Links.Concat(
                    new[]
                    {
                        new Wire.PlannerLink
                        {
                            Kind = kind,
                            Id = id,
                            Label = label,
                        },
                    }
                )
            );
        }

        void WorkerSpawnSettled()
        {
            // The launch may commit links even when process startup fails. Refresh only
            // this endpoint, preserving any backlog edits made while the dialog was open.
            if (_endpoint == DaemonClient.BaseUrl && !Dirty && !_busy)
                Load();
        }
    }
}
