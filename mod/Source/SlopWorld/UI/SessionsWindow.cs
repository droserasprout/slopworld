using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>The colony overview: every session, its state, and the buttons to act on it.</summary>
    public class SessionsWindow : Window
    {
        Vector2 _scroll;

        public static void Toggle()
        {
            var open = Find.WindowStack.WindowOfType<SessionsWindow>();
            if (open != null) { open.Close(); return; }

            SessionHub.Instance.Refresh();
            Find.WindowStack.Add(new SessionsWindow());
        }

        public SessionsWindow()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize => new Vector2(720f, 480f);

        public override void DoWindowContents(Rect rect)
        {
            var hub = SessionHub.Instance;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, 300f, 32f), "Agents");
            Text.Font = GameFont.Small;

            // Status line doubles as the daemon health indicator.
            GUI.color = hub.Online ? new Color(0.5f, 0.8f, 0.5f) : new Color(0.9f, 0.5f, 0.5f);
            Widgets.Label(new Rect(rect.x + 90f, rect.y + 8f, 400f, 24f),
                $"{SlopClient.BaseUrl} - {hub.Status}");
            GUI.color = Color.white;

            float top = rect.y + 40f;
            var list = new Rect(rect.x, top, rect.width, rect.height - top - 40f);
            DrawList(list, hub);

            var bar = new Rect(rect.x, rect.yMax - 32f, rect.width, 30f);
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, 130f, 30f), "Add session"))
                Find.WindowStack.Add(new EditSessionDialog(null));

            if (Widgets.ButtonText(new Rect(bar.x + 138f, bar.y, 130f, 30f), "Edit config"))
                ConfigWindow.Open();

            if (Widgets.ButtonText(new Rect(bar.x + 276f, bar.y, 130f, 30f), "Reconnect"))
                hub.Connect();
        }

        void DrawList(Rect rect, SessionHub hub)
        {
            const float RowH = 52f;
            var view = new Rect(0f, 0f, rect.width - 18f, hub.Sessions.Count * RowH + 4f);

            Widgets.BeginScrollView(rect, ref _scroll, view);

            if (hub.Sessions.Count == 0)
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(new Rect(4f, 8f, view.width - 8f, 48f),
                    hub.Online
                        ? "No sessions yet. Add one and it will show up as a colonist."
                        : "Daemon unreachable. Is slopd running?  systemctl --user status slopd");
                GUI.color = Color.white;
            }

            float y = 0f;
            foreach (var s in hub.Sessions.ToList())
            {
                DrawRow(new Rect(0f, y, view.width, RowH - 4f), s);
                y += RowH;
            }

            Widgets.EndScrollView();
        }

        void DrawRow(Rect r, SessionInfo s)
        {
            Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.03f));
            Widgets.DrawHighlightIfMouseover(r);

            // State chip, so the list scans the same way the map overlay does.
            var chip = new Rect(r.x + 6f, r.y + 6f, 10f, r.height - 12f);
            Widgets.DrawBoxSolid(chip, TerminalWindow.StateColor(s.State));

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(r.x + 24f, r.y + 4f, 200f, 22f), s.Name);

            // The state in words next to the name, so the row scans without
            // decoding the colour of the chip beside it.
            GUI.color = TerminalWindow.StateColor(s.State);
            Widgets.Label(new Rect(r.x + 230f, r.y + 4f, 100f, 22f), s.State.ToString().ToLower());
            GUI.color = new Color(0.65f, 0.66f, 0.68f);

            string flags = (s.Sandbox ? "bwrap" : "unsandboxed") + (s.Net ? "" : ", no net");
            Widgets.Label(new Rect(r.x + 24f, r.y + 24f, r.width - 340f, 20f),
                $"{s.Dir}  ({flags})");
            GUI.color = Color.white;

            // Edit spans the top line; the rest sit under it, terminal last.
            float top = r.y + 4f, bottom = r.y + 26f;
            float right = r.xMax - 6f;

            if (Widgets.ButtonText(new Rect(right - 174f, top, 174f, 20f), "Edit"))
                Find.WindowStack.Add(new EditSessionDialog(s));

            var term = new Rect(right - 22f, bottom, 22f, 20f);
            TooltipHandler.TipRegion(term, $"Open the terminal for '{s.Name}'.");
            if (Widgets.ButtonImage(term, TerminalIcon.Tex))
                TerminalWindow.Open(s.Name);

            float x = right - 22f;

            x -= 62f;
            if (s.Alive)
            {
                if (Widgets.ButtonText(new Rect(x, bottom, 58f, 20f), "Stop"))
                    SessionHub.Instance.Stop(s.Name, Fail);
            }
            else if (Widgets.ButtonText(new Rect(x, bottom, 58f, 20f), "Start"))
            {
                SessionHub.Instance.Start(s.Name, Fail);
            }

            x -= 52f;
            if (Widgets.ButtonText(new Rect(x, bottom, 48f, 20f), "Del"))
            {
                var name = s.Name;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Remove session '{name}'? This kills the tmux session and drops it from config.toml.",
                    () => SessionHub.Instance.Remove(name, Fail),
                    destructive: true));
            }
        }

        static void Fail(string msg) =>
            Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false);
    }
}
