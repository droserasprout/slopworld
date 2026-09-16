using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public partial class SessionInfo
    {
        public string Name = "";
        public string Project = "";
        // Repeated on the wire so a session list reads without a join. Blank when the entry
        // names a project that has gone.
        public string Dir = "";
        // A command preset's explicit name. Blank means this agent states a command line of
        // its own, or falls through to the daemon default when Cmd is also blank.
        public string Command = "";
        // The command preset after the daemon resolves [defaults] agent. Unlike Command,
        // this is also populated when the session leaves its command blank for that default.
        public string CommandPreset = "";
        // This agent's own answer to what that preset runs. Blank is the preset's.
        public string Cmd = "";
        // Sandbox presets selected by this agent, in addition to its command dependencies.
        public List<string> Sandbox = new List<string>();
        // Opts into the generated runtime context; its mount path and global discovery text are
        // controlled by the daemon's Instructions settings.
        public bool SlopworldMd;
        // Replaces the sandbox's per-run /tmp tmpfs with a private copy kept in this agent's
        // durable state directory.
        public bool PersistentTmp;
        // As the daemon will exec it, preset and defaults resolved. Read-only here.
        public string Agent = "";
        public AgentState State = AgentState.Down;
        public bool Alive;
        // The network mode owned by this agent.
        public NetworkMode Network = NetworkMode.Private;
        // The DNS configuration owned by this agent.
        public DnsConfig Dns = DnsConfig.Resolved();
        // Resource limits owned by this agent. Null fields mean no cap.
        public SessionLimits Limits;
        // Project-owned mounts shown for the next start; they are not written with the agent.
        public List<MountEntry> Mounts = new List<MountEntry>();
        public bool Autostart;
        public bool AutoResume;
        // Read-only here: slopd is waiting to run or finish startup auto-resume. Keyboard input
        // stays behind that ordered sequence.
        public bool AutoResumePending;
        // Task-owned worker metadata. Parentage is explicit on the wire; names and projects are
        // never inspected to guess a child relationship.
        public bool Worker;
        public string Parent = "";
        public string TaskId = "";
        public bool Durable;
        // A library item's errand or a tmux session started by hand: it leaves the colony when its
        // process exits, and there is no entry to edit or delete. Durable host tabs also use
        // the ghost-row presentation, but are identified separately by Host.
        public bool Ephemeral;
        // A durable host shell tab. It uses the ghost-row presentation but is not a sandboxed
        // agent and remains in the sidebar when its shell is down.
        public bool Host;
        // Read-only here: for host rows, true means tmux has a foreground command other than
        // the login shell. It stays false for ordinary agent sessions.
        public bool ProcessRunning;

        // Read-only here: the terminal window measures itself and sends the resize.
        public int Cols;
        public int Rows;

        // The daemon's generated task title when present, otherwise what the app calls itself
        // over OSC 0/2. Available for every agent, not only the subscribed pane.
        public string Title = "";

        // A non-empty manual label is shown in place of the generated title and disables
        // daemon-side title summaries for this session.
        public string Label = "";

        // The app rang the bell and nobody has looked since. Cleared by the daemon the moment
        // a pane is subscribed to, so opening the terminal is what answers it.
        public bool Bell;

        // Unix millis of the last pane change. Zero before it has ever drawn anything.
        public long LastChange;

        // Unix millis of the last state move, and a different clock from the one above: a
        // working agent redraws several times a second, so its LastChange is always now and
        // an age off it reads "0s" forever. This is what "working 3m" is measured from.
        public long StateSince;

        public bool Gone => !Alive;

        // The daemon's clock is this machine's, so the two agree without anything being sent
        // to keep them in step: an age is the difference and not a countdown the daemon owns.
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Distinguishes successive processes under one durable session name. Terminal history
        // caches may be reused only inside one run.
        public long RunId;

        public static long NowMs => (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;

        public static AgentState ParseState(string s)
        {
            switch (s)
            {
                case WireProtocol.AgentState.Working: return AgentState.Working;
                case WireProtocol.AgentState.Waiting: return AgentState.Waiting;
                case WireProtocol.AgentState.Idle: return AgentState.Idle;
                default: return AgentState.Down;
            }
        }

        // `Agent` never rides along: it is what the daemon resolved, and writing it back would
        // pin today's answer into the file forever.
        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)},\"project\":{JVal.Q(Project)}," +
            $"\"command\":{JVal.Q(Command)}," +
            $"\"cmd\":{(string.IsNullOrEmpty((Cmd ?? "").Trim()) ? "null" : JVal.Q(Cmd))}," +
            $"\"sandbox\":[{string.Join(",", Sandbox.Select(JVal.Q).ToArray())}]," +
            $"\"slopworld_md\":{JVal.B(SlopworldMd)}," +
            $"\"persistent_tmp\":{JVal.B(PersistentTmp)}," +
            $"\"label\":{(string.IsNullOrEmpty((Label ?? "").Trim()) ? "null" : JVal.Q(Label))}," +
            $"\"network\":{JVal.Q(NetworkModeText.Name(Network))}," +
            $"\"dns\":{Dns.ToJson()}," +
            $"\"limits\":{Limits.ToJson()}," +
            $"\"autostart\":{JVal.B(Autostart)}," +
            $"\"auto_resume\":{JVal.B(AutoResume)}}}";
    }
}
