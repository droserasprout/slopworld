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

        public static void DrawGhostLabel(Rect r, SessionInfo info, string fallback,
                                           bool hostIcon, float markWidth)
        {
            RowAct act = RowActions.Of(info);
            string title = GhostTitle(info, fallback, act);
            string context = GhostContext(info, title, act);
            GameFont oldFont = Text.Font;
            bool hostRow = hostIcon && info != null && (info.Host || info.Ephemeral);

            if (hostRow)
            {
                float d = Mathf.Min(markWidth, r.height);
                var icon = new Rect(r.x, r.y + (r.height - d) / 2f, d, d);
                GUI.color = SlopWidgets.Off;
                GUI.DrawTexture(icon, Icons.Terminal);
                string project = info.Project ?? "";
                TooltipHandler.TipRegion(icon, project.Length > 0
                    ? "Host session in " + project
                    : "Host session");
                r.x += d + 4f;
                r.width -= d + 4f;
            }

            // Host paths are useful even when the sidebar is narrow. Keep the normal row
            // font when it fits, but reclaim the compact font's width before truncating it.
            if (hostRow)
            {
                float contextWidth = SlopWidgets.Wide(context);
                float titleW = SlopWidgets.Wide(title);
                float available = context.Length > 0
                    ? r.width - contextWidth - SlopWidgets.GapS
                    : r.width;
                if (titleW > available) Text.Font = GameFont.Tiny;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            if (context.Length == 0)
            {
                GUI.color = SlopWidgets.Lead;
                SlopWidgets.RowLabel(r, title);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = oldFont;
                return;
            }

            float contextW = Mathf.Min(SlopWidgets.Wide(context), r.width * 0.42f);
            var quiet = new Rect(r.xMax - contextW, r.y, contextW, r.height);
            var strong = new Rect(r.x, r.y, Mathf.Max(0f, quiet.x - SlopWidgets.GapS - r.x),
                r.height);

            GUI.color = SlopWidgets.Lead;
            SlopWidgets.RowLabel(strong, title);
            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(quiet, context);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = oldFont;
        }

        public static void DrawAgentText(Rect text, string session, SessionInfo info,
                                         AgentState state, Color tint,
                                         float nameH, float subH, float bellW)
        {
            var line = new Rect(text.x, text.y, text.width, nameH);

            Text.Font = GameFont.Small;
            string ago = state == AgentState.Down ? "" : Ago(info);
            float ageW = ago.Length == 0 ? 0f : SlopWidgets.Wide(ago);

            float bell = info != null && info.Bell ? Mathf.Min(bellW, nameH) : 0f;
            float timeX = line.xMax - ageW;
            float bellX = timeX - (bell > 0f ? SlopWidgets.GapXS + bell : 0f);
            float nameRight = bell > 0f ? bellX : timeX;
            var name = new Rect(line.x, line.y,
                Mathf.Max(0f, nameRight - line.x -
                    (ageW > 0f || bell > 0f ? SlopWidgets.GapXS : 0f)), line.height);

            if (info != null && info.Bell)
            {
                GUI.color = SlopWidgets.Warn;
                GUI.DrawTexture(new Rect(bellX, line.y + (nameH - bell) / 2f, bell, bell),
                    Icons.Bell);
            }

            if (ageW > 0f)
            {
                var time = new Rect(timeX, line.y, ageW, line.height);
                GUI.color = tint;
                SlopWidgets.RowLabel(time, ago, TextAnchor.MiddleRight);

                string stateName = state == AgentState.Waiting
                    ? "waiting for input"
                    : state.ToString().ToLowerInvariant();
                TooltipHandler.TipRegion(time, $"{stateName} for {ago}");
            }

            Text.Font = GameFont.Small;
            GUI.color = tint;
            SlopWidgets.RowLabel(name, session ?? "?");

            Text.Font = GameFont.Tiny;
            string indicators = Settings.StatusbarAgentIndicators
                ? AgentIndicators(info) : "";
            float indicatorW = indicators.Length == 0 ? 0f : SlopWidgets.Wide(indicators);
            var line2 = new Rect(text.x, text.y + nameH, text.width, subH);
            if (indicatorW > 0f)
            {
                var indicator = new Rect(line2.xMax - indicatorW, line2.y,
                    indicatorW, line2.height);
                GUI.color = SlopWidgets.Faint;
                SlopWidgets.RowLabel(indicator, indicators, TextAnchor.MiddleRight);
                TooltipHandler.TipRegion(indicator,
                    "a autostart · r resume on start · h host-mode networking · t persistent /tmp");
                line2.width = Mathf.Max(0f, line2.width - indicatorW - SlopWidgets.GapXS);
            }

            if (state != AgentState.Down)
            {
                string title = Title(info);
                if (title.Length > 0)
                {
                    GUI.color = SlopWidgets.Dim;
                    SlopWidgets.RowLabel(line2, title);
                    if (SlopWidgets.Wide(title) > line2.width)
                        TooltipHandler.TipRegion(line2, title);
                }
            }
        }

        static string AgentIndicators(SessionInfo info)
        {
            if (info == null) return "";

            bool host = info.Network == NetworkMode.Host;
            string indicators = "";
            if (info.Autostart) indicators += "a";
            if (info.AutoResume) indicators += "r";
            if (host) indicators += "h";
            if (info.PersistentTmp) indicators += "t";
            return indicators;
        }

        internal static string Ago(SessionInfo info)
        {
            if (info == null || info.StateSince <= 0) return "";
            long seconds = (SessionInfo.NowMs - info.StateSince) / 1000L;
            if (seconds < 0L) return "";
            if (seconds < 60L) return "<1m";
            if (seconds < 3600L) return seconds / 60L + "m";
            if (seconds < 86400L) return seconds / 3600L + "h";
            return seconds / 86400L + "d";
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

        static string Title(SessionInfo info)
        {
            if (info == null) return "";
            var value = string.IsNullOrWhiteSpace(info.Label) ? info.Title : info.Label;
            value = value ?? "";
            var font = Text.CurFontStyle?.font;
            var clean = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
                clean.Append(char.IsControl(c) || (font != null && !font.HasCharacter(c))
                    ? ' ' : c);
            string title = clean.ToString().Trim();
            title = RestoreHostPath(info, title);
            return title.Length == 0 || IsHostTitle(title) ? "" : title;
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
