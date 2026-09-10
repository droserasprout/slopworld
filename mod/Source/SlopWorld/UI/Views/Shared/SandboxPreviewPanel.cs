using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The final sandbox answer for a project or agent. This is deliberately a client-side
    // readout of requested parameters: the daemon still decides which paths exist and which
    // protected paths it drops when it builds bwrap's argv.
    public sealed class SandboxPreviewData
    {
        public string Title = "";
        public string Subtitle = "";
        public string Network = "";
        public List<string> Presets = new List<string>();
        public List<string> Notes = new List<string>();
        public List<SandboxPreviewField> Fields = new List<SandboxPreviewField>();

        public static SandboxPreviewData ForProject(ProjectInfo project) =>
            SandboxPreviewBuilder.Build("Project sandbox", project?.Name ?? "(new project)",
                project?.Dir ?? "", project, null, null);

        public static SandboxPreviewData ForAgent(SessionInfo session)
        {
            var hub = SessionHub.Instance;
            var project = hub.Project(session.Project);
            string commandName = string.IsNullOrEmpty(session.Command)
                ? session.CommandPreset : session.Command;
            CommandInfo command = null;
            if (string.IsNullOrEmpty(session.Cmd) || string.IsNullOrWhiteSpace(session.Cmd))
                command = hub.Command(commandName);
            var data = SandboxPreviewBuilder.Build("Agent sandbox", session.Name, session.Dir,
                project, command, session);
            if (project == null && !string.IsNullOrEmpty(session.Project))
                data.Notes.Add($"Project '{session.Project}' is not available; only known " +
                               "sandbox presets are shown.");
            if (!string.IsNullOrEmpty(commandName) && command == null &&
                string.IsNullOrWhiteSpace(session.Cmd))
                data.Notes.Add($"Command preset '{commandName}' is not available in the current " +
                               "preset list.");
            return data;
        }
    }

    public sealed class SandboxPreviewField
    {
        public readonly string Label;
        public readonly List<string> Values;

        public SandboxPreviewField(string label, List<string> values)
        {
            Label = label;
            Values = values ?? new List<string>();
        }
    }

    public static class SandboxPreviewPanel
    {
        public static void Draw(Rect outer, ref SmoothScroll scroll, SandboxPreviewData data)
        {
            Slab.Box(outer, UiTheme.Well, UiTheme.Edge);
            var pad = outer.ContractedBy(UiTheme.GapS);
            var view = new Rect(0f, 0f, pad.width - UiTheme.ScrollbarW,
                Mathf.Max(Height(data, pad.width - UiTheme.ScrollbarW), pad.height));
            using (scroll.Scope(pad, view))
            {

                float y = 0f;
                y = TextBlock(view.width, y, data.Title, UiTheme.Lead);
                y += UiTheme.GapXS;
                if (!string.IsNullOrEmpty(data.Subtitle))
                {
                    y = TextBlock(view.width, y, data.Subtitle, UiTheme.Dim);
                    y += UiTheme.GapXS;
                }
                if (data.Notes.Count > 0)
                {
                    y = TextBlock(view.width, y, string.Join("\n", data.Notes.ToArray()),
                        UiTheme.Faint);
                    y += UiTheme.GapS;
                }

                foreach (var field in data.Fields)
                    y = Field(view.width, y, field);

            }
        }

        static float Height(SandboxPreviewData data, float width)
        {
            float y = 0f;
            y += UiText.StatusLabelHeight(data.Title, width) + UiTheme.GapXS;
            if (!string.IsNullOrEmpty(data.Subtitle))
                y += UiText.StatusLabelHeight(data.Subtitle, width) + UiTheme.GapXS;
            if (data.Notes.Count > 0)
                y += UiText.StatusLabelHeight(string.Join("\n", data.Notes.ToArray()), width) +
                     UiTheme.GapS;
            foreach (var field in data.Fields)
            {
                string text = field.Values.Count == 0
                    ? "(nothing)"
                    : string.Join("\n", field.Values.ToArray());
                y += UiTheme.RowH + UiTheme.GapXS +
                     UiText.StatusLabelHeight(text, width) + UiTheme.GapM;
            }
            return y + UiTheme.GapM;
        }

        static float Field(float width, float y, SandboxPreviewField field)
        {
            UiLayout.SectionHeading(new Rect(0f, y, width, UiTheme.RowH), field.Label);
            y += UiTheme.RowH + UiTheme.GapXS;
            string text = field.Values.Count == 0
                ? "(nothing)"
                : string.Join("\n", field.Values.ToArray());
            float h = UiText.StatusLabelHeight(text, width);
            UiText.StatusLabel(new Rect(0f, y, width, h), text,
                field.Values.Count == 0 ? UiTheme.Faint : UiTheme.Name);
            return y + h + UiTheme.GapM;
        }

        static float TextBlock(float width, float y, string text, Color color)
        {
            float h = UiText.StatusLabelHeight(text, width);
            UiText.StatusLabel(new Rect(0f, y, width, h), text, color);
            return y + h;
        }
    }
}
