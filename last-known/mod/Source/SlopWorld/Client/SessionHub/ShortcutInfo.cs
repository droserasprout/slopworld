using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Verse;


namespace SlopWorld
{
    // The agent it lands is temporary and never written to config.toml, so what is saved is
    // the errand. Spelled out rather than pointing at an existing agent, which would stop
    // working the day that agent was deleted.
    public class ShortcutInfo
    {
        public string Name = "";
        public ShortcutKind Kind = ShortcutKind.Prompt;
        public ShortcutLink Link = ShortcutLink.Project;
        // Where it runs when Link is Project, the sandbox a fresh scratch project copies
        // when it is Temp, and unread when it is Ask.
        public string Project = "";
        // The prompt, or the command line. Sent once the pane is ready for it.
        public string Text = "";
        // Blank means the daemon's own default.
        public string Command = "";

        // Shipped with the daemon rather than written in config.toml: it cannot be edited or
        // deleted, and the shortcuts table leaves it out. The breadcrumb lists still offer it,
        // which is the only place a shipped entry is meant to be seen.
        public bool Builtin;

        public static ShortcutInfo FromJson(JVal j) => new ShortcutInfo
        {
            Name = j["name"].AsString(),
            Kind = j["kind"].AsString() == "shell" ? ShortcutKind.Shell :
                   j["kind"].AsString() == "breadcrumb" ? ShortcutKind.Breadcrumb :
                   j["kind"].AsString() == "fa" ? ShortcutKind.FileAction : ShortcutKind.Prompt,
            Link = ParseLink(j["link"].AsString()),
            Project = j["project"].AsString(),
            Text = j["text"].AsString(),
            Command = j["command"].IsNull ? "" : j["command"].AsString(),
            Builtin = j["builtin"].AsBool(false),
        };

        // An unknown link reads as Project, the way an unknown state reads as Down: a version
        // skew has to stay survivable.
        public static ShortcutLink ParseLink(string s)
        {
            switch (s)
            {
                case "temp": return ShortcutLink.Temp;
                case "ask": return ShortcutLink.Ask;
                default: return ShortcutLink.Project;
            }
        }

        public static string LinkName(ShortcutLink l) =>
            l == ShortcutLink.Temp ? "temp" : l == ShortcutLink.Ask ? "ask" : "project";

        static string KindName(ShortcutKind k) =>
            k == ShortcutKind.Shell ? "shell" :
            k == ShortcutKind.Breadcrumb ? "breadcrumb" :
            k == ShortcutKind.FileAction ? "fa" : "prompt";

        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)}," +
            $"\"kind\":{JVal.Q(KindName(Kind))}," +
            $"\"link\":{JVal.Q(LinkName(Link))}," +
            $"\"project\":{JVal.Q(Project)},\"text\":{JVal.Q(Text)}," +
            $"\"command\":{(string.IsNullOrEmpty((Command ?? "").Trim()) ? "null" : JVal.Q(Command))}}}";

        public ShortcutInfo Copy() => new ShortcutInfo
        {
            Name = Name,
            Kind = Kind,
            Link = Link,
            Project = Project,
            Text = Text,
            Command = Command,
            Builtin = Builtin,
        };
    }
}
