using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public partial class SessionInfo
    {
        public string Name = "";
        public string Project = "";
        public string Worktree = "";
        public string WorktreeName = "";
        // Include the project directory in each session response to avoid a separate project lookup.
        // Empty when the named project no longer exists.
        public string Dir = "";
        // Explicit command preset name. When empty, use Cmd or the daemon default if Cmd is also empty.
        public string Command = "";
        // The command preset after the daemon resolves [defaults] agent. Unlike Command,
        // this is also populated when the session leaves its command blank for that default.
        public string CommandPreset = "";
        // Custom command line for this agent. An empty value uses the preset's command.
        public string Cmd = "";
        public string Args = "";
        // Sandbox presets selected by this agent, in addition to its command dependencies.
        public List<string> Sandbox = new List<string>();
        // Use a private directory in persistent agent state instead of a new /tmp tmpfs for each run.
        public bool PersistentTmp;
        // Read-only command after the daemon resolves presets and defaults.
        public string Agent = "";
        public AgentState State = AgentState.Down;
        public bool Alive;
        // The network mode owned by this agent.
        public NetworkMode Network = NetworkMode.Private;
        // The DNS configuration owned by this agent.
        public DnsConfig Dns = DnsConfig.Resolved();
        // Resource limits owned by this agent. Null fields mean no cap.
        public SessionLimits Limits;
        // Project mounts for the next start. Do not save these mounts with the agent.
        public List<MountEntry> Mounts = new List<MountEntry>();
        public bool Autostart;
        public bool AutoResume;
        // Read-only status: startup auto-resume is pending or active.
        // Queue keyboard input after the auto-resume sequence.
        public bool AutoResumePending;
        // Worker metadata from the task. The wire response explicitly identifies the parent.
        // Do not infer parent relationships from names or projects.
        public bool Worker;
        public string Parent = "";
        public string TaskId = "";
        public bool Durable;
        // A library item's errand or a tmux session started by hand. It leaves the colony when its
        // process exits, and there is no entry to edit or delete. Durable host tabs also use the
        // ghost-row presentation, but are identified separately by Host.
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
        // Explicit routing and recovery identity for session-backed readers.
        public string Intent = "";
        public string ReaderPath = "";
        public string ReaderKey = "";
        public string ReaderScope = "";
        public bool ReaderPinned;
        public int ReaderLine;

        // The application sent a bell that the user has not acknowledged.
        // The daemon clears it when the client subscribes to the pane on opening the terminal.
        public bool Bell;

        // Last pane change in milliseconds since the Unix epoch. Zero before the first output.
        public long LastChange;

        // Last state change in milliseconds since the Unix epoch.
        // Use this timestamp for state duration, such as "working 3m".
        // LastChange can update on every redraw and does not measure time in the current state.
        public long StateSince;

        public bool Gone => !Alive;

        // Calculate elapsed time locally from the daemon's Unix timestamps.
        // This assumes that the game and daemon clocks agree.
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

        // Exclude Agent because it contains the daemon's resolved command.
        // Saving it would prevent later preset or default changes from taking effect.
        public Wire.SessionConfig ToWire()
        {
            var value = new Wire.SessionConfig
            {
                Name = Name,
                Project = Project,
                Worktree = Worktree,
                Command = Command,
                Sandbox = { Sandbox },
                PersistentTmp = PersistentTmp,
                Network = NetworkModeText.Name(Network),
                Dns = Dns.ToWire(),
                Limits = Limits.ToWire(),
                Autostart = Autostart,
                AutoResume = AutoResume,
            };
            if (!string.IsNullOrWhiteSpace(Cmd)) value.Cmd = Cmd;
            if (!string.IsNullOrWhiteSpace(Args)) value.Args = Args;
            if (!string.IsNullOrWhiteSpace(Label)) value.Label = Label;
            return value;
        }
    }
}
