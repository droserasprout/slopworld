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
    // Host-side inventory for the commands SlopWorld runs, integrates with, or offers as a
    // default. The scan is deliberately independent of slopd: this answers what the game can
    // see on the host, including tools needed before the daemon starts.
    public class BinariesPage : IOptionPage
    {
        sealed class BinarySpec
        {
            public readonly string Group;
            public readonly string Name;
            public readonly string Use;

            public BinarySpec(string group, string name, string use)
            {
                Group = group;
                Name = name;
                Use = use;
            }
        }

        sealed class BinaryResult
        {
            public readonly BinarySpec Spec;
            public string Path { get; private set; }
            public bool Resolved { get; private set; }

            public BinaryResult(BinarySpec spec)
            {
                Spec = spec;
            }

            public bool Found => Resolved && !string.IsNullOrEmpty(Path);

            public void SetPath(string path)
            {
                Path = path;
                Resolved = true;
            }
        }

        // Keep this inventory in step with check-reqs.json, the daemon's command defaults, and
        // the command choices in CommandsPage. Each name gets its own row so alternatives such
        // as xclip/xsel and the four supported agent CLIs can be compared directly.
        static readonly BinarySpec[] Inventory =
        {
            new BinarySpec("SlopWorld", "slopd", "daemon service"),
            new BinarySpec("SlopWorld", "slopctl", "agent coordination CLI"),
            new BinarySpec("SlopWorld", "slopworld", "game launcher"),
            new BinarySpec("SlopWorld", "slopcar", "macOS sidecar launcher"),

            new BinarySpec("Runtime", "tmux", "session multiplexer"),
            new BinarySpec("Runtime", "bwrap", "sandbox isolation"),
            new BinarySpec("Runtime", "pasta", "private networking"),
            new BinarySpec("Runtime", "systemctl", "user service control"),
            new BinarySpec("Runtime", "systemd-run", "per-agent scopes"),
            new BinarySpec("Runtime", "pgrep", "game process lookup"),
            new BinarySpec("Runtime", "rg", "workspace search"),
            new BinarySpec("Runtime", "git", "Git view and snapshots"),
            new BinarySpec("Runtime", "bash", "default shell"),
            new BinarySpec("Runtime", "tail", "slopctl log following"),
            new BinarySpec("Runtime", "journalctl", "slopctl daemon logs"),

            new BinarySpec("Agent CLIs", "claude", "Anthropic agent"),
            new BinarySpec("Agent CLIs", "codex", "OpenAI agent"),
            new BinarySpec("Agent CLIs", "opencode", "OpenCode agent"),
            new BinarySpec("Agent CLIs", "pi", "Pi agent"),

            new BinarySpec("Command tools", "less", "default pager"),
            new BinarySpec("Command tools", "more", "alternate pager"),
            new BinarySpec("Command tools", "bat", "pager or syntax highlighter"),
            new BinarySpec("Command tools", "highlight", "syntax highlighter"),
            new BinarySpec("Command tools", "micro", "default editor"),
            new BinarySpec("Command tools", "vim", "alternate editor"),
            new BinarySpec("Command tools", "nvim", "Neovim editor"),
            new BinarySpec("Command tools", "nano", "alternate editor"),
            new BinarySpec("Command tools", "emacsclient", "Emacs editor"),

            new BinarySpec("Desktop, audio, and clipboard", "gio", "open host applications"),
            new BinarySpec("Desktop, audio, and clipboard", "gdbus", "native application chooser"),
            new BinarySpec("Desktop, audio, and clipboard", "xdg-open", "open links during setup"),
            new BinarySpec("Desktop, audio, and clipboard", "wl-copy", "Wayland clipboard copy"),
            new BinarySpec("Desktop, audio, and clipboard", "wl-paste", "Wayland clipboard paste"),
            new BinarySpec("Desktop, audio, and clipboard", "xclip", "X11 clipboard"),
            new BinarySpec("Desktop, audio, and clipboard", "xsel", "X11 clipboard fallback"),
            new BinarySpec("Desktop, audio, and clipboard", "pactl", "audio device lookup"),
            new BinarySpec("Desktop, audio, and clipboard", "songrec", "jukebox recognition"),

            new BinarySpec("Build and developer", "make", "project command entrypoint"),
            new BinarySpec("Build and developer", "cargo", "daemon build and tests"),
            new BinarySpec("Build and developer", "csc", "mod compiler"),
            new BinarySpec("Build and developer", "dotnet", "C# formatting and tests"),
            new BinarySpec("Build and developer", "mdbook", "human documentation"),
            new BinarySpec("Build and developer", "python3", "tooling scripts"),
            new BinarySpec("Build and developer", "rsvg-convert", "SVG icon fallback"),
            new BinarySpec("Build and developer", "ldconfig", "library discovery"),
            new BinarySpec("Build and developer", "fc-match", "font discovery"),
            new BinarySpec("Build and developer", "fc-list", "font inventory"),
            new BinarySpec("Build and developer", "ffmpeg", "OST conversion"),
            new BinarySpec("Build and developer", "xdotool", "screenshot window lookup"),
            new BinarySpec("Build and developer", "import", "screenshot capture"),
            new BinarySpec("Build and developer", "curl", "redeploy helper"),
            new BinarySpec("Build and developer", "docker", "macOS sidecar"),
            new BinarySpec("Build and developer", "gogdl", "RimWorld installer"),
        };

        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly OperationGate _operations = new OperationGate();
        List<BinaryResult> _results;
        string _error;
        bool _loading;
        int _resolved;

        public void Load() => Scan();

        void Scan()
        {
            int generation = _operations.Begin();
            _loading = true;
            _error = null;
            _resolved = 0;
            var results = Inventory.Select(spec => new BinaryResult(spec)).ToList();
            _results = results;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    foreach (var result in results)
                    {
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
            if (_resolved >= _results.Count) _loading = false;
        }

        void Fail(int generation, string message)
        {
            if (!_operations.IsCurrent(generation)) return;
            _error = message;
            foreach (var result in _results)
                if (!result.Resolved) result.SetPath(null);
            _loading = false;
        }

        public void Draw(Rect rect)
        {
            using (WidgetState.Save()) DrawCore(rect);
        }

        void DrawCore(Rect rect)
        {
            var inner = SettingsPageLayout.Body(rect);
            if (_scroll.HandleWheel(inner)) return;
            string caption = "Host commands used, integrated, or recommended by SlopWorld. " +
                "A checkmark means the executable is on the game's PATH.";
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
                        _error ?? (_loading ? "Checking host PATH..." : "No scan results."),
                        _error != null ? UiTheme.Bad : UiTheme.Dim);
                }
                else
                {
                    DrawRows(geometry.View, top, inner.height);
                }
            }

            var foot = new UiLayout.Bar(SettingsPageLayout.Footer(rect));
            if (foot.Left("Refresh", UiTheme.Btn.Ghost, !_loading)) Scan();
            string status = _results == null ? (_loading ? "Checking..." : "") :
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
                UiText.RowLabel(r, "Binary / Use / Path");
                return;
            }
            float nameW, pathW, useX, useW;
            Columns(r.width, out nameW, out pathW, out useX, out useW);
            UiText.RowLabel(new Rect(r.x + UiTheme.GapS, r.y, 28f, r.height), "", TextAnchor.MiddleCenter);
            UiText.RowLabel(new Rect(r.x + 28f, r.y, nameW - 28f, r.height), "Binary");
            UiText.RowLabel(new Rect(r.x + nameW, r.y, pathW, r.height), "Path");
            UiText.RowLabel(new Rect(r.x + useX, r.y, useW, r.height), "Use");
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
                UiText.RowLabel(path, "checking...");
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
