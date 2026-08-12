using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // A row that stands for another list rather than for an errand. `FloatMenuOption` carries
    // no children of its own, and this is the whole of what [SlopMenu]'s nesting needs from
    // one; everything else about the row is read off the base as usual, `Disabled` included.
    public class SlopSubmenu : FloatMenuOption
    {
        // Asked when the pointer arrives rather than when the parent is built: a station's
        // presets carry which one is playing, and a tree built up front would say so once.
        public readonly Func<List<FloatMenuOption>> Children;

        // The action is a stand-in and is never run: [SlopMenu] answers a row with a list on
        // it by opening the list. It cannot be null, because `Disabled` *is* `action == null`
        // on this class - setting the property nulls the action, and reading it asks whether
        // the action is there - so a submenu built without one arrives greyed out and dead.
        public SlopSubmenu(string label, Func<List<FloatMenuOption>> children)
            : base(label, () => { })
        {
            Children = children;
        }
    }

    // The dropdown, in the mod's own chrome. `FloatMenu` was the last vanilla surface left:
    // every picker here - fonts, themes, projects, stations, network modes, the sidebar's
    // context menus - opened one, and it arrived textured, rounded at the corners and lit in
    // vanilla's colours in the middle of a flat dark form.
    //
    // A **drop-in**, taking the same `List<FloatMenuOption>` the call sites already build, so
    // switching one over is a changed type name and nothing else. That is also the limit of
    // it: this reads the four things those options actually carry - the label, the action,
    // `Disabled`, and the extra part that [SlopWidgets.MenuToggle] hangs a checkbox on,
    // either side of the label as `extraPartRightJustified` says - and ignores the two dozen
    // fields vanilla's own menus use for pawn orders, which nothing in this mod builds.
    //
    // A [SlopSubmenu] row opens its list **beside** itself while the pointer is on it. Each
    // level is a window of its own - one list, one scroll view, one set of bounds - and the
    // parent/child chain is what keeps them behaving as one menu. The chain is also the
    // answer to the leftovers nesting used to leave: a level that opened the next by closing
    // itself and opening a fresh window could only ever dismiss the level the pointer was
    // on, so picking a value two deep left the menus above it standing.
    public class SlopMenu : Window
    {
        readonly List<FloatMenuOption> _options;
        readonly SmoothScroll _scroll = new SmoothScroll();

        // Where to put it, for a menu that does not belong at the mouse: one opened from a
        // button the keyboard reached, or one that reopens itself a tick at a time and
        // would otherwise walk across the screen behind the cursor. Null is the mouse.
        readonly Vector2? _at;

        SlopMenu _parent;
        SlopMenu _child;

        // The row `_child` hangs off, kept lit for as long as it stands - the pointer has
        // moved on to the child by then, and an unlit row over an open list reads as a menu
        // that belongs to nothing.
        int _open = -1;

        // The row the pointer is on and when it arrived there, which is what the delay before
        // a list opens is counted from.
        int _hot = -1;
        float _hotAt;

        // Where a submenu hangs: the screen y of the parent row that opened it. Written by
        // the parent rather than kept from the moment it opened - see `Place`.
        float _anchor;

        // What a submenu asked for before the screen had its say. Kept because `Place` runs
        // every frame off it and `InitialSize` measures every label in the list to answer,
        // and because a child narrowed to fit one side must widen again if the room returns.
        Vector2 _want;

        // Vanilla's own ceiling on a menu's width, kept: a label longer than this is a path
        // or a URL, and past three hundred pixels a wider menu does not make it readable.
        const float MaxW = 300f;
        const float MinW = 160f;

        // The clear space either side of a label, and above and below the whole list.
        const float PadX = SlopWidgets.MenuPadX;
        const float PadY = SlopWidgets.MenuPadY;

        // How much of the screen a menu may take before it scrolls instead of growing. The
        // jukebox's station list is the one that reaches it.
        const float MaxScreen = 0.6f;

        // The mark on a row with a list under it, drawn in the arrow a folded project head
        // wears in the sidebar: one shape for "there is more here than this line".
        const float ArrowW = 11f;

        // How long the pointer rests on a row before its list opens. Long enough that a
        // pointer crossing the menu on its way to a row further down does not leave a trail
        // of opened lists behind it, short enough not to read as waiting for the menu.
        const float OpenDelay = 0.18f;

        static float RowH => SlopWidgets.MenuRowH;

        // The frame this menu was built on - see `Keyed`.
        readonly int _born = Time.frameCount;

        public SlopMenu(List<FloatMenuOption> options, Vector2? at = null)
        {
            _options = options ?? new List<FloatMenuOption>();
            _at = at;

            doWindowBackground = false;
            doCloseX = false;
            doCloseButton = false;
            closeOnClickedOutside = true;
            drawShadow = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            layer = WindowLayer.Super;

            // `WindowStack.Add` opens with `RemoveWindowsOfType(window.GetType())`, which
            // closes every standing window of this exact type *before* `PreOpen` runs. One
            // menu at a time is right for vanilla's single-level float menus and fatal here:
            // adding a child took its own parent down, and the parent's `PostClose` unhooked
            // the child on the way out, so the child then sized itself as a root and landed
            // at the mouse. `PreOpen` does the sweep instead, where it can tell a chain from
            // a menu that has no business standing.
            onlyOneOfTypeAllowed = false;
        }

        SlopMenu(List<FloatMenuOption> options, SlopMenu parent, float anchor)
            : this(options)
        {
            _parent = parent;
            _anchor = anchor;
        }

        protected override float Margin => 0f;

        public static void Open(List<FloatMenuOption> options) =>
            Find.WindowStack.Add(new SlopMenu(options));

        // An option with no label is a structural row, not a disabled action. Keeping it in
        // the same list preserves the menu's simple measurement and lets callers put a rule
        // exactly between related groups of actions.
        public static FloatMenuOption Separator() => new FloatMenuOption("", null);

        float ContentH => _options.Count * RowH + PadY * 2f;

        float WidestLabel()
        {
            var was = Text.Font;
            Text.Font = GameFont.Small;
            float w = 0f;
            foreach (var o in _options)
                w = Mathf.Max(w, SlopWidgets.Wide(o.Label) + o.extraPartWidth +
                    (o is SlopSubmenu ? ArrowW + SlopWidgets.GapXS : 0f));
            Text.Font = was;
            return w;
        }

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Clamp(WidestLabel() + PadX * 2f, MinW, MaxW),
                Mathf.Min(ContentH, UI.screenHeight * MaxScreen));

        // At the mouse unless it was given somewhere, and shoved back onto the screen
        // rather than off the bottom of it - which is where a menu opened from a row near
        // the foot of a tall list would go.
        // A menu that is nobody's child puts away whatever menu was standing - the answer
        // vanilla's `onlyOneOfTypeAllowed` gave until the chain needed it off. A menu opened
        // by a click is closed by that same click landing outside it; this is for the ones
        // opened from a key or from a callback, which no click ever answers for.
        public override void PreOpen()
        {
            base.PreOpen();
            if (_parent == null) Sweep(this);
        }

        // The keyboard is the chrome's, never the menu's: there is nothing in here to press,
        // and the keys that do reach the chrome replace the very screen the menu was opened
        // onto - F1 puts a palette in front of it, F12 takes the pane away underneath, F2-F6
        // swap the sidebar view it was opened from. So any key puts the tree away, and the
        // event is otherwise left alone: whoever it was addressed to still gets it, and the
        // palette that F1 opens arrives with no menu standing behind it.
        //
        // Off `rawType`, not `type`. A window that absorbs input - the pane, whenever there is
        // one - makes `WindowStack.HandleEventsHighPriority` spend a `Use` on every KeyDown
        // before any window body runs, which is why hanging this off a key *handler* answered
        // only where nothing was absorbing. `Use` does not clear `rawType` (see gotchas).
        bool Keyed()
        {
            // Never on the frame it opened. A menu put up from a key - the palette's own
            // pickers are - would otherwise be shut by the character event IMGUI sends after
            // the key that produced it, the press arriving as two events and both being read.
            if (Time.frameCount == _born) return false;

            var e = Event.current;
            if (e.rawType != EventType.KeyDown || e.keyCode == KeyCode.None) return false;
            // A modifier on its own is somebody reaching for a chord, not a key.
            if (Modifier(e.keyCode)) return false;

            CloseTree();
            return true;
        }

        static bool Modifier(KeyCode k) =>
            k == KeyCode.LeftShift || k == KeyCode.RightShift ||
            k == KeyCode.LeftControl || k == KeyCode.RightControl ||
            k == KeyCode.LeftAlt || k == KeyCode.RightAlt ||
            k == KeyCode.LeftCommand || k == KeyCode.RightCommand;

        static void Sweep(SlopMenu keep)
        {
            var stack = Find.WindowStack;
            if (stack == null) return;

            // Downward, because closing one takes its branch - which stands above it - with
            // it. Nothing below the index being read moves, so the walk stays valid.
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                var m = stack[i] as SlopMenu;
                if (m != null && m != keep) m.CloseBranch();
            }
        }

        protected override void SetInitialSizeAndPosition()
        {
            if (_parent != null)
            {
                _want = InitialSize;
                Place();
                return;
            }

            var size = InitialSize;
            var at = _at ?? UI.MousePositionOnUIInverted;
            windowRect = new Rect(
                Mathf.Max(0f, Mathf.Min(at.x, UI.screenWidth - size.x)),
                Mathf.Max(0f, Mathf.Min(at.y, UI.screenHeight - size.y)),
                size.x, size.y);
        }

        // Beside the parent and **never over it**, which is the whole of the rule every
        // desktop menu follows: the trail back out of a tree is the levels above it, and a
        // child that covered its parent would take that trail with it. So the room either
        // side is measured and the child takes a side rather than being slid across one -
        // the right by preference, the left when the right cannot hold it, and where neither
        // can, the roomier side with the list **narrowed** to what is there. Truncated labels
        // cost less than a menu hanging off the screen or a parent hidden under its own child.
        //
        // Level with the row that opened it, the two frames sharing one border so the chain
        // reads as one surface rather than as windows that happen to touch.
        //
        // Worked out every frame rather than kept from the moment it opened. Both terms move:
        // the parent list scrolls under a resting pointer, and a parent near an edge is itself
        // shoved, either of which leaves a pinned child pointing at a row that is no longer
        // there.
        void Place()
        {
            var p = _parent.windowRect;
            float roomRight = UI.screenWidth - p.xMax;
            float roomLeft = p.x;

            bool onRight = _want.x <= roomRight || roomRight >= roomLeft;
            float w = Mathf.Min(_want.x, Mathf.Max(onRight ? roomRight : roomLeft, MinW));
            float x = onRight ? p.xMax - Slab.LineW : p.x - w + Slab.LineW;

            windowRect = new Rect(
                Mathf.Max(0f, x),
                Mathf.Max(0f, Mathf.Min(_anchor, UI.screenHeight - _want.y)),
                w, _want.y);
        }

        public override void DoWindowContents(Rect rect)
        {
            if (Keyed()) return;

            Slab.Box(rect, SlopWidgets.PopoverBg, SlopWidgets.Edge);

            var inner = new Rect(rect.x, rect.y + PadY, rect.width, rect.height - PadY * 2f);
            bool scrolls = ContentH > rect.height;
            var view = new Rect(0f, 0f, inner.width - (scrolls ? SlopWidgets.ScrollbarW : 0f),
                _options.Count * RowH);

            // One hit test for the whole list, before the scroll view opens its group and
            // while the viewport still means what it says. The rows tile it exactly, so which
            // one the pointer is on is arithmetic, and the answer is wanted twice - for the
            // row that lights and for the row whose list opens.
            int hot = Hot(new Rect(inner.x, inner.y, view.width, inner.height));

            Text.Font = GameFont.Small;
            _scroll.Begin(inner, view);

            // Stops at the row that was pressed. The press closes the menu and may open
            // another one, and drawing the rest of a list that is already gone is at best
            // wasted and at worst a second option answering the same click.
            float y = 0f;
            for (int i = 0; i < _options.Count; i++)
            {
                if (Row(new Rect(0f, y, view.width, RowH), _options[i], i, hot == i)) break;
                y += RowH;
            }

            _scroll.End();

            if (Event.current.type == EventType.Repaint) Pointer(hot);
        }

        int Hot(Rect viewport)
        {
            if (!Mouse.IsOver(viewport)) return -1;

            float y = Event.current.mousePosition.y - viewport.y + _scroll.Position.y;
            int i = Mathf.FloorToInt(y / RowH);
            return i >= 0 && i < _options.Count ? i : -1;
        }

        // The pointer walks the tree, once a frame. A row with a list opens it once the
        // pointer has rested the delay out; a row without one shuts the branch at once; a
        // pointer that has left this menu - into the child, which is another window, or off
        // the menu entirely - leaves what is open standing.
        void Pointer(int hot)
        {
            if (hot != _hot)
            {
                _hot = hot;
                _hotAt = Time.realtimeSinceStartup;
            }

            if (hot < 0) return;

            var sub = Sub(hot);
            if (sub == null)
            {
                CloseChild();
                return;
            }

            // Already open: it is only being kept where its row is. The list that stands
            // during the delay is the last one opened, which is steadier than shutting it the
            // moment the pointer leaves its row and reopening a neighbour's a beat later.
            if (_open == hot)
            {
                if (_child != null) _child.Follow(RowTop(hot));
                return;
            }

            if (Time.realtimeSinceStartup - _hotAt >= OpenDelay) OpenChild(hot, sub);
        }

        // Null where the row has no list or is disabled, which is the same answer as far as
        // the pointer is concerned: neither is a place a live list belongs beside.
        SlopSubmenu Sub(int i) =>
            _options[i].Disabled ? null : _options[i] as SlopSubmenu;

        // The screen y a submenu opened from row `i` hangs at. Its own top padding puts its
        // first row a `PadY` below this, which is where the parent's row is: the rows are
        // drawn inside a scroll view inside a window, and only this menu knows the offsets.
        float RowTop(int i) => windowRect.y + i * RowH - _scroll.Position.y;

        void Follow(float anchor)
        {
            _anchor = anchor;
            Place();
        }

        // True if this row took the press.
        bool Row(Rect r, FloatMenuOption o, int i, bool hot)
        {
            if (string.IsNullOrEmpty(o.Label))
            {
                Slab.Hairline(new Rect(r.x + PadX, r.y + r.height / 2f,
                    r.width - PadX * 2f, 1f), SlopWidgets.Edge);
                return false;
            }

            bool on = !o.Disabled;
            bool over = on && hot;
            bool nest = o is SlopSubmenu;
            bool lit = over || _open == i;

            if (lit) Slab.Fill(r, SlopWidgets.Hover);
            if (o.tooltip.HasValue) TooltipHandler.TipRegion(r, o.tooltip.Value);

            // The extra part is the checkbox [SlopWidgets.MenuToggle] draws, before the
            // label or after it as the option asks - a tick goes where a settings page
            // puts it, which is in front.
            float extra = o.extraPartWidth;
            bool right = o.extraPartRightJustified;
            if (o.extraPartOnGUI != null && extra > 0f)
                o.extraPartOnGUI(right
                    ? new Rect(r.xMax - extra, r.y, extra, r.height)
                    : new Rect(r.x + PadX, r.y, extra, r.height));

            if (nest)
            {
                var mark = new Rect(r.xMax - PadX - ArrowW,
                    r.y + (r.height - ArrowW) / 2f, ArrowW, ArrowW);
                GUI.color = !on ? SlopWidgets.Off : lit ? SlopWidgets.Lead : SlopWidgets.Faint;
                GUI.DrawTexture(mark, TexButton.Reveal);
                GUI.color = Color.white;
            }

            var label = new Rect(r.x + PadX + (right ? 0f : extra), r.y,
                r.width - PadX * 2f - extra - (nest ? ArrowW + SlopWidgets.GapXS : 0f),
                r.height);
            var wasAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = !on ? SlopWidgets.Off : lit ? SlopWidgets.Lead : SlopWidgets.Name;
            SlopWidgets.RowLabel(label, o.Label);
            GUI.color = Color.white;
            Text.Anchor = wasAnchor;

            if (!on || !Widgets.ButtonInvisible(r)) return false;

            // A row with a list under it is an address rather than an answer, and a press on
            // one is the pointer saying it will not wait out the delay. Opened here rather
            // than swallowed outright because clicking a window shuts every window above it,
            // this one's child included.
            var sub = o as SlopSubmenu;
            if (sub != null)
            {
                OpenChild(i, sub);
                return false;
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
            // The whole tree goes before the action runs: a row answers for the menu it is in
            // and for every menu that led to it, and an action that opens a second menu or a
            // dialog would otherwise arrive underneath the ones that asked for it.
            CloseTree();
            if (o.action != null) o.action();
            return true;
        }

        void OpenChild(int i, SlopSubmenu sub)
        {
            CloseChild();
            // Claimed before the list is asked for, so a row that turns out to have nothing
            // under it is asked once rather than once a frame for as long as it is hovered.
            _open = i;

            var kids = sub.Children != null ? sub.Children() : null;
            if (kids == null || kids.Count == 0) return;

            _child = new SlopMenu(kids, this, RowTop(i));
            Find.WindowStack.Add(_child);
        }

        void CloseChild()
        {
            _open = -1;
            var c = _child;
            _child = null;
            if (c != null) c.CloseBranch();
        }

        // This menu and everything it has open below it. The link upward is cut first: the
        // parent is the one doing the closing in every path that reaches here, and the
        // notification it would get back from `PostClose` is one it does not need.
        void CloseBranch()
        {
            _parent = null;
            CloseChild();
            Close(false);
        }

        void CloseTree()
        {
            var root = this;
            while (root._parent != null) root = root._parent;
            root.CloseBranch();
        }

        // Closed by the window stack rather than by us - a click outside the menu, or a click
        // on the parent, which shuts every window above the one clicked. The branch below goes
        // with it, and the parent is told so its row stops reading as open.
        public override void PostClose()
        {
            base.PostClose();

            var p = _parent;
            _parent = null;
            if (p != null && p._child == this)
            {
                p._child = null;
                p._open = -1;
            }

            CloseChild();
        }

        // Escape puts the whole tree away rather than one level of it. The pointer is what
        // walks back up a menu that opens on hover, so a level dismissed on its own would
        // reopen as soon as the mouse moved.
        public override void OnCancelKeyPressed()
        {
            CloseTree();
            Event.current.Use();
        }
    }
}
