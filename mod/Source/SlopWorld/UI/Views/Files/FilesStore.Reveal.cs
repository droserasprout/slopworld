using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Reveals only the required ancestor listings, probing exact paths when a listing is capped.
    sealed partial class FilesStore
    {
        public void RevealPath(string project, List<string> parts)
        {
            int version = ++_focusVersion;
            Reveal(Root(project), parts, 0, version);
            BumpTree();
        }

        void Reveal(FileNode parent, List<string> parts, int at, int version)
        {
            if (version != _focusVersion) return;
            parent.Expanded = true;
            BumpTree();
            Fetch(parent, () =>
            {
                if (version != _focusVersion || parent.Children == null) return;
                var child = parent.Children.FirstOrDefault(n => n.Name == parts[at]);
                if (child != null) RevealChild(child, parts, at, version);
                else if (parent.More) RevealOmittedChild(parent, parts, at, version);
                else UiLayout.Fail($"path not found: {string.Join("/", parts)}");
            });
        }

        void RevealChild(FileNode child, List<string> parts, int at, int version)
        {
            if (at + 1 < parts.Count)
            {
                if (!child.IsDir) { UiLayout.Fail($"not a directory: {child.Name}"); return; }
                Reveal(child, parts, at + 1, version);
                return;
            }
            Revealed?.Invoke(child);
        }

        // A capped listing cannot establish absence. Probe the exact file, then try a
        // directory listing if it is not a regular file. No extra siblings need loading.
        void RevealOmittedChild(FileNode parent, List<string> parts, int at, int version)
        {
            string path = parent.Path.TrimEnd('/') + "/" + parts[at];
            int listingVersion = parent.ListingVersion;
            bool current() => version == _focusVersion && listingVersion == parent.ListingVersion;
            void found(bool directory, Wire.BrowseResult listing = null)
            {
                if (!current()) return;
                var child = parent.Children.FirstOrDefault(n => n.Name == parts[at]);
                if (child == null)
                {
                    child = Child(parent, parts[at], directory, directory, false);
                    if (listing != null)
                    {
                        child.Children = Listed(child, listing);
                        child.More = listing.Truncated;
                        child.HasChildren = child.Children.Count > 0 || child.More;
                    }
                    parent.Children.Add(child);
                    BumpTree();
                }
                RevealChild(child, parts, at, version);
            }
            void fail(string error)
            {
                if (current()) UiLayout.Fail("Could not reveal " + path + ": " + error);
            }
            DaemonClient.Get<Wire.FileStatResult>(WireProtocol.Routes.FileStat +
                "?path=" + Uri.EscapeDataString(path), stat =>
                {
                    if (!current()) return;
                    if (stat.IsFile) found(false);
                    else DaemonClient.Get<Wire.BrowseResult>(BrowseUrl(path),
                        listing => found(true, listing), fail);
                }, fail);
        }

    }
}
