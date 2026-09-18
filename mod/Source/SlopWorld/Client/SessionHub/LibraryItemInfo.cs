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

        public static LibraryItemInfo FromWire(Wire.LibraryItem j) => new LibraryItemInfo
        {
            Name = j.Name,
            Kind = j.Kind == WireProtocol.LibraryKind.Shell ? LibraryItemKind.Shell :
                   j.Kind == WireProtocol.LibraryKind.Breadcrumb ? LibraryItemKind.Breadcrumb :
                   j.Kind == WireProtocol.LibraryKind.Fa ? LibraryItemKind.FileAction : LibraryItemKind.Prompt,
            Link = ParseLink(j.Link),
            Project = j.Project,
            Text = j.Text,
            Command = !j.HasCommand ? "" : j.Command,
            Host = j.Host,
            AgentTemplate = j.AgentTemplate,
            Mode = FileActionModeText.Parse(j.Mode),
            Builtin = j.Builtin,
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

        public Wire.LibraryItem ToWire()
        {
            var value = new Wire.LibraryItem
            {
                Name = Name,
                Kind = KindName(Kind),
                Link = LinkName(Link),
                Project = Project,
                Text = Text,
                Host = Host,
                AgentTemplate = AgentTemplate,
                Mode = FileActionModeText.Name(Mode)
            };
            if (!string.IsNullOrWhiteSpace(Command)) value.Command = Command;
            return value;
        }

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
