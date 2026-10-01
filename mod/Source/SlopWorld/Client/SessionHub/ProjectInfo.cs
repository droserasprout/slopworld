using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public class ProjectInfo
    {
        public string Id = "";
        public string Name = "";
        public string WorktreeRoot = "";
        string _dir = "";
        MountEntry _directoryMount;
        public string Dir
        {
            get => _dir;
            set
            {
                if (_dir == value) return;
                // Keep the row through an empty text field while the user replaces a path.
                var primary = Mounts.FirstOrDefault(IsPrimaryMount) ??
                    (Mounts.Contains(_directoryMount) ? _directoryMount : null);
                _directoryMount = primary;
                _dir = value;
                _expandedDir = null;
                if (primary != null)
                {
                    primary.From = value;
                    primary.To = value;
                }
            }
        }
        // The daemon generates TempRoot/name and creates the directory when the first agent starts there.
        // The project entry persists after the daemon removes the temporary directory.
        public bool Temp;
        // Every agent in this project receives these direct mounts at its next start.
        public List<MountEntry> Mounts = new List<MountEntry>();

        // Resolve paths on the daemon because the game can have a different home directory and environment.
        // Preserve Dir for editing. After a local edit, preview the literal path.
        // Continue until the daemon resolves it.
        public string ExpandedDir => _expandedDir ?? Dir;
        string _expandedDir;

        public bool IsPrimaryMount(MountEntry mount) => mount != null &&
            IsProjectPath(mount.From) &&
            (IsProjectPath(mount.To) || NormalizeMountPath(mount.To) == ".");

        bool IsProjectPath(string path) => !string.IsNullOrEmpty(path) &&
            ((!string.IsNullOrEmpty(Dir) && NormalizeMountPath(path) == NormalizeMountPath(Dir)) ||
             (!string.IsNullOrEmpty(ExpandedDir) && NormalizeMountPath(path) == NormalizeMountPath(ExpandedDir)));

        // These paths use the Linux daemon's filesystem rules.
        // Normalize only the path text here. The daemon expands environment variables and home-directory prefixes.
        static string NormalizeMountPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string parts = string.Join("/", path.Split('/').Where(p => p.Length > 0 && p != ".").ToArray());
            return path.StartsWith("/", StringComparison.Ordinal) ? "/" + parts : parts.Length == 0 ? "." : parts;
        }

        public MountEntry EnsurePrimaryMount()
        {
            if (string.IsNullOrEmpty(Dir)) return null;
            var primary = Mounts.FirstOrDefault(IsPrimaryMount);
            if (primary == null)
            {
                primary = new MountEntry { From = Dir, To = Dir };
                Mounts.Insert(0, primary);
            }
            return primary;
        }

        // The daemon supplies the temporary root directory.
        // Return an empty value when metadata is missing instead of displaying a compiled default path.
        public static string TempRoot => SessionHub.Instance.Config?.TemporaryRoot ?? "";

        public static ProjectInfo FromWire(Wire.Project j) => new ProjectInfo
        {
            Id = j.Id,
            Name = j.Name,
            WorktreeRoot = j.WorktreeRoot,
            Dir = j.Dir,
            _expandedDir = j.ExpandedDir,
            Temp = j.Temp,
            Mounts = MountEntry.ListFromWire(j.Mounts),
        };

        // The daemon supplies the primary read-write mount implicitly. Other modes must
        // remain explicit so saving preserves the user’s override of that default.
        public Wire.Project ToWire() => new Wire.Project
        {
            Id = Id,
            Name = Name,
            WorktreeRoot = WorktreeRoot,
            Dir = Dir,
            Temp = Temp,
            Mounts = { Mounts.Where(m => !IsPrimaryMount(m) || m.Mode != MountMode.Rw).Select(m => m.ToWire()) },
        };

        public ProjectInfo Copy() => new ProjectInfo
        {
            Id = Id,
            Name = Name,
            WorktreeRoot = WorktreeRoot,
            Dir = Dir,
            _expandedDir = _expandedDir,
            Temp = Temp,
            Mounts = Mounts.Select(m => new MountEntry { From = m.From, To = m.To, Mode = m.Mode }).ToList(),
        };
    }
}
