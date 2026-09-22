using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Rendering for a row is fed by the sidebar's layout and session snapshots. It does not
    // own selection, clicks, or geometry; those remain with AgentSidebar.
    static class SidebarRowRenderer
    {
        static readonly string[] ViewPrefixes = { "view-", "search-", "link-" };
        static readonly string[] EditPrefixes = { "edit-" };
        static readonly string[] DiffPrefixes = { "diff-" };
        static readonly SidebarTitleCache Titles = new SidebarTitleCache();
        static readonly System.Func<SessionInfo, string> BuildTitleText = BuildTitle;
        static readonly string[] IndicatorText =
        {
            "", "a", "r", "ar", "h", "ah", "rh", "arh",
            "t", "at", "rt", "art", "ht", "aht", "rht", "arht",
        };
        static int _ageFrame = -1;
        static long _ageNowMs;
        static long _ageBucket = long.MinValue;
        static long _sessionsVersion = long.MinValue;
        static int _titleFontRevision;
        static int _presentationFontRevision = -1;
        static bool _indicators;
        static readonly Dictionary<SessionInfo, RowPresentation> Presentations =
            new Dictionary<SessionInfo, RowPresentation>();

        struct RowPresentation
        {
            public string Name;
            public string Title;
            public string RawTitle;
            public string Label;
            public string Dir;
            public bool Host;
            public AgentState State;
            public long StateSince;
            public int IndicatorMask;
            public string Ago;
            public string StateTip;
            public string Indicators;
        }

        static SidebarRowRenderer()
        {
            // Glyph availability can change on an atlas rebuild even when the font is the same.
            Font.textureRebuilt += _ => _titleFontRevision++;
        }

        internal static void BeginFrame(long sessionsVersion)
        {
            int frame = Time.frameCount;
            if (_ageFrame == frame && _sessionsVersion == sessionsVersion) return;

            long now = SessionInfo.NowMs;
            long bucket = now / 1000L;
            bool indicators = Settings.StatusbarAgentIndicators;
            if (_sessionsVersion != sessionsVersion || _ageBucket != bucket ||
                _indicators != indicators || _presentationFontRevision != _titleFontRevision)
                Presentations.Clear();

            _ageFrame = frame;
            _ageNowMs = now;
            _ageBucket = bucket;
            _sessionsVersion = sessionsVersion;
            _presentationFontRevision = _titleFontRevision;
            _indicators = indicators;
        }

        public static void DrawGhostLabel(Rect r, SessionInfo info, string fallback,
                                           bool hostIcon, float markWidth, bool italic = false)
        {
            RowAct act = RowActions.Of(info);
            string title = GhostTitle(info, fallback, act);
            string context = GhostContext(info, title, act);
            GameFont oldFont = Text.Font;
            bool hostRow = hostIcon && info != null && (info.Host || info.Ephemeral);
            bool hostTerminal = hostIcon && info != null && info.Host;

            if (hostRow)
            {
                float d = Mathf.Min(markWidth, r.height);
                var icon = new Rect(r.x, r.y + (r.height - d) / 2f, d, d);
                if (Event.current.type == EventType.Repaint)
                {
                    var was = GUI.color;
                    GUI.color = UiTheme.Off;
                    GUI.DrawTexture(icon, Icons.Terminal);
                    GUI.color = was;
                }
                string project = info.Project ?? "";
                TooltipHandler.TipRegion(icon, project.Length > 0
                    ? "Host session in " + project
                    : "Host session");
                r.x += d + UiTheme.GapXS;
                r.width -= d + UiTheme.GapXS;
            }

            // Host paths are useful even when the sidebar is narrow. Keep the normal row
            // font when it fits, but reclaim the compact font's width before truncating it.
            if (hostRow)
            {
                float contextWidth = UiTheme.Wide(context);
                float titleW = UiTheme.Wide(title);
                float available = context.Length > 0
                    ? r.width - contextWidth - UiTheme.GapS
                    : r.width;
                if (titleW > available) Text.Font = GameFont.Tiny;
            }

            Color titleColor = hostTerminal ? HostTerminalColor(info) : UiTheme.Lead;
            Text.Anchor = TextAnchor.MiddleLeft;
            if (context.Length == 0)
            {
                GUI.color = titleColor;
                UiText.RowLabel(r, title, TextAnchor.MiddleLeft, italic);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = oldFont;
                return;
            }

            float contextW = Mathf.Min(UiTheme.Wide(context), r.width * 0.42f);
            var quiet = new Rect(r.xMax - contextW, r.y, contextW, r.height);
            var strong = new Rect(r.x, r.y, Mathf.Max(0f, quiet.x - UiTheme.GapS - r.x),
                r.height);

            GUI.color = titleColor;
            UiText.RowLabel(strong, title, TextAnchor.MiddleLeft, italic);
            GUI.color = UiTheme.Dim;
            UiText.RowLabel(quiet, context);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = oldFont;
        }

        public static void DrawAgentText(Rect text, string session, SessionInfo info,
                                         AgentState state, Color tint,
                                         float nameH, float subH, float bellW)
        {
            var line = new Rect(text.x, text.y, text.width, nameH);

            Text.Font = GameFont.Small;
            var presentation = Present(info, state);
            string ago = state == AgentState.Down ? "" : presentation.Ago;
            float ageW = ago.Length == 0 ? 0f : UiTheme.Wide(ago);

            float bell = info != null && info.Bell ? Mathf.Min(bellW, nameH) : 0f;
            float timeX = line.xMax - ageW;
            float bellX = timeX - (bell > 0f ? UiTheme.GapXS + bell : 0f);
            float nameRight = bell > 0f ? bellX : timeX;
            var name = new Rect(line.x, line.y,
                Mathf.Max(0f, nameRight - line.x -
                    (ageW > 0f || bell > 0f ? UiTheme.GapXS : 0f)), line.height);

            if (info != null && info.Bell)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    var was = GUI.color;
                    GUI.color = UiTheme.Warn;
                    GUI.DrawTexture(new Rect(bellX, line.y + (nameH - bell) / 2f, bell, bell),
                        Icons.Bell);
                    GUI.color = was;
                }
            }

            if (ageW > 0f)
            {
                var time = new Rect(timeX, line.y, ageW, line.height);
                GUI.color = tint;
                UiText.RowLabel(time, ago, TextAnchor.MiddleRight);

                TooltipHandler.TipRegion(time, presentation.StateTip);
            }

            Text.Font = GameFont.Small;
            GUI.color = tint;
            UiText.RowLabel(name, (session ?? "?") + (string.IsNullOrEmpty(info?.WorktreeName) ? "" : " · " + info.WorktreeName));

            Text.Font = GameFont.Tiny;
            string indicators = Settings.StatusbarAgentIndicators
                ? presentation.Indicators : "";
            float indicatorW = indicators.Length == 0 ? 0f : UiTheme.Wide(indicators);
            var line2 = new Rect(text.x, text.y + nameH, text.width, subH);
            if (indicatorW > 0f)
            {
                var indicator = new Rect(line2.xMax - indicatorW, line2.y,
                    indicatorW, line2.height);
                GUI.color = UiTheme.Faint;
                UiText.RowLabel(indicator, indicators, TextAnchor.MiddleRight);
                TooltipHandler.TipRegion(indicator,
                    "a autostart · r resume on start · h host-mode networking · t persistent /tmp");
                line2.width = Mathf.Max(0f, line2.width - indicatorW - UiTheme.GapXS);
            }

            // Generated terminal titles can be stale once an agent is down, but a custom
            // label is durable identity and should remain visible in that state.
            if (state != AgentState.Down || !string.IsNullOrWhiteSpace(info?.Label))
            {
                string title = presentation.Title;
                if (title.Length > 0)
                {
                    GUI.color = UiTheme.Dim;
                    UiText.RowLabel(line2, title);
                    if (UiTheme.Wide(title) > line2.width)
                        TooltipHandler.TipRegion(line2, title);
                }
            }
        }

        static string AgentIndicators(SessionInfo info)
        {
            if (info == null) return "";

            int mask = (info.Autostart ? 1 : 0)
                | (info.AutoResume ? 2 : 0)
                | (info.Network == NetworkMode.Host ? 4 : 0)
                | (info.PersistentTmp ? 8 : 0);
            return IndicatorText[mask];
        }

        internal static string Ago(SessionInfo info)
        {
            return Present(info, info?.State ?? AgentState.Down).Ago;
        }

        static RowPresentation Present(SessionInfo info, AgentState state)
        {
            if (info == null) return default;
            if (_ageFrame != Time.frameCount) BeginFrame(_sessionsVersion);

            int mask = (info.Autostart ? 1 : 0)
                | (info.AutoResume ? 2 : 0)
                | (info.Network == NetworkMode.Host ? 4 : 0)
                | (info.PersistentTmp ? 8 : 0);
            if (Presentations.TryGetValue(info, out var cached) && cached.Name == info.Name &&
                cached.RawTitle == info.Title && cached.Label == info.Label && cached.Dir == info.Dir &&
                cached.Host == info.Host && cached.State == state &&
                cached.StateSince == info.StateSince && cached.IndicatorMask == mask)
            {
                PerfTrace.Count("sidebar-presentation-hits");
                return cached;
            }

            PerfTrace.Count("sidebar-presentation-rebuilds");
            string ago = Age(info.StateSince, state == AgentState.Down);
            string stateName = StateName(state);
            cached = new RowPresentation
            {
                Name = info.Name,
                Title = Title(info),
                RawTitle = info.Title,
                Label = info.Label,
                Dir = info.Dir,
                Host = info.Host,
                State = state,
                StateSince = info.StateSince,
                IndicatorMask = mask,
                Ago = ago,
                StateTip = ago.Length == 0 ? stateName : stateName + " for " + ago,
                Indicators = IndicatorText[mask],
            };
            Presentations[info] = cached;
            return cached;
        }

        static string Age(long stateSince, bool down)
        {
            if (down || stateSince <= 0) return "";
            long seconds = (_ageNowMs - stateSince) / 1000L;
            if (seconds < 0L) return "";
            if (seconds < 60L) return "<1m";
            if (seconds < 3600L) return seconds / 60L + "m";
            if (seconds < 86400L) return seconds / 3600L + "h";
            return seconds / 86400L + "d";
        }

        internal static string StateName(AgentState state)
        {
            switch (state)
            {
                case AgentState.Working: return "working";
                case AgentState.Waiting: return "waiting for input";
                case AgentState.Idle: return "idle";
                default: return "down";
            }
        }

        static string GhostTitle(SessionInfo info, string fallback, RowAct act)
        {
            if (act != RowAct.None)
            {
                string name = info?.Name ?? fallback;
                string label = info?.Label ?? "";
                if (IsRoutedLabel(label, act)) name = label;
                string subject = StripActionPrefix(name, act);
                // The action icon already says view/edit/diff. Keep the row title to the
                // filename, with the project retained as the quiet right-hand context.
                return subject;
            }

            string title = Title(info);
            return title.Length > 0 ? title : info?.Name ?? fallback;
        }

        static string GhostContext(SessionInfo info, string title, RowAct act)
        {
            string project = info?.Project ?? "";
            if (act != RowAct.None) return project;

            string name = info?.Name ?? "";
            if (info != null && (info.Ephemeral || info.Host))
            {
                string process = ProcessName(info);
                return process.Length > 0 && title != process ? process : "";
            }
            if (title != name && name.Length > 0)
            {
                return project.Length > 0 ? name + "  ·  " + project : name;
            }
            return project;
        }

        // Host rows use foreground process state rather than pane output activity: a live
        // process is white, an unchanged shell is grey, and a stopped tab is red.
        static Color HostTerminalColor(SessionInfo info)
        {
            if (info.State == AgentState.Down) return UiTheme.StateDown;
            if (info.ProcessRunning) return Color.white;
            return UiTheme.StateIdle;
        }

        static string StripActionPrefix(string name, RowAct act)
        {
            foreach (string prefix in Prefixes(act))
                if (name.StartsWith(prefix, System.StringComparison.Ordinal))
                    return name.Substring(prefix.Length);
            return name;
        }

        static bool IsRoutedLabel(string label, RowAct act) =>
            !string.IsNullOrEmpty(label) && StripActionPrefix(label, act) != label;

        static string[] Prefixes(RowAct act)
        {
            switch (act)
            {
                case RowAct.View: return ViewPrefixes;
                case RowAct.Edit: return EditPrefixes;
                case RowAct.Diff: return DiffPrefixes;
                default: return System.Array.Empty<string>();
            }
        }

        static string ProcessName(SessionInfo info)
        {
            string name = info?.Name ?? "";
            string project = info?.Project ?? "";
            string prefix = project.Length == 0 ? "" : project + "-";
            return prefix.Length > 0 && name.StartsWith(prefix, System.StringComparison.Ordinal)
                ? name.Substring(prefix.Length)
                : name;
        }

        static string Title(SessionInfo info) =>
            Titles.Get(info, Text.CurFontStyle?.font, _titleFontRevision, BuildTitleText);

        static string BuildTitle(SessionInfo info)
        {
            PerfTrace.Count("sidebar-title-rebuilds");
            if (info == null) return "";
            bool fixedLabel = !string.IsNullOrWhiteSpace(info.Label);
            var value = fixedLabel ? info.Label : info.Title;
            value = value ?? "";
            var font = Text.CurFontStyle?.font;
            var clean = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
                clean.Append(char.IsControl(c) || (font != null && !font.HasCharacter(c))
                    ? ' ' : c);
            string title = clean.ToString().Trim();
            if (!fixedLabel) title = RestoreHostPath(info, title);
            return title.Length == 0 || (!fixedLabel && IsHostTitle(title)) ? "" : title;
        }

        // tmux can hand us zsh's width-limited cwd title ("..it/repo" or "..pository")
        // even though the durable host record carries the complete current directory.
        // Rebuild only that suffix; commands and other application titles remain untouched.
        static string RestoreHostPath(SessionInfo info, string title)
        {
            string dir = info?.Dir ?? "";
            int suffixAt = 0;
            while (suffixAt < title.Length
                && (title[suffixAt] == '.' || title[suffixAt] == '\u2026')) suffixAt++;
            string suffix = title.Substring(suffixAt);
            if (info?.Host != true || suffixAt == 0 || suffix.Length < 3
                || dir.Length == 0 || !dir.EndsWith(suffix, System.StringComparison.Ordinal))
                return title;

            string home = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.UserProfile).TrimEnd('/');
            return home.Length > 0
                && (dir == home || dir.StartsWith(home + "/", System.StringComparison.Ordinal))
                ? "~" + dir.Substring(home.Length)
                : dir;
        }

        static bool IsHostTitle(string title) =>
            string.Equals(title, System.Environment.MachineName,
                System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(title, "localhost", System.StringComparison.OrdinalIgnoreCase);
    }
}
