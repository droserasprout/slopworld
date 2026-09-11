using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The project and session editors share the visible DNS form while keeping ownership of
    // the selected value and its raw, still-editable server text in their respective shells.
    static class DnsForm
    {
        public static void Draw(Listing_Standard l, DnsConfig value, DnsConfig inherited,
                                bool allowInherit, string fieldId, ref string servers,
                                Action<DnsConfig> setValue)
        {
            var inheritedDns = inherited ?? DnsConfig.Resolved();
            string inheritLabel = "Inherit project (" + inheritedDns.Label + ")";
            string label = allowInherit && value == null ? inheritLabel :
                value?.Label ?? "System resolver";

            var choices = new List<SelectorOption>();
            if (allowInherit)
                choices.Add(new SelectorOption(inheritLabel, () => setValue(null)));
            choices.Add(new SelectorOption("System resolver",
                () => setValue(DnsConfig.Resolved())));
            choices.Add(new SelectorOption("Custom DNS servers", () =>
            {
                if (value?.Mode != DnsMode.Servers) setValue(DnsConfig.Custom());
            }));
            UiControls.Select(l, "DNS", label, choices, out _);

            if (value?.Mode == DnsMode.Servers)
            {
                servers = UiControls.Field(l, fieldId, servers ?? "");
                GUI.color = UiTheme.Dim;
                l.Label("Comma-separated IPv4 addresses; maximum two. Changes apply on restart.");
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = UiTheme.Dim;
                l.Label("System resolver follows the daemon's current resolv.conf.");
                GUI.color = Color.white;
            }
        }

        public static bool TrySave(DnsConfig value, string servers, out string error)
        {
            error = null;
            if (value?.Mode != DnsMode.Servers) return true;
            if (!DnsConfig.TryParseServers(servers, out List<string> parsed, out error)) return false;
            value.Servers = parsed;
            return true;
        }
    }
}
