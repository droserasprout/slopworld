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
            Slab.Box(outer, UiWidgets.Well, UiWidgets.Edge);
            var pad = outer.ContractedBy(UiWidgets.GapS);
            var view = new Rect(0f, 0f, pad.width - UiWidgets.ScrollbarW,
                Mathf.Max(Height(data, pad.width - UiWidgets.ScrollbarW), pad.height));
            using (scroll.Scope(pad, view))
            {

                float y = 0f;
                y = TextBlock(view.width, y, data.Title, UiWidgets.Lead);
                y += UiWidgets.GapXS;
                if (!string.IsNullOrEmpty(data.Subtitle))
                {
                    y = TextBlock(view.width, y, data.Subtitle, UiWidgets.Dim);
                    y += UiWidgets.GapXS;
                }
                if (data.Notes.Count > 0)
                {
                    y = TextBlock(view.width, y, string.Join("\n", data.Notes.ToArray()),
                        UiWidgets.Faint);
                    y += UiWidgets.GapS;
                }

                foreach (var field in data.Fields)
                    y = Field(view.width, y, field);

            }
        }

        static float Height(SandboxPreviewData data, float width)
        {
            float y = 0f;
            y += Text.CalcHeight(data.Title, width) + UiWidgets.GapXS;
            if (!string.IsNullOrEmpty(data.Subtitle))
                y += Text.CalcHeight(data.Subtitle, width) + UiWidgets.GapXS;
            if (data.Notes.Count > 0)
                y += Text.CalcHeight(string.Join("\n", data.Notes.ToArray()), width) +
                     UiWidgets.GapS;
            foreach (var field in data.Fields)
            {
                string text = field.Values.Count == 0
                    ? "(nothing)"
                    : string.Join("\n", field.Values.ToArray());
                y += UiWidgets.RowH + UiWidgets.GapXS +
                     Text.CalcHeight(text, width) + UiWidgets.GapM;
            }
            return y + UiWidgets.GapM;
        }

        static float Field(float width, float y, SandboxPreviewField field)
        {
            UiWidgets.SectionHeading(new Rect(0f, y, width, UiWidgets.RowH), field.Label);
            y += UiWidgets.RowH + UiWidgets.GapXS;
            string text = field.Values.Count == 0
                ? "(nothing)"
                : string.Join("\n", field.Values.ToArray());
            GUI.color = field.Values.Count == 0 ? UiWidgets.Faint : UiWidgets.Name;
            Widgets.Label(new Rect(0f, y, width, Text.CalcHeight(text, width)), text);
            GUI.color = Color.white;
            return y + Text.CalcHeight(text, width) + UiWidgets.GapM;
        }

        static float TextBlock(float width, float y, string text, Color color)
        {
            GUI.color = color;
            Widgets.Label(new Rect(0f, y, width, Text.CalcHeight(text, width)), text);
            GUI.color = Color.white;
            return y + Text.CalcHeight(text, width);
        }
    }
}
