using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The colonist bar as a left-hand column: agents gathered under the project they run in,
    // a portrait and two lines apiece. The other half of ColonistBarStrip, which owns the
    // swap - this file is only the shape, and everything said there about pointing the bar's
    // own cached layout at ours holds here too.
    //
    // So the portraits, their state icons, the selection brackets and the click that opens a
    // pane are all still the bar's own; what is drawn *around* them is ours, from the same
    // ColonistBarOnGUI call, which is what keeps the column in one piece over a pane as well
    // as on the map.
    //
    // The name under each portrait is vanilla's, centred in a cell 26 pixels wide, and there
    // is no room for it beside the text this draws. Patch_SidebarPawnLabel declines it while
    // the column is up.
    public static class AgentSidebar
    {
        // Wide enough for a session name and a directory beside a portrait, and no wider:
        // this comes off the map for as long as the layout is on.
        public const float Width = 210f;

        // The cell, as a fraction of the bar's nominal 48x48. Shrinks further to fit the
        // column, never below Floor - past that a portrait is a smudge and the column should
        // run off the bottom instead, which at least says there are more.
        const float Nominal = 0.5f;
        const float Floor = 0.3f;

        const float HeadH = 20f;
        const float AddH = 26f;
        const float Pad = 6f;
        const float CellX = 8f;
        const float TextGap = 7f;
        const float NameH = 17f;
        const float SubH = 14f;

        static readonly Color PanelBg = new Color(0.09f, 0.10f, 0.12f, 0.93f);
        static readonly Color Edge = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color HeadColor = new Color(0.55f, 0.57f, 0.62f);
        static readonly Color SubColor = new Color(0.62f, 0.64f, 0.67f);
        static readonly Color Current = new Color(1f, 1f, 1f, 0.10f);

        // A group of one project's agents, and one agent's place in the column. Rebuilt by
        // Place, read by both draw passes and by the click handler - three things that must
        // never disagree about where a row is, hence one table rather than the arithmetic
        // three times.
        struct Row
        {
            public string Session;
            public Pawn Pawn;
            public Rect Line;   // the whole row, for the highlight and the hover
            public Rect Text;   // beside the portrait: the two labels, and what a click takes
        }

        struct Head
        {
            public string Label;
            public Rect Rect;
        }

        static readonly List<Row> Rows = new List<Row>();
        static readonly List<Head> Heads = new List<Head>();

        // Reused rather than rebuilt: this runs every frame, and a dictionary of lists per
        // frame is garbage the game does not need. Keys stay behind when a project empties;
        // Order skips the ones with nothing in them.
        static readonly Dictionary<string, List<int>> Buckets =
            new Dictionary<string, List<int>>();
        static readonly List<string> Order = new List<string>();

        // The bucket for an agent whose project has gone, and for a pawn that is not an
        // agent at all. Last in the column, and named rather than blank: an unheaded run of
        // portraits reads as belonging to the project above it.
        const string Loose = "no project";

        // Top to bottom, and the top bar starts where this ends rather than crossing it: a
        // panel hung under the bar leaves the corner above it showing the map through a hole
        // the width of the column.
        public static Rect Panel => new Rect(0f, 0f, Width, UI.screenHeight);

        // The column's order is the column's own, so Alt+3 is the third portrait down rather
        // than the third the bar would have drawn.
        public static List<string> Sessions()
        {
            var order = new List<string>();
            foreach (var row in Rows)
                if (row.Session != null) order.Add(row.Session);
            return order;
        }

        // True for the length of the two draw passes, which is when the pawn label is
        // declined.
        public static bool Drawing { get; private set; }

        // Lays the whole column out and answers the scale the portraits are drawn at.
        // Entries the bar carries with no pawn (a caravan's group row) are parked off screen:
        // there is a loc for every entry whether it draws or not.
        public static float Place(
            List<ColonistBar.Entry> entries, List<Vector2> locs, int count, bool plus,
            out Rect add)
        {
            Rows.Clear();
            Heads.Clear();
            Bucket(entries, locs, count);

            // From the top of the screen: the top bar is beside the column, not over it, so
            // the whole height is the column's to lay out in.
            float top = Pad;
            float room = UI.screenHeight - top - Pad;

            int rows = 0;
            foreach (var key in Order) rows += Buckets[key].Count;

            float s = Fit(rows, Order.Count, plus, room);
            float pitch = Pitch(s);
            float cell = ColonistBar.BaseSize.y * s;
            // The portrait, overhang and all: the texture is taller than the cell it is
            // anchored in, so the row is as tall as what is actually drawn.
            float head = ColonistBarColonistDrawer.PawnTextureSize.y * s;

            float y = top;
            foreach (var key in Order)
            {
                Heads.Add(new Head
                {
                    Label = key,
                    Rect = new Rect(CellX, y, Width - CellX * 2f, HeadH),
                });
                y += HeadH;

                foreach (int i in Buckets[key])
                {
                    // The loc is the cell's corner and the texture is anchored to the cell's
                    // bottom, sticking Overhang above it - which is what vanilla's own row
                    // spacing leaves room for, so the row starts at the top of the portrait
                    // rather than at the top of the cell.
                    locs[i] = new Vector2(CellX, y + head - cell);

                    float tx = CellX + ColonistBar.BaseSize.x * s + TextGap;
                    var line = new Rect(0f, y, Width, head);
                    Rows.Add(new Row
                    {
                        Session = Session(entries[i].pawn),
                        Pawn = entries[i].pawn,
                        Line = line,
                        Text = new Rect(tx, y + (head - NameH - SubH) / 2f,
                            Width - tx - Pad, NameH + SubH),
                    });

                    y += pitch;
                }
            }

            add = plus
                ? new Rect(CellX, y + 2f, Width - CellX * 2f, AddH)
                : Rect.zero;
            return s;
        }

        // Vanilla's own vertical pitch, which is what leaves room for the overhang: a cell
        // and the gap the next row's head pokes into.
        static float Pitch(float s) =>
            (ColonistBar.BaseSize.y + ColonistBar.BaseSpaceBetweenColonistsVertical) * s;

        // Headings and the "+" are a fixed cost, so only the portraits shrink. Solved rather
        // than stepped down, this being a straight line in s.
        static float Fit(int rows, int groups, bool plus, float room)
        {
            if (rows <= 0) return Nominal;
            float fixedH = groups * HeadH + (plus ? AddH + 2f : 0f);
            float each = ColonistBar.BaseSize.y + ColonistBar.BaseSpaceBetweenColonistsVertical;
            float s = (room - fixedH) / (rows * each);
            return Mathf.Clamp(s, Floor, Nominal);
        }

        static void Bucket(List<ColonistBar.Entry> entries, List<Vector2> locs, int count)
        {
            foreach (var list in Buckets.Values) list.Clear();
            Order.Clear();

            for (int i = 0; i < count && i < entries.Count; i++)
            {
                var pawn = entries[i].pawn;
                if (pawn == null)
                {
                    // No pawn, nothing to draw, and a loc left where it was would land the
                    // group frame in the middle of the column.
                    locs[i] = new Vector2(-9999f, -9999f);
                    continue;
                }

                var session = Session(pawn);
                var info = session == null ? null : SessionHub.Instance.Get(session);
                string key = string.IsNullOrEmpty(info?.Project) ? Loose : info.Project;

                if (!Buckets.TryGetValue(key, out var list))
                    Buckets[key] = list = new List<int>();
                list.Add(i);
            }

            foreach (var kv in Buckets)
                if (kv.Value.Count > 0) Order.Add(kv.Key);

            // Alphabetical, with the loose ones last: the column has to come back the same
            // way every frame, and a dictionary's own order does not.
            Order.Sort((a, b) =>
                a == Loose ? (b == Loose ? 0 : 1)
                : b == Loose ? -1
                : string.CompareOrdinal(a, b));
        }

        static string Session(Pawn pawn) =>
            pawn == null ? null : AgentColony.Current?.SessionOf(pawn);

        // ------------------------------------------------------------------ drawing
        //
        // Two passes around the bar's own, from ColonistBarOnGUI: the panel and the headings
        // go under the portraits, the labels and the clicks over them.

        public static void DrawBack()
        {
            if (!Wanted()) return;
            Drawing = true;

            var panel = Panel;
            Widgets.DrawBoxSolid(panel, PanelBg);
            Widgets.DrawBoxSolid(new Rect(panel.xMax - 1f, panel.y, 1f, panel.height), Edge);

            string open = TerminalWindow.CurrentName;
            var selected = Find.Selector?.SingleSelectedThing as Pawn;

            foreach (var row in Rows)
            {
                // The agent whose pane is up, or with none the one the map is looking at.
                bool current = open != null
                    ? row.Session == open
                    : row.Pawn != null && row.Pawn == selected;

                if (current) Widgets.DrawBoxSolid(row.Line, Current);
                else if (Mouse.IsOver(row.Line)) Widgets.DrawHighlight(row.Line);
            }

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerLeft;
            GUI.color = HeadColor;
            foreach (var head in Heads)
            {
                Widgets.Label(head.Rect, head.Label.Truncate(head.Rect.width));
                Widgets.DrawBoxSolid(
                    new Rect(head.Rect.x, head.Rect.yMax - 1f, head.Rect.width, 1f),
                    new Color(1f, 1f, 1f, 0.08f));
            }
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        public static void DrawFront()
        {
            if (!Drawing) return;
            try
            {
                var hub = SessionHub.Instance;
                foreach (var row in Rows)
                {
                    var info = row.Session == null ? null : hub.Get(row.Session);
                    var state = info?.State ?? AgentState.Down;

                    Text.Font = GameFont.Small;
                    GUI.color = TerminalWindow.StateColor(state);
                    var name = new Rect(row.Text.x, row.Text.y, row.Text.width, NameH);
                    Widgets.Label(name, (row.Session ?? row.Pawn?.LabelShort ?? "?")
                        .Truncate(name.width));

                    Text.Font = GameFont.Tiny;
                    GUI.color = SubColor;
                    var sub = new Rect(row.Text.x, row.Text.y + NameH, row.Text.width, SubH);
                    Widgets.Label(sub, Sub(info, state).Truncate(sub.width));

                    GUI.color = Color.white;
                    Click(row, info);
                }

                Absorb();
            }
            finally
            {
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                Drawing = false;
            }
        }

        // Line two is what the row is doing and where, which is the pair a project heading
        // does not already answer. A temporary agent says so instead of naming a directory
        // it is about to lose.
        static string Sub(SessionInfo info, AgentState state)
        {
            string word = state.ToString().ToLower();
            if (info == null) return word;
            if (info.Ephemeral) return word + " - temporary";
            string dir = Leaf(info.Dir);
            return string.IsNullOrEmpty(dir) ? word : word + "  " + dir;
        }

        static string Leaf(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            string trimmed = dir.TrimEnd('/');
            int cut = trimmed.LastIndexOf('/');
            return cut < 0 ? trimmed : trimmed.Substring(cut + 1);
        }

        // The label beside a portrait does what the portrait does, or half the row would be
        // scenery. Over a pane that is switching to the agent - the same thing
        // Patch_BarClickSwitchesTerminal does with the portrait itself - and on the map it is
        // what vanilla does with a bar click, which is to go and look at it.
        static void Click(Row row, SessionInfo info)
        {
            if (row.Session == null || ColonistBarStrip.Blocked) return;
            if (!Widgets.ButtonInvisible(row.Text, false)) return;

            if (!ColonistBarStrip.Drawing)
            {
                if (row.Pawn == null) return;
                // Cleared first: the brackets' jump-out is an animation off the select time,
                // so a pawn already selected would never replay it.
                Find.Selector.ClearSelection();
                CameraJumper.TryJumpAndSelect(row.Pawn);
                return;
            }

            if (info != null && info.Gone) SessionHub.Instance.Start(row.Session);
            else if (row.Session != TerminalWindow.CurrentName) TerminalWindow.Open(row.Session);
        }

        // Belt and braces for the flag DrawFront clears: see Patch_ColonistBarStripLayout.
        public static void EndDraw() => Drawing = false;

        // The column is a fifth of the screen taken off the map, and the map is what handles
        // whatever the portraits and the rows did not: without this, a press on the panel
        // starts a drag-selection over the ground behind it and a right-click orders a
        // colonist to walk there. Last, so the rows have already had their refusal, and both
        // buttons, since a move order is the more surprising of the two.
        //
        // The press and nothing else. A drag and its release belong to whoever the press went
        // to - ReorderableWidget settles a dragged portrait on a later pass of its own, and a
        // MouseUp eaten here would strand one mid-drag.
        //
        // The "+" is drawn from a postfix on the same method as this one and Harmony does not
        // say which of the two goes first, so its slot is stepped around by rect rather than
        // by running order.
        //
        // Not while something is stacked over a pane - that is the same frame the strip stops
        // listening in, and the window on top is the one the click belongs to.
        static void Absorb()
        {
            if (ColonistBarStrip.Blocked) return;

            var e = Event.current;
            if (e.type != EventType.MouseDown) return;
            if (!Mouse.IsOver(Panel)) return;
            if (ColonistBarStrip.AddRect.Contains(e.mousePosition)) return;
            e.Use();
        }

        static bool Wanted()
        {
            if (!ColonistBarStrip.Vertical) return false;
            if (Event.current.type == EventType.Layout) return false;
            // The map-layer call under a pane draws into pixels the pane has already
            // covered, and its buttons would take clicks aimed at the terminal.
            if (ColonistBarStrip.Suppressed) return false;
            // The bar hides itself on a small screen and behind the tile picker, and a panel
            // with no portraits in it is worse than no panel.
            return ColonistBarStrip.BarShown && !Cutscene.Playing;
        }
    }

    // Vanilla writes the pawn's name across the bottom of its cell. In the column the cell is
    // a portrait's width and the name lives beside it, so the two would be drawn over each
    // other. Only while our own passes are up: this is the same call the map labels go
    // through.
    [HarmonyLib.HarmonyPatch(typeof(GenMapUI), nameof(GenMapUI.DrawPawnLabel),
        new[] { typeof(Pawn), typeof(Vector2), typeof(float), typeof(float),
                typeof(Dictionary<string, string>), typeof(GameFont), typeof(bool),
                typeof(bool) })]
    public static class Patch_SidebarPawnLabel
    {
        static bool Prefix() => !AgentSidebar.Drawing;
    }
}
