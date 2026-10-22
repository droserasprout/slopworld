using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Own one combined discovery run. Host and daemon completions share its generation;
    // UI rendering consumes published rows without advancing discovery state.
    internal sealed class BinaryDiscovery
    {
        readonly OperationGate _operations = new OperationGate();
        readonly Action<Action<Wire.WhereIsReply>, Action<string>> _request;
        readonly Func<string, string> _locate;
        readonly Action<Action> _background,
            _mainThread;
        int _resolved,
            _hostCount;
        internal List<BinaryResult> Results { get; private set; }
        internal string Error { get; private set; }
        internal bool HostLoading { get; private set; }
        internal bool DaemonLoading { get; private set; }

        internal BinaryDiscovery(
            Action<Action<Wire.WhereIsReply>, Action<string>> request,
            Func<string, string> locate,
            Action<Action> background,
            Action<Action> mainThread
        )
        {
            _request = request;
            _locate = locate;
            _background = background;
            _mainThread = mainThread;
        }

        internal void Load()
        {
            long generation = _operations.Begin();
            Error = null;
            _resolved = 0;
            var results = Inventory.Select(spec => new BinaryResult(spec)).ToList();
            Results = results;
            _hostCount = results.Count(result => result.Spec.Probe == ProbeKind.HostPath);
            HostLoading = _hostCount > 0;
            DaemonLoading = true;
            _request(
                reply =>
                {
                    if (!_operations.IsCurrent(generation))
                        return;
                    var paths = reply.Binaries.ToDictionary(
                        binary => binary.Name,
                        binary => binary.Path,
                        StringComparer.OrdinalIgnoreCase
                    );
                    foreach (
                        var result in results.Where(result =>
                            result.Spec.Probe == ProbeKind.DaemonPath
                        )
                    )
                    {
                        paths.TryGetValue(result.Spec.Name, out var path);
                        result.SetPath(path);
                    }
                    DaemonLoading = false;
                },
                error =>
                {
                    if (!_operations.IsCurrent(generation))
                        return;
                    foreach (
                        var result in results.Where(result =>
                            result.Spec.Probe == ProbeKind.DaemonPath
                        )
                    )
                        result.SetUnavailable();
                    DaemonLoading = false;
                }
            );
            _background(() =>
            {
                try
                {
                    foreach (var result in results)
                    {
                        if (result.Spec.Probe != ProbeKind.HostPath)
                            continue;
                        string path = null;
                        try
                        {
                            path = _locate(result.Spec.Name);
                        }
                        catch
                        { /* an unavailable host command is just not found */
                        }
                        _mainThread(() => Resolve(generation, result, path));
                    }
                }
                catch (Exception e)
                {
                    _mainThread(() => Fail(generation, e.Message));
                }
            });
        }

        void Resolve(long generation, BinaryResult result, string path)
        {
            if (!_operations.IsCurrent(generation) || result.Resolved)
                return;
            result.SetPath(path);
            if (++_resolved >= _hostCount)
                HostLoading = false;
        }

        void Fail(long generation, string message)
        {
            if (!_operations.IsCurrent(generation))
                return;
            Error = message;
            foreach (var result in Results)
                if (!result.Resolved && result.Spec.Probe == ProbeKind.HostPath)
                    result.SetPath(null);
            HostLoading = false;
        }

        internal enum ProbeKind
        {
            HostPath,
            DaemonPath,
        }

        internal sealed class BinarySpec
        {
            public readonly string Group;
            public readonly string Name;
            public readonly string Use;
            public readonly ProbeKind Probe;

            public BinarySpec(
                string group,
                string name,
                string use,
                ProbeKind probe = ProbeKind.HostPath
            )
            {
                Group = group;
                Name = name;
                Use = use;
                Probe = probe;
            }
        }

        internal enum ProbeOutcome
        {
            Pending,
            Found,
            Missing,
            Unavailable,
        }

        internal sealed class BinaryResult
        {
            public readonly BinarySpec Spec;
            public string Path { get; private set; }
            public ProbeOutcome Outcome { get; private set; }
            public string Status =>
                Outcome == ProbeOutcome.Found ? null
                : Outcome == ProbeOutcome.Missing ? "not found"
                : Outcome == ProbeOutcome.Unavailable ? "daemon unavailable"
                : Spec.Probe == ProbeKind.DaemonPath ? "Checking daemon"
                : "Checking";
            public bool Resolved => Outcome != ProbeOutcome.Pending;
            public bool Found => Outcome == ProbeOutcome.Found;

            public BinaryResult(BinarySpec spec)
            {
                Spec = spec;
            }

            public void SetPath(string path)
            {
                Path = path;
                Outcome = string.IsNullOrEmpty(path) ? ProbeOutcome.Missing : ProbeOutcome.Found;
            }

            public void SetUnavailable()
            {
                Path = null;
                Outcome = ProbeOutcome.Unavailable;
            }
        }

        // Inventory for player-facing integrations and default applications. Daemon rows use
        // /api/whereis, because the game process's PATH is not the daemon's PATH in a native
        // service and is especially unhelpful when the daemon runs in slopcar.
        internal static readonly BinarySpec[] Inventory =
        {
            new BinarySpec("SlopWorld", "slopd", "daemon service"),
            new BinarySpec("SlopWorld", "slopctl", "agent coordination CLI"),
            new BinarySpec("SlopWorld", "slopworld", "game launcher"),
            new BinarySpec("SlopWorld", "slopcar", "macOS sidecar launcher"),
            new BinarySpec("Runtime", "tmux", "session multiplexer", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "bwrap", "sandbox isolation", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "pasta", "private networking", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "systemctl", "user service control"),
            new BinarySpec("Runtime", "systemd-run", "per-agent scopes", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "ps", "sandbox process inspection", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "rg", "workspace search", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "git", "Git view and snapshots", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "env", "pager environment", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "bash", "default shell", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "zsh", "alternate shell", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "fish", "alternate shell", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "nu", "alternate shell", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "pwsh", "alternate shell", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "sh", "POSIX shell", ProbeKind.DaemonPath),
            new BinarySpec("Runtime", "tail", "slopctl log following"),
            new BinarySpec("Runtime", "journalctl", "slopctl daemon logs"),
            new BinarySpec("Agent CLIs", "claude", "Anthropic agent", ProbeKind.DaemonPath),
            new BinarySpec("Agent CLIs", "codex", "OpenAI agent", ProbeKind.DaemonPath),
            new BinarySpec("Agent CLIs", "opencode", "OpenCode agent", ProbeKind.DaemonPath),
            new BinarySpec("Agent CLIs", "pi", "Pi agent", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "less", "default pager", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "delta", "Git diff pager", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "more", "alternate pager", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "bat", "pager/highlighter", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "batcat", "bat on Debian", ProbeKind.DaemonPath),
            new BinarySpec(
                "Command tools",
                "highlight",
                "syntax highlighter",
                ProbeKind.DaemonPath
            ),
            new BinarySpec(
                "Command tools",
                "pygmentize",
                "syntax highlighter",
                ProbeKind.DaemonPath
            ),
            new BinarySpec("Command tools", "micro", "default editor", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "vim", "alternate editor", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "nvim", "Neovim editor", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "nano", "alternate editor", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "emacsclient", "Emacs editor", ProbeKind.DaemonPath),
            new BinarySpec(
                "Desktop, audio, and clipboard",
                "gio",
                "open host applications",
                ProbeKind.DaemonPath
            ),
            new BinarySpec(
                "Desktop, audio, and clipboard",
                "gdbus",
                "native application chooser",
                ProbeKind.DaemonPath
            ),
            new BinarySpec(
                "Desktop, audio, and clipboard",
                "wl-copy",
                "Wayland clipboard copy",
                ProbeKind.DaemonPath
            ),
            new BinarySpec(
                "Desktop, audio, and clipboard",
                "wl-paste",
                "Wayland clipboard paste",
                ProbeKind.DaemonPath
            ),
            new BinarySpec(
                "Desktop, audio, and clipboard",
                "xclip",
                "X11/XWayland clipboard",
                ProbeKind.DaemonPath
            ),
            new BinarySpec(
                "Desktop, audio, and clipboard",
                "xsel",
                "X11/XWayland clipboard fallback",
                ProbeKind.DaemonPath
            ),
            new BinarySpec("Desktop, audio, and clipboard", "pactl", "audio device lookup"),
            new BinarySpec("Desktop, audio, and clipboard", "songrec", "jukebox recognition"),
            new BinarySpec(
                "Desktop, audio, and clipboard",
                "ncspot",
                "Spotify playback",
                ProbeKind.DaemonPath
            ),
        };
    }
}
