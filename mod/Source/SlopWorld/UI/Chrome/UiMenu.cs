using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    public class UiSubmenu : FloatMenuOption
    {
        // Asked when the pointer arrives rather than when the parent is built. A station's presets
        // carry which one is playing, and a tree built up front would say so once.
        public readonly Func<List<FloatMenuOption>> Children;

        // FloatMenuOption treats a null action as disabled, even for submenu openers.
        public UiSubmenu(string label, Func<List<FloatMenuOption>> children)
            : base(label, () => { })
        {
            Children = children;
        }
    }

    public partial class UiMenu : Window
    {
        sealed class SeparatorOption : FloatMenuOption
        {
            public SeparatorOption() : base(" ", null) { }
        }

        readonly List<FloatMenuOption> _options;
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly MenuRowGeometry _rows = new MenuRowGeometry();
        int _measuredCount = -1;
        float _measuredRowHeight;

        // Where to put it, for a menu that does not belong at the mouse. One opened from a button
        // the keyboard reached, or one that reopens itself a tick at a time and would otherwise
        // walk across the screen behind the cursor. Null is the mouse.
        readonly Vector2? _at;

        // Selector menus own a control underneath them. Keep that distinction so the
        // control can show its open state and a click on its source closes the menu without
        // falling through and opening it again.
        readonly bool _selector;

        UiMenu _parent;
        UiMenu _child;

        // Keep the parent row lit while the pointer is in its child menu.
        int _open = -1;

        // The row the pointer is on and when it arrived there, which is what the delay before
        // a list opens is counted from.
        int _hot = -1;
        float _hotAt;

        // Where a submenu hangs: the screen y of the parent row that opened it. Written by
        // the parent rather than kept from the moment it opened - see `Place`.
        float _anchor;

        // Keep the size that a submenu requested before placement.
        // `Place` uses it each frame, and `InitialSize` measures all labels to calculate it.
        // A submenu can then widen again after temporary screen limits no longer apply.
        Vector2 _want;

        const float MaxW = 300f;
        const float MinW = 160f;

        const float PadX = UiTheme.MenuPadX;
        const float PadY = UiTheme.MenuPadY;

        const float MaxScreen = 0.6f;

        const float ArrowW = UiTheme.DisclosureW;

        const float OpenDelay = 0.18f;

        static float RowH => UiTheme.MenuRowH;
        const float SeparatorH = 8f;

        // The frame this menu was built on - see `HandleKeyboard`.
        readonly int _born = Time.frameCount;

        public UiMenu(List<FloatMenuOption> options, Vector2? at = null, bool selector = false)
        {
            _options = options ?? new List<FloatMenuOption>();
            _at = at;
            _selector = selector;

            doWindowBackground = false;
            doCloseX = false;
            doCloseButton = false;
            closeOnClickedOutside = true;
            closeOnCancel = true;
            closeOnAccept = true;
            drawShadow = false;
            absorbInputAroundWindow = selector;
            preventCameraMotion = false;
            layer = WindowLayer.Super;

            // WindowStack removes same-type windows before PreOpen. Disable that behavior so
            // nested menus survive. Root PreOpen sweeps unrelated menus instead.
            onlyOneOfTypeAllowed = false;
        }

        UiMenu(List<FloatMenuOption> options, UiMenu parent, float anchor)
            : this(options)
        {
            _parent = parent;
            _anchor = anchor;
        }

        protected override float Margin => 0f;

        public static void Open(List<FloatMenuOption> options) =>
            Find.WindowStack.Add(new UiMenu(options));

        // A catalog update can replace a submenu with a plain row or remove its anchor.
        // Close the old child before replacing rows so it cannot keep stale actions alive.
        protected void ReplaceOptions(List<FloatMenuOption> options)
        {
            CloseChild();
            _options.Clear();
            _options.AddRange(options);
            _measuredCount = -1;
            _hot = _selected = -1;
            _keyboardSelection = false;
        }

        // Focus changes can happen before the window stack gets a chance to dismiss a menu.
        // Callers that replace the screen explicitly close the whole chain rather than
        // leaving a menu owned by the old focus standing behind it.
        public static void CloseAll() => Sweep(null);

        public static bool IsSelectorOpenAt(Vector2 at)
        {
            var stack = Find.WindowStack;
            if (stack == null) return false;

            for (int i = 0; i < stack.Count; i++)
            {
                var menu = stack[i] as UiMenu;
                if (menu == null || !menu._selector || menu._parent != null || !menu._at.HasValue)
                    continue;
                if ((menu._at.Value - at).sqrMagnitude < 0.25f) return true;
            }

            return false;
        }

        // FloatMenuOption replaces empty labels with "(missing label)". Use a distinct row type.
        public static FloatMenuOption Separator() => new SeparatorOption();

        // The width a plain labelled picker gets when its menu contains the same labels. Keep
        // this beside InitialSize so the control and the opened list do not disagree about
        // padding, minimums or the screen's maximum readable width.
        public static float WidthFor(IEnumerable<string> labels)
        {
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                float widest = 0f;
                if (labels != null)
                {
                    foreach (var label in labels)
                    {
                        widest = Mathf.Max(widest, UiTheme.Wide(label));
                        if (widest + PadX * 2f >= MaxW) break;
                    }
                }
                return Mathf.Clamp(widest + PadX * 2f, MinW, MaxW);
            }
        }

        static float Height(FloatMenuOption option) =>
            option is SeparatorOption ? SeparatorH : RowH;

        void MeasureRows()
        {
            if (_measuredCount == _options.Count && _measuredRowHeight == RowH) return;
            _rows.Build(_options.Count, i => Height(_options[i]));
            _measuredCount = _options.Count;
            _measuredRowHeight = RowH;
        }

        float ContentH
        {
            get { MeasureRows(); return _rows.Height + PadY * 2f; }
        }

        float WidestLabel()
        {
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                float w = 0f;
                foreach (var o in _options)
                {
                    if (o is SeparatorOption) continue;
                    w = Mathf.Max(w, UiTheme.Wide(o.Label) + o.extraPartWidth +
                        (o is UiSubmenu ? ArrowW + UiTheme.GapXS : 0f));
                    if (w + PadX * 2f >= MaxW) break;
                }
                return w;
            }
        }

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Clamp(WidestLabel() + PadX * 2f, MinW, MaxW),
                Mathf.Min(ContentH, UI.screenHeight * MaxScreen));

        public override void PreOpen()
        {
            base.PreOpen();
            if (_parent == null) Sweep(this);
        }

        static void Sweep(UiMenu keep)
        {
            var stack = Find.WindowStack;
            if (stack == null) return;

            // Downward, because closing one takes its branch - which stands above it - with
            // it. Nothing below the index being read moves, so the walk stays valid.
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                var m = stack[i] as UiMenu;
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
            using (WidgetState.Save()) DrawContents(rect);
        }

        void DrawContents(Rect rect)
        {
            var inner = new Rect(rect.x, rect.y + PadY, rect.width, rect.height - PadY * 2f);
            // Wheel passes reuse measured bounds and skip row controls and labels.
            if (_measuredCount == _options.Count && _measuredRowHeight == RowH &&
                _scroll.HandleWheel(inner)) return;

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

            Slab.Box(rect, UiTheme.PopoverBg, UiTheme.Edge);

            bool scrolls = ContentH > rect.height;
            var view = new Rect(0f, 0f, inner.width - (scrolls ? UiTheme.ScrollbarW : 0f),
                ContentH - PadY * 2f);

            // Check the pointer position once for the list before the scroll view opens its group.
            // The rows fill the viewport exactly, so calculate the row from the pointer position.
            // Use the result to highlight the row and open its list.
            int hot = Hot(new Rect(inner.x, inner.y, view.width, inner.height));

            using (WidgetState.Save())
            using (_scroll.Scope(inner, view))
            {
                Text.Font = GameFont.Small;
                // Stop after the selected row.
                // The selection closes this menu and can open another menu.
                // Continuing could let a second option process the same click.
                _rows.Visible(_scroll.Position.y, inner.height, out int first, out int end);
                for (int i = first; i < end; i++)
                {
                    if (Row(new Rect(0f, _rows.Top(i), view.width, _rows.RowHeight(i)),
                        _options[i], i, hot == i)) break;
                }
            }

            if (Event.current.type == EventType.Repaint) Pointer(hot);
        }

        int Hot(Rect viewport)
        {
            if (!Mouse.IsOver(viewport)) return -1;

            float y = Event.current.mousePosition.y - viewport.y + _scroll.Position.y;
            if (y < 0f) return -1;

            return _rows.At(y);
        }

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

            // Keep an open submenu aligned with its row.
            // During the delay, keep the last submenu open instead of changing it for brief pointer movement.
            if (_open == hot)
            {
                if (_child != null) _child.Follow(RowTop(hot));
                return;
            }

            if (Time.realtimeSinceStartup - _hotAt >= OpenDelay) OpenChild(hot, sub);
        }

        UiSubmenu Sub(int i) =>
            _options[i].Disabled ? null : _options[i] as UiSubmenu;

        // The screen y a submenu opened from row `i` hangs at. Its first row begins at this
        // same y because menus have no vertical inset. Only this menu knows the scroll offset.
        float RowTop(int i)
        {
            return windowRect.y - _scroll.Position.y + _rows.Top(i);
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
                    r.width - PadX * 2f, 1f), UiTheme.Edge);
                return false;
            }

            bool on = !o.Disabled;
            bool over = on && hot;
            bool nest = o is UiSubmenu;
            bool lit = over || _open == i || _selected == i;

            RowChrome.Hover(r, _selected == i, on, lit, RowHoverPolicy.Local,
                RowSelectionStyle.Hover);
            if (o.tooltip.HasValue) TooltipHandler.TipRegion(r, o.tooltip.Value);

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
                using (WidgetState.Save())
                {
                    GUI.color = !on ? UiTheme.Off : lit ? UiTheme.Lead : UiTheme.Faint;
                    GUI.DrawTexture(mark, TexButton.Reveal);
                }
            }

            var label = new Rect(r.x + PadX + (right ? 0f : extra), r.y,
                r.width - PadX * 2f - extra - (nest ? ArrowW + UiTheme.GapXS : 0f),
                r.height);
            using (WidgetState.Save())
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = !on ? UiTheme.Off : lit ? UiTheme.Lead : UiTheme.Name;
                UiText.RowLabel(label, o.Label);
            }

            if (!UiButtons.RowButton(r, on)) return false;

            // A row with a list under it is an address rather than an answer, and a press on
            // one is the pointer saying it will not wait out the delay. Opened here rather
            // than swallowed outright because clicking a window shuts every window above it,
            // this one's child included.
            var sub = o as UiSubmenu;
            if (sub != null)
            {
                _selected = i;
                OpenChild(i, sub);
                return false;
            }

            SoundDefOf.Click.PlayOneShotOnCamera();
            // Close the complete menu tree before running the action.
            // Otherwise, a new menu or dialog could open below the menus that started the action.
            _selected = i;
            CloseTree();
            if (o.action != null) o.action();
            return true;
        }

        void OpenChild(int i, UiSubmenu sub)
        {
            CloseChild();
            // Claimed before the list is asked for. Therefore, a row that turns out to have nothing
            // under it is asked once rather than once a frame for as long as it is hovered.
            _open = i;

            var kids = sub.Children != null ? sub.Children() : null;
            if (kids == null || kids.Count == 0) return;

            _child = new UiMenu(kids, this, RowTop(i));
            Find.WindowStack.Add(_child);
        }

        void CloseChild()
        {
            _open = -1;
            var c = _child;
            _child = null;
            if (c != null) c.CloseBranch();
        }

        // This menu and everything it has open below it. The link upward is cut first. The parent
        // is the one doing the closing in every path that reaches here. The notification it would
        // get back from `PostClose` is one it does not need.
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

        // Escape puts the whole tree away rather than one level of it. The pointer is what walks
        // back up a menu that opens on hover. Therefore, a level dismissed on its own would reopen
        // as soon as the mouse moved.
        public override void OnCancelKeyPressed()
        {
            CloseTree();
            Event.current.Use();
        }
    }
}
