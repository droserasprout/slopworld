using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // A preview is advisory until the daemon accepts the project. Keep the request generation
    // beside the parsed project model so delayed replies can be tested without the game UI.
    public sealed class TempProjectPreviewState
    {
        public const int MaxAttempts = 3;
        int _serial;
        DateTime _retryAt = DateTime.MinValue;
        public string Name { get; private set; }
        public string Dir { get; private set; }
        public bool Pending { get; private set; }
        public string Error { get; private set; }
        public int Attempts { get; private set; }

        public bool ShouldRequest(string name, DateTime now)
        {
            string wanted = name ?? "";
            if (Name != wanted) return true;
            return !Pending && Dir == null && Attempts < MaxAttempts && now >= _retryAt;
        }

        public int Begin(string name) => Begin(name, DateTime.UtcNow);

        public int Begin(string name, DateTime now)
        {
            string wanted = name ?? "";
            if (!ShouldRequest(wanted, now)) return 0;
            if (Name != wanted)
            {
                Name = wanted;
                Attempts = 0;
                Dir = null;
            }
            Error = null;
            Pending = true;
            Attempts++;
            return ++_serial;
        }

        public bool Accept(int serial, bool temporary, string name, string dir)
        {
            if (!IsCurrent(serial, temporary, name) || string.IsNullOrEmpty(dir)) return false;
            Dir = dir;
            Pending = false;
            Error = null;
            return true;
        }

        public bool Fail(int serial, bool temporary, string name, string error, DateTime now)
        {
            if (!IsCurrent(serial, temporary, name)) return false;
            Pending = false;
            Error = string.IsNullOrEmpty(error) ? "Daemon preview failed." : error;
            int seconds = 1 << Math.Min(Attempts - 1, 2);
            _retryAt = now.AddSeconds(seconds);
            return true;
        }

        bool IsCurrent(int serial, bool temporary, string name)
        {
            if (serial != _serial) return false;
            if (temporary && name == Name) return true;
            Cancel();
            return false;
        }

        public void Cancel()
        {
            if (Name == null && !Pending) return;
            _serial++;
            Name = null;
            Dir = null;
            Pending = false;
            Error = null;
            Attempts = 0;
            _retryAt = DateTime.MinValue;
        }

        public string Status => Dir ?? (Pending
            ? "Waiting for daemon preview…"
            : Attempts >= MaxAttempts ? "Preview unavailable after three attempts." : Error ?? "");
    }

    public class ProjectInfo
    {
        public string Name = "";
        public string Dir = "";
        // The daemon coins TempRoot/name and makes it when the first agent starts there. It
        // is /tmp that is temporary, not the entry.
        public bool Temp;
        // Every agent in this project receives these direct mounts at its next start.
        public List<MountEntry> Mounts = new List<MountEntry>();

        // Resolve on the daemon: the game can have a different home and environment.
        // Keep Dir verbatim for editing; older daemons still supply literal paths.
        public string ExpandedDir => _expandedDir ?? Dir;
        string _expandedDir;

        // The daemon is the only owner of temporary root policy. Missing metadata is explicit
        // so the editor does not silently present a compiled daemon path.
        public static string TempRoot => SessionHub.Instance.Config?.TemporaryRoot ?? "";

        public static ProjectInfo FromJson(JVal j) => new ProjectInfo
        {
            Name = j["name"].AsString(),
            Dir = j["dir"].AsString(),
            _expandedDir = j["expanded_dir"].IsNull ? null : j["expanded_dir"].AsString(),
            Temp = j["temp"].AsBool(false),
            Mounts = j["mounts"].IsNull ? new List<MountEntry>() : MountEntry.ListFromJson(j["mounts"]),
        };

        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)},\"dir\":{JVal.Q(Dir)},\"temp\":{JVal.B(Temp)}," +
            $"\"mounts\":{MountEntry.ListToJson(Mounts)}}}";

        public ProjectInfo Copy() => new ProjectInfo
        {
            Name = Name,
            Dir = Dir,
            _expandedDir = _expandedDir,
            Temp = Temp,
            Mounts = Mounts.Select(m => new MountEntry { From = m.From, To = m.To, Mode = m.Mode }).ToList(),
        };
    }
}
