using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Inventory for the commands SlopWorld runs, integrates with, or offers as a default. Host
    // The game resolves host rows. Daemon rows come from /api/whereis so native and
    // sidecar deployments report the environment that actually launches the command.
    public class BinariesPage : IOptionPage
    {
        enum ProbeKind
        {
            HostPath,
            DaemonPath,
        }

        sealed class BinarySpec
        {
            public readonly string Group;
            public readonly string Name;
            public readonly string Use;
            public readonly ProbeKind Probe;

            public BinarySpec(string group, string name, string use)
                : this(group, name, use, ProbeKind.HostPath)
            {
            }

            public BinarySpec(string group, string name, string use, ProbeKind probe)
            {
                Group = group;
                Name = name;
                Use = use;
                Probe = probe;
            }
        }

        sealed class BinaryResult
        {
            public readonly BinarySpec Spec;
            public string Path { get; private set; }
            public bool Resolved { get; private set; }
            bool _found;

            public BinaryResult(BinarySpec spec)
            {
                Spec = spec;
            }

            public bool Found => Resolved && _found;

            public void SetPath(string path)
            {
                Path = path;
                _found = !string.IsNullOrEmpty(path);
                Resolved = true;
            }

            public void SetStatus(bool found, string status)
            {
                Path = status;
                _found = found;
                Resolved = true;
            }
        }

        // Inventory for player-facing integrations and default applications. Daemon rows use
        // /api/whereis, because the game process's PATH is not the daemon's PATH in a native
        // service and is especially unhelpful when the daemon runs in slopcar.
        static readonly BinarySpec[] Inventory =
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
            new BinarySpec("Command tools", "bat", "pager or syntax highlighter", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "highlight", "syntax highlighter", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "micro", "default editor", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "vim", "alternate editor", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "nvim", "Neovim editor", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "nano", "alternate editor", ProbeKind.DaemonPath),
            new BinarySpec("Command tools", "emacsclient", "Emacs editor", ProbeKind.DaemonPath),

            new BinarySpec("Desktop, audio, and clipboard", "gio", "open host applications", ProbeKind.DaemonPath),
            new BinarySpec("Desktop, audio, and clipboard", "gdbus", "native application chooser", ProbeKind.DaemonPath),
            new BinarySpec("Desktop, audio, and clipboard", "wl-copy", "Wayland clipboard copy", ProbeKind.DaemonPath),
            new BinarySpec("Desktop, audio, and clipboard", "wl-paste", "Wayland clipboard paste", ProbeKind.DaemonPath),
            new BinarySpec("Desktop, audio, and clipboard", "xclip", "X11 clipboard", ProbeKind.DaemonPath),
            new BinarySpec("Desktop, audio, and clipboard", "xsel", "X11 clipboard fallback", ProbeKind.DaemonPath),
            new BinarySpec("Desktop, audio, and clipboard", "pactl", "audio device lookup"),
            new BinarySpec("Desktop, audio, and clipboard", "songrec", "jukebox recognition"),
            new BinarySpec("Desktop, audio, and clipboard", "ncspot", "Spotify playback", ProbeKind.DaemonPath),
        };

        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly OperationGate _operations = new OperationGate();
        List<BinaryResult> _results;
        string _error;
        bool _loading;
        int _resolved;
        int _hostCount;
        bool _daemonLoading;
        string _daemonError;
        Dictionary<string, string> _daemonPaths;

        public void Load()
        {
            _daemonLoading = true;
            _daemonError = null;
            _daemonPaths = null;
            DaemonClient.Get<Wire.WhereIsReply>(WireProtocol.Routes.Whereis,
                reply =>
                {
                    _daemonPaths = reply.Binaries.ToDictionary(
                        binary => binary.Name, binary => binary.Path,
                        StringComparer.OrdinalIgnoreCase);
                    _daemonLoading = false;
                },
                error =>
                {
                    _daemonError = error;
                    _daemonLoading = false;
                });
            Scan();
        }

        void Scan()
        {
            int generation = _operations.Begin();
            _loading = true;
            _error = null;
            _resolved = 0;
            var results = Inventory.Select(spec => new BinaryResult(spec)).ToList();
            _results = results;
            _hostCount = results.Count(result => result.Spec.Probe == ProbeKind.HostPath);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    foreach (var result in results)
                    {
                        if (result.Spec.Probe != ProbeKind.HostPath) continue;
                        string path = null;
                        try { path = Locate(result.Spec.Name); }
                        catch { /* an unavailable host command is just not found */ }

                        BinaryResult completed = result;
                        DaemonClient.OnMainThread(() => Resolve(generation, completed, path));
                    }
                }
                catch (Exception e)
                {
                    DaemonClient.OnMainThread(() => Fail(generation, e.Message));
                }
            });
        }

        void Resolve(int generation, BinaryResult result, string path)
        {
            if (!_operations.IsCurrent(generation) || result.Resolved) return;
            result.SetPath(path);
            _resolved++;
            if (_resolved >= _hostCount) _loading = false;
        }

        void Fail(int generation, string message)
        {
            if (!_operations.IsCurrent(generation)) return;
            _error = message;
            foreach (var result in _results)
                if (!result.Resolved && result.Spec.Probe == ProbeKind.HostPath)
                    result.SetPath(null);
            _loading = false;
        }

        void UpdateDaemonResults()
        {
            if (_results == null) return;
            foreach (var result in _results)
            {
                if (result.Spec.Probe != ProbeKind.DaemonPath) continue;
                if (_daemonLoading)
                {
                    result.SetStatus(false, "Checking daemon");
                    continue;
                }
                if (_daemonError != null)
                {
                    result.SetStatus(false, "daemon unavailable");
                    continue;
                }
                string path = null;
                if (_daemonPaths != null) _daemonPaths.TryGetValue(result.Spec.Name, out path);
                result.SetPath(path);
            }
        }

        public void Draw(Rect rect)
        {
            using (WidgetState.Save()) DrawCore(rect);
        }

        void DrawCore(Rect rect)
        {
            var inner = SettingsPageLayout.Body(rect);
            if (_scroll.HandleWheel(inner)) return;
            UpdateDaemonResults();
            string caption = "Commands used, integrated, or recommended by SlopWorld. " +
                "Host rows check the game's PATH. Daemon rows use the daemon's effective PATH.";
            float width = UiScrollBody.Measure(inner, 0f,
                UiScrollbarReservation.Always).ContentWidth;
            float captionH = UiText.StatusLabelHeight(caption, width);
            float top = captionH + UiTheme.GapS + UiTheme.RowH;
            var geometry = UiScrollBody.Measure(inner, top + ContentHeight(width),
                UiScrollbarReservation.Always);
            using (_scroll.Scope(inner, geometry.View))
            {
                // Even the first wheel pass must not construct native path fields.
                if (SmoothScroll.WheelOnly) return;
                UiText.StatusLabel(new Rect(0f, 0f, geometry.View.width, captionH), caption,
                    UiTheme.Dim);
                DrawHeader(new Rect(0f, captionH + UiTheme.GapS, geometry.View.width,
                    UiTheme.RowH));
                if (_results == null)
                {
                    UiText.StatusLabel(new Rect(0f, top, geometry.View.width,
                        Mathf.Max(UiTheme.LineH, geometry.View.height - top)),
                        _error ?? (_loading ? "Checking host PATH" : "No scan results."),
                        _error != null ? UiTheme.Bad : UiTheme.Dim);
                }
                else
                {
                    DrawRows(geometry.View, top, inner.height);
                }
            }

            var foot = new UiLayout.Bar(SettingsPageLayout.Footer(rect));
            if (foot.Left("Refresh", UiTheme.Btn.Ghost, !_loading && !_daemonLoading)) Load();
            string status = _results == null ? (_loading ? "Checking" : "") :
                $"{_results.Count(result => result.Found)} of {_results.Count} found";
            GUI.color = _error != null ? UiTheme.Bad : UiTheme.Dim;
            UiText.RowLabel(foot.Rest(), _error ?? status, TextAnchor.MiddleRight);
            GUI.color = Color.white;
        }

        void DrawHeader(Rect r)
        {
            Slab.Fill(r, UiTheme.RowBg);
            if (r.width < 520f)
            {
                UiText.RowLabel(r, "Binary / Use / Path or status");
                return;
            }
            float nameW, pathW, useX, useW;
            Columns(r.width, out nameW, out pathW, out useX, out useW);
            UiText.RowLabel(new Rect(r.x + UiTheme.GapS, r.y, 28f, r.height), "", TextAnchor.MiddleCenter);
            UiText.RowLabel(new Rect(r.x + 28f, r.y, nameW - 28f, r.height), "Binary");
            UiText.RowLabel(new Rect(r.x + nameW, r.y, pathW, r.height), "Path");
            UiText.RowLabel(new Rect(r.x + useX, r.y, useW, r.height), "Use / Status");
            Slab.Hairline(new Rect(r.x, r.yMax - 1f, r.width, 1f), UiTheme.Edge);
        }

        void DrawRows(Rect view, float y, float viewportHeight)
        {
            float rowH = BinaryRowHeight(view.width);
            float scrollY = _scroll.Position.y;
            string focused = GUI.GetNameOfFocusedControl();
            string group = null;
            // Inventory order is already grouped. Do not allocate GroupBy iterators for
            // every IMGUI pass, and keep offscreen native TextEditors out of the hot path.
            foreach (var result in _results)
            {
                if (group != result.Spec.Group)
                {
                    if (group != null) y += UiTheme.GapS;
                    group = result.Spec.Group;
                    if (VisibleRows.Intersects(y, UiTheme.RowH, scrollY, viewportHeight))
                        UiLayout.SectionHeading(new Rect(0f, y, view.width, UiTheme.RowH), group);
                    y += UiTheme.RowH + UiTheme.GapS;
                }
                if (VisibleRows.Intersects(y, rowH, scrollY, viewportHeight) ||
                    focused == "binaries.path." + result.Spec.Name)
                    DrawRow(new Rect(0f, y, view.width, rowH), result);
                y += rowH;
            }
        }

        void DrawRow(Rect r, BinaryResult result)
        {
            RowChrome.Hover(r, false, true, RowHoverPolicy.OverlayAware);
            float nameW, pathW, useX, useW;
            Columns(r.width, out nameW, out pathW, out useX, out useW);

            bool stacked = r.width < 520f;
            float lineH = UiTheme.RowH;
            if (stacked) { nameW = r.width; pathW = r.width; useX = 0f; useW = r.width; }
            var mark = new Rect(r.x + UiTheme.GapS, r.y + (lineH - 16f) / 2f, 16f, 16f);
            if (result.Found)
            {
                GUI.color = UiTheme.Yes;
                GUI.DrawTexture(mark, Icons.Check);
            }

            GUI.color = result.Found ? UiTheme.Name : UiTheme.Dim;
            UiText.RowLabel(new Rect(r.x + 28f, r.y, Mathf.Max(0f, nameW - 28f), lineH), result.Spec.Name);
            GUI.color = UiTheme.Dim;
            UiText.RowLabel(new Rect(r.x + useX, stacked ? r.y + lineH : r.y, useW, lineH), result.Spec.Use);
            GUI.color = result.Found ? UiTheme.Lead : UiTheme.Bad;
            var path = new Rect(stacked ? r.x : r.x + nameW,
                stacked ? r.y + lineH * 2f : r.y, pathW, lineH);
            if (!result.Resolved)
            {
                GUI.color = UiTheme.Dim;
                UiText.RowLabel(path, "Checking");
            }
            else if (result.Found)
            {
                float fieldPad = Mathf.Min(UiTheme.GapXS, path.width / 2f);
                var field = new Rect(path.x + fieldPad,
                    path.y + (path.height - UiTheme.FieldH) / 2f,
                    Mathf.Max(0f, path.width - fieldPad * 2f), UiTheme.FieldH);
                UiText.ReadOnlyField(field, "binaries.path." + result.Spec.Name,
                    result.Path);
            }
            else
            {
                UiText.RowLabel(path, "not found");
            }
            GUI.color = Color.white;
        }

        static float BinaryRowHeight(float width) => width < 520f ? UiTheme.RowH * 3f : UiTheme.RowH;

        float ContentHeight(float width)
        {
            float rows = Inventory.Length * BinaryRowHeight(width);
            string group = null;
            foreach (var spec in Inventory)
            {
                if (group == spec.Group) continue;
                group = spec.Group;
                rows += UiTheme.RowH + UiTheme.GapS * 2f;
            }
            return Mathf.Max(rows, UiTheme.LineH);
        }

        static void Columns(float width, out float nameW, out float pathW,
                            out float useX, out float useW)
        {
            nameW = Mathf.Min(170f, width * 0.28f);
            useW = Mathf.Min(235f, width * 0.38f);
            pathW = Mathf.Max(0f, width - nameW - useW);
            useX = nameW + pathW;
        }

        static string Locate(string command)
        {
            string path = LocateWithWhich(command);
            return !string.IsNullOrEmpty(path) ? path : LocateOnPath(command);
        }

        static string LocateWithWhich(string command)
        {
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var process = Process.Start(start))
                {
                    if (process == null) return null;
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0) return null;
                    return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault()?.Trim();
                }
            }
            catch
            {
                return null;
            }
        }

        static string LocateOnPath(string command)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                string root = string.IsNullOrEmpty(directory) ? "." : directory;
                string candidate;
                try { candidate = Path.Combine(root, command); }
                catch { continue; }
                if (File.Exists(candidate))
                {
                    try { return Path.GetFullPath(candidate); }
                    catch { return candidate; }
                }
            }
            return null;
        }
    }
}
