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
    public class LibraryItemInfo
    {
        public string Name = "";
        public LibraryItemKind Kind = LibraryItemKind.Prompt;
        public LibraryItemLink Link = LibraryItemLink.Project;
        // The project where it runs when Link is Project; temporary links create a fresh
        // workspace, and ask links leave the destination to the caller.
        public string Project = "";
        // The prompt, or the command line. Sent once the pane is ready for it.
        public string Text = "";
        // Blank means the daemon's own default.
        public string Command = "";
        public bool Host;
        public string AgentTemplate = "";
        // What a file action does after selection. Ask preserves the original per-invocation menu.
        public FileActionMode Mode = FileActionMode.Ask;

        // Shipped with the daemon rather than written in config.toml: it cannot be edited or
        // deleted, and the library table leaves it out. The breadcrumb lists still offer it,
        // which is the only place a shipped entry is meant to be seen.
        public bool Builtin;

        public static LibraryItemInfo FromJson(JVal j) => new LibraryItemInfo
        {
            Name = j["name"].AsString(),
            Kind = j["kind"].AsString() == WireProtocol.LibraryKind.Shell ? LibraryItemKind.Shell :
                   j["kind"].AsString() == WireProtocol.LibraryKind.Breadcrumb ? LibraryItemKind.Breadcrumb :
                   j["kind"].AsString() == WireProtocol.LibraryKind.Fa ? LibraryItemKind.FileAction : LibraryItemKind.Prompt,
            Link = ParseLink(j["link"].AsString(WireProtocol.LibraryLink.Project)),
            Project = j["project"].AsString(),
            Text = j["text"].AsString(),
            Command = j["command"].IsNull ? "" : j["command"].AsString(),
            Host = j["host"].AsBool(false),
            AgentTemplate = j["agent_template"].AsString(),
            Mode = FileActionModeText.Parse(j["mode"].AsString(WireProtocol.FileActionMode.Ask)),
            Builtin = j["builtin"].AsBool(false),
        };

        // An unknown link reads as Project, the way an unknown state reads as Down: a version
        // skew has to stay survivable.
        public static LibraryItemLink ParseLink(string s)
        {
            switch (s)
            {
                case WireProtocol.LibraryLink.Temp: return LibraryItemLink.Temp;
                case WireProtocol.LibraryLink.Ask: return LibraryItemLink.Ask;
                default: return LibraryItemLink.Project;
            }
        }

        public static string LinkName(LibraryItemLink l) =>
            l == LibraryItemLink.Temp ? WireProtocol.LibraryLink.Temp :
            l == LibraryItemLink.Ask ? WireProtocol.LibraryLink.Ask : WireProtocol.LibraryLink.Project;

        static string KindName(LibraryItemKind k) =>
            k == LibraryItemKind.Shell ? WireProtocol.LibraryKind.Shell :
            k == LibraryItemKind.Breadcrumb ? WireProtocol.LibraryKind.Breadcrumb :
            k == LibraryItemKind.FileAction ? WireProtocol.LibraryKind.Fa : WireProtocol.LibraryKind.Prompt;

        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)}," +
            $"\"kind\":{JVal.Q(KindName(Kind))}," +
            $"\"link\":{JVal.Q(LinkName(Link))}," +
            $"\"project\":{JVal.Q(Project)},\"text\":{JVal.Q(Text)}," +
            $"\"command\":{(string.IsNullOrEmpty((Command ?? "").Trim()) ? "null" : JVal.Q(Command))}," +
            $"\"host\":{JVal.B(Host)},\"agent_template\":{JVal.Q(AgentTemplate)}," +
            $"\"mode\":{JVal.Q(FileActionModeText.Name(Mode))}}}";

        public LibraryItemInfo Copy() => new LibraryItemInfo
        {
            Name = Name,
            Kind = Kind,
            Link = Link,
            Project = Project,
            Text = Text,
            Command = Command,
            Host = Host,
            AgentTemplate = AgentTemplate,
            Mode = Mode,
            Builtin = Builtin,
        };
    }
}
