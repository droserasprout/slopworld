using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Verse;


namespace SlopWorld
{
    // Save the errand definition in config.toml. Agents created for errands are temporary.
    // Store the definition independently of existing agents so deleting an agent does not invalidate the errand.
    public class LibraryItemInfo
    {
        public string Name = "";
        public LibraryItemKind Kind = LibraryItemKind.Prompt;
        public LibraryItemLink Link = LibraryItemLink.Project;
        // Destination project when Link is Project.
        // Temporary links create a new workspace. Ask links let the caller select the destination.
        public string Project = "";
        // The prompt, or the command line. Sent once the pane is ready for it.
        public string Text = "";
        // Blank means the daemon's own default.
        public string Command = "";
        public bool Host;
        public string AgentTemplate = "";
        // Action after file selection. Ask displays a menu for each invocation.
        public FileActionMode Mode = FileActionMode.Ask;

        // The daemon supplies this entry without saving it in config.toml.
        // Built-in entries do not permit editing or deletion.
        // Omit them from the library table but include them in breadcrumb lists.
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

        // Use Project for unknown link values to tolerate differences between client and daemon versions.
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
