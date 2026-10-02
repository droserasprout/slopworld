using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public static class ProjectSummary
    {
        // The workspace in one line, the way the project rows read it.
        public static string Of(ProjectInfo p)
        {
            var bits = new List<string>();
            // First, because it is the one thing here about the ground rather than about the
            // sandbox around it.
            if (p.Temp) bits.Add("temporary");
            int extras = p.Mounts.Count(m => !p.IsPrimaryMount(m));
            bits.Add(extras == 0 ? "no extra mounts" : extras + " shared mounts");
            return string.Join(", ", bits.ToArray());
        }
    }
}
