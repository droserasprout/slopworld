using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // FloatMenuOption has no child collection; this subclass supplies the nesting data
    // required by [SlopMenu]. Everything else is read from the base row, including `Disabled`.
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

    // Dark-chrome replacement for FloatMenu. It accepts the existing option lists and only
    // relies on label, action, Disabled, and the checkbox extra-part used by this mod.
    // Submenus are sibling windows linked by parent/child references so closing a branch
    // removes every level above it.
    public class SlopMenu : Window
    {
        sealed class SeparatorOption : FloatMenuOption
        {
            public SeparatorOption() : base(" ", null) { }
        }

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

        // Keyboard selection is independent of the pointer: a menu opened from a focused
        // control may have no useful mouse position at all, and moving an arrow key should
        // not be reset by the pointer's hover pass.
        int _selected = -1;
        bool _keyboardSelection;
        Event _acceptEvent;

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

        // The clear space either side of a label. Rows touch the frame vertically so a menu
        // does not grow a needless blank strip above and below its first and last action.
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
        const float SeparatorH = 8f;

        // The frame this menu was built on - see `HandleKeyboard`.
        readonly int _born = Time.frameCount;

        public SlopMenu(List<FloatMenuOption> options, Vector2? at = null)
        {
            _options = options ?? new List<FloatMenuOption>();
            _at = at;

            doWindowBackground = false;
            doCloseX = false;
            doCloseButton = false;
            closeOnClickedOutside = true;
            closeOnCancel = true;
            closeOnAccept = true;
            drawShadow = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            layer = WindowLayer.Super;

            // WindowStack removes same-type windows before PreOpen. Disable that behavior so
            // nested menus survive; root PreOpen sweeps unrelated menus instead.
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

        // Focus changes can happen before the window stack gets a chance to dismiss a menu.
        // Callers that replace the screen explicitly close the whole chain rather than
        // leaving a menu owned by the old focus standing behind it.
        public static void CloseAll() => Sweep(null);

        // A separator is a structural row, not a disabled action. It needs its own type
        // because FloatMenuOption turns an empty label into "(missing label)" in its setter.
        // Keeping it in the same list preserves the menu's simple measurement and lets
        // callers put a rule exactly between related groups of actions.
        public static FloatMenuOption Separator() => new SeparatorOption();

        // The width a plain labelled picker gets when its menu contains the same labels. Keep
        // this beside InitialSize so the control and the opened list do not disagree about
        // padding, minimums or the screen's maximum readable width.
        public static float WidthFor(IEnumerable<string> labels)
        {
            var was = Text.Font;
            Text.Font = GameFont.Small;
            float widest = 0f;
            if (labels != null)
            {
                foreach (var label in labels)
                    widest = Mathf.Max(widest, SlopWidgets.Wide(label));
            }
            Text.Font = was;
            return Mathf.Clamp(widest + PadX * 2f, MinW, MaxW);
        }

        static float Height(FloatMenuOption option) =>
            option is SeparatorOption ? SeparatorH : RowH;

        float ContentH
        {
            get
            {
                float h = PadY * 2f;
                foreach (var option in _options) h += Height(option);
                return h;
            }
        }

        float WidestLabel()
        {
            var was = Text.Font;
            Text.Font = GameFont.Small;
            float w = 0f;
            foreach (var o in _options)
            {
                if (o is SeparatorOption) continue;
                w = Mathf.Max(w, SlopWidgets.Wide(o.Label) + o.extraPartWidth +
                    (o is SlopSubmenu ? ArrowW + SlopWidgets.GapXS : 0f));
            }
            Text.Font = was;
            return w;
        }

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Clamp(WidestLabel() + PadX * 2f, MinW, MaxW),
                Mathf.Min(ContentH, UI.screenHeight * MaxScreen));

        // Root menus open at the pointer and clamp to the screen; child menus use their
        // parent anchor. Root PreOpen closes unrelated menus while preserving the chain.
        public override void PreOpen()
        {
            base.PreOpen();
            if (_parent == null) Sweep(this);
        }

        // Keyboard navigation belongs to the menu rather than the control that opened it.
        // Use rawType because an absorbing window may consume event.type before this body
        // runs. Unhandled keys still dismiss the menu and remain unused, so a global shortcut
        // such as F1 can act on the same press.
        bool HandleKeyboard()
        {
            // Accept is dispatched by WindowStack before window contents. The menu handles
            // it there so an accepted form underneath cannot submit at the same time; skip
            // the same event when the body is reached.
            if (Event.current != null &&
                ReferenceEquals(Event.current, _acceptEvent)) return true;

            // WindowStack draws parents before children. Give the deepest open menu first
            // refusal so an arrow or Enter cannot answer for a row hidden behind its child.
            if (_child != null && _child.HandleKeyboard())
            {
                _keyboardSelection = true;
                return true;
            }

            // Never on the frame it opened. A menu put up from a key - the palette's own
            // pickers are - would otherwise be shut by the character event IMGUI sends after
            // the key that produced it, the press arriving as two events and both being read.
            if (Time.frameCount == _born) return false;

            var e = Event.current;
            if (e == null || e.rawType != EventType.KeyDown || e.keyCode == KeyCode.None)
                return false;
            // A modifier on its own is somebody reaching for a chord, not a key.
            if (Modifier(e.keyCode)) return false;

            _keyboardSelection = true;

            switch (e.keyCode)
            {
                case KeyCode.UpArrow:
                    MoveSelection(-1);
                    e.Use();
                    return true;

                case KeyCode.DownArrow:
                    MoveSelection(1);
                    e.Use();
                    return true;

                case KeyCode.PageUp:
                    MoveSelection(-PageSize);
                    e.Use();
                    return true;

                case KeyCode.PageDown:
                    MoveSelection(PageSize);
                    e.Use();
                    return true;

                case KeyCode.Home:
                    _selected = FindSelectable(0, 1);
                    RevealSelection();
                    e.Use();
                    return true;

                case KeyCode.End:
                    _selected = FindSelectable(_options.Count - 1, -1);
                    RevealSelection();
                    e.Use();
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    EnsureSelection();
                    e.Use();
                    ActivateSelection();
                    return true;

                case KeyCode.RightArrow:
                    EnsureSelection();
                    if (IsSelectable(_selected))
                    {
                        var sub = Sub(_selected);
                        if (sub != null) OpenChild(_selected, sub);
                    }
                    e.Use();
                    return true;

                case KeyCode.LeftArrow:
                    if (_parent != null)
                    {
                        var parent = _parent;
                        parent.CloseChild();
                    }
                    e.Use();
                    return true;

                default:
                    CloseTree();
                    return true;
            }
        }

        int PageSize => Mathf.Max(1, Mathf.Min(Mathf.Max(1, _options.Count - 1),
            Mathf.FloorToInt((windowRect.height - PadY * 2f) / RowH)));

        bool IsSelectable(int i) => i >= 0 && i < _options.Count &&
            !(_options[i] is SeparatorOption) && !_options[i].Disabled;

        int FindSelectable(int start, int step)
        {
            if (_options.Count == 0) return -1;
            for (int n = 0; n < _options.Count; n++)
            {
                int i = (start + step * n) % _options.Count;
                if (i < 0) i += _options.Count;
                if (IsSelectable(i)) return i;
            }
            return -1;
        }

        void EnsureSelection()
        {
            if (IsSelectable(_selected)) return;
            _selected = IsSelectable(_hot) ? _hot : FindSelectable(0, 1);
            RevealSelection();
        }

        void MoveSelection(int delta)
        {
            if (_options.Count == 0) return;

            int direction = delta < 0 ? -1 : 1;
            int steps = Mathf.Abs(delta);
            if (!IsSelectable(_selected))
            {
                _selected = IsSelectable(_hot) ? _hot :
                    FindSelectable(direction > 0 ? 0 : _options.Count - 1, direction);
                // With no hover, the first arrow establishes the nearest sensible end.
                // With a hover, it advances from the row the pointer is already on.
                if (!IsSelectable(_hot)) steps = 0;
            }

            for (int n = 0; n < steps; n++)
            {
                int next = FindSelectable(_selected + direction, direction);
                if (next < 0 || next == _selected) break;
                _selected = next;
            }
            RevealSelection();
        }

        void RevealSelection()
        {
            if (!IsSelectable(_selected)) return;

            float top = 0f;
            for (int i = 0; i < _selected; i++) top += Height(_options[i]);
            _scroll.Reveal(top, Height(_options[_selected]),
                Mathf.Max(1f, windowRect.height - PadY * 2f));
        }

        void ActivateSelection()
        {
            if (!IsSelectable(_selected)) return;

            var sub = _options[_selected] as SlopSubmenu;
            if (sub != null)
            {
                OpenChild(_selected, sub);
                return;
            }

            var option = _options[_selected];
            SoundDefOf.Click.PlayOneShotOnCamera();
            CloseTree();
            if (option.action != null) option.action();
        }

        public override void OnAcceptKeyPressed()
        {
            _acceptEvent = Event.current;
            EnsureSelection();
            Event.current.Use();
            ActivateSelection();
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

        // Place beside the parent, never over it. Prefer the right side, then the left, and
        // narrow to the roomier side when neither can hold the requested width. Recompute
        // every frame because scrolling or edge clamping can move the anchor row.
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
            // A submenu can be populated by an asynchronous host query after it opens. Its
            // list is shared with the caller, so measure it again and let the child follow its
            // parent row when the application entries arrive.
            if (_parent != null)
            {
                var want = InitialSize;
                if (want != _want)
                {
                    _want = want;
                    Place();
                }
            }

            var e = Event.current;
            if (e != null && (e.rawType == EventType.MouseMove ||
                              e.rawType == EventType.MouseDown ||
                              e.rawType == EventType.MouseUp))
            {
                _keyboardSelection = false;
                _selected = -1;
            }

            if (HandleKeyboard()) return;

            Slab.Box(rect, SlopWidgets.PopoverBg, SlopWidgets.Edge);

            var inner = new Rect(rect.x, rect.y + PadY, rect.width, rect.height - PadY * 2f);
            bool scrolls = ContentH > rect.height;
            var view = new Rect(0f, 0f, inner.width - (scrolls ? SlopWidgets.ScrollbarW : 0f),
                ContentH - PadY * 2f);

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
                float h = Height(_options[i]);
                if (Row(new Rect(0f, y, view.width, h), _options[i], i, hot == i)) break;
                y += h;
            }

            _scroll.End();

            if (Event.current.type == EventType.Repaint) Pointer(hot);
        }

        int Hot(Rect viewport)
        {
            if (!Mouse.IsOver(viewport)) return -1;

            float y = Event.current.mousePosition.y - viewport.y + _scroll.Position.y;
            if (y < 0f) return -1;

            float top = 0f;
            for (int i = 0; i < _options.Count; i++)
            {
                float h = Height(_options[i]);
                if (y < top + h) return i;
                top += h;
            }

            return -1;
        }

        // The pointer walks the tree, once a frame. A row with a list opens it once the
        // pointer has rested the delay out; a row without one shuts the branch at once; a
        // pointer that has left this menu - into the child, which is another window, or off
        // the menu entirely - leaves what is open standing.
        void Pointer(int hot)
        {
            if (_keyboardSelection) return;

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

        // The screen y a submenu opened from row `i` hangs at. Its first row begins at this
        // same y because menus have no vertical inset; only this menu knows the scroll offset.
        float RowTop(int i)
        {
            float y = windowRect.y - _scroll.Position.y;
            for (int j = 0; j < i; j++) y += Height(_options[j]);
            return y;
        }

        void Follow(float anchor)
        {
            _anchor = anchor;
            Place();
        }

        // True if this row took the press.
        bool Row(Rect r, FloatMenuOption o, int i, bool hot)
        {
            if (o is SeparatorOption)
            {
                Slab.Hairline(new Rect(r.x + PadX, r.y + r.height / 2f,
                    r.width - PadX * 2f, 1f), SlopWidgets.Edge);
                return false;
            }

            bool on = !o.Disabled;
            bool over = on && hot;
            bool nest = o is SlopSubmenu;
            bool lit = over || _open == i || _selected == i;

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
                _selected = i;
                OpenChild(i, sub);
                return false;
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
            // The whole tree goes before the action runs: a row answers for the menu it is in
            // and for every menu that led to it, and an action that opens a second menu or a
            // dialog would otherwise arrive underneath the ones that asked for it.
            _selected = i;
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
