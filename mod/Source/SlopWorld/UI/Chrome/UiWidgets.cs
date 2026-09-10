using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Compatibility facade for existing windows and pages. Ownership lives in UiTheme,
    // UiText, UiButtons, UiControls and UiLayout; this type deliberately contains only
    // explicit forwards while callers migrate by feature.
    public abstract class UiWidgets
    {
        public enum Btn
        {
            Default,
            Primary,
            Danger,
            Ghost,
        }

        static UiTheme.Btn Core(Btn kind) => (UiTheme.Btn)(int)kind;

        public static int AtlasRevision => UiTheme.AtlasRevision;
        public static Color Accent => UiTheme.Accent;
        public static Color Destructive => UiTheme.Destructive;
        public static Color WindowBg => UiTheme.WindowBg;
        public static Color ViewBg => UiTheme.ViewBg;
        public static Color PopoverBg => UiTheme.PopoverBg;
        public static Color Lead => UiTheme.Lead;
        public static Color Name => UiTheme.Name;
        public static Color Dim => UiTheme.Dim;
        public static Color Faint => UiTheme.Faint;
        public static Color Off => UiTheme.Off;
        public static Color Bad => UiTheme.Bad;
        public static Color Warn => UiTheme.Warn;
        public static Color Global => UiTheme.Global;
        public static Color Panel => UiTheme.Panel;
        public static Color OfflineBg => UiTheme.OfflineBg;
        public static Color Scrim => UiTheme.Scrim;
        public static Color Edge => UiTheme.Edge;
        public static Color EdgeLit => UiTheme.EdgeLit;
        public static Color ScrollTrough => UiTheme.ScrollTrough;
        public static Color ScrollThumb => UiTheme.ScrollThumb;
        public static Color ScrollThumbHover => UiTheme.ScrollThumbHover;
        public static Color ScrollThumbHeld => UiTheme.ScrollThumbHeld;
        public static Color Well => UiTheme.Well;
        public static Color Yes => UiTheme.Yes;
        public static Color RowBg => UiTheme.RowBg;
        public static Color RowOn => UiTheme.RowOn;
        public static Color Sel => UiTheme.Sel;
        public static Color Hover => UiTheme.Hover;
        public static Color StateWorking => UiTheme.StateWorking;
        public static Color StateWaiting => UiTheme.StateWaiting;
        public static Color StateIdle => UiTheme.StateIdle;
        public static Color StateDown => UiTheme.StateDown;
        public static Color Info => UiTheme.Info;
        public static Color Clear => UiTheme.Clear;
        public static Color Fade(Color color, float by) => UiTheme.Fade(color, by);
        public static float LineH => UiTheme.LineH;
        public static float FieldH => UiTheme.FieldH;
        public static float RowH => UiTheme.RowH;
        public static float HeaderH => UiTheme.HeaderH;
        public static float LineHOf(GameFont font) => UiTheme.LineHOf(font);
        public static GameFont Real(GameFont font) => UiTheme.Real(font);
        public static float TinyH => UiTheme.TinyH;
        public static float TinyRowH => UiTheme.TinyRowH;
        public static float Wide(string text) => UiTheme.Wide(text);
        public static string TruncateText(string text, float width) => UiTheme.TruncateText(text, width);
        public static float BtnH => UiTheme.BtnH;
        public static float ButtonPadX => UiTheme.ButtonPadX;
        public static float ButtonMinW => UiTheme.ButtonMinW;
        public static float FieldPadX => UiTheme.FieldPadX;
        public static float FieldPadY => UiTheme.FieldPadY;
        public const float IconInset = UiTheme.IconInset;
        public const float IconW = UiTheme.IconW;
        public const float PickerCell = UiTheme.PickerCell;
        public const float PickerIcon = UiTheme.PickerIcon;
        public const float ListInset = UiTheme.ListInset;
        public const float DisclosureW = UiTheme.DisclosureW;
        public const float ScrollbarW = UiTheme.ScrollbarW;
        public const float ScrollTrackW = UiTheme.ScrollTrackW;
        public const float ScrollThumbInset = UiTheme.ScrollThumbInset;
        public const float MenuPadX = UiTheme.MenuPadX;
        public const float MenuPadY = UiTheme.MenuPadY;
        public const float StatusMarker = UiTheme.StatusMarker;
        public static float CompactH => UiTheme.CompactH;
        public static float RowBtnH => UiTheme.RowBtnH;
        public static float MenuRowH => UiTheme.MenuRowH;
        public static float PaletteRowH => UiTheme.PaletteRowH;
        public static float GapXS => UiTheme.GapXS;
        public static float GapS => UiTheme.GapS;
        public static float GapM => UiTheme.GapM;
        public static float GapL => UiTheme.GapL;

        public static float StatusLabelHeight(string text, float width,
                                               GameFont font = GameFont.Small) =>
            UiText.StatusLabelHeight(text, width, font);

        public static void StatusLabel(Rect r, string text, Color color,
                                       GameFont font = GameFont.Small,
                                       TextAnchor anchor = TextAnchor.UpperLeft) =>
            UiText.StatusLabel(r, text, color, font, anchor);

        public static void RowLabel(Rect r, string text,
                                    TextAnchor anchor = TextAnchor.MiddleLeft) =>
            UiText.RowLabel(r, text, anchor);

        public static void RowLabel(Rect r, string text, TextAnchor anchor, bool italic) =>
            UiText.RowLabel(r, text, anchor, italic);

        public static string Field(Rect r, string name, string text, bool on = true,
                                   string defaultValue = null) =>
            UiText.Field(r, name, text, on, defaultValue);

        public static void ReadOnlyField(Rect r, string name, string text) =>
            UiText.ReadOnlyField(r, name, text);

        public static string Area(Rect r, string name, string text, bool on = true,
                                  bool frame = true, string defaultValue = null) =>
            UiText.Area(r, name, text, on, frame, defaultValue);

        public static void FieldFrame(Rect r, bool focused) => UiText.FieldFrame(r, focused);

        public static string BareField(Rect r, string name, string text) =>
            UiText.BareField(r, name, text);

        public static bool RowButton(Rect r, bool on = true) => UiButtons.RowButton(r, on);

        public static bool Button(Rect r, string label, Btn kind = Btn.Default, bool on = true) =>
            UiButtons.Button(r, label, Core(kind), on);

        // Internal implementations such as UiLayout.Bar still use the owner enum directly.
        public static bool Button(Rect r, string label, UiTheme.Btn kind, bool on = true) =>
            UiButtons.Button(r, label, kind, on);

        public static void ButtonBackground(Rect r, Btn kind, bool on, bool over, bool held) =>
            UiButtons.ButtonBackground(r, Core(kind), on, over, held);

        public static void ActionButtonBackground(Rect r, Btn kind, bool on, bool over,
                                                  bool held) =>
            UiButtons.ActionButtonBackground(r, Core(kind), on, over, held);

        public static float TickW => UiControls.TickW;
        public static float TickColW => UiControls.TickColW;
        public static Rect TickBox(Rect r, bool on, bool locked = false) =>
            UiControls.TickBox(r, on, locked);

        public static bool Checkbox(Rect r, string label, bool on, string tip = null,
                                    bool locked = false, bool warn = false) =>
            UiControls.Checkbox(r, label, on, tip, locked, warn);

        public static Rect FieldRect(Listing_Standard l) => UiControls.FieldRect(l);

        public static string Field(Listing_Standard l, string name, string text, bool on = true,
                                   string defaultValue = null) =>
            UiControls.Field(l, name, text, on, defaultValue);

        public static string Area(Listing_Standard l, float height, string name, string text,
                                  bool on = true, bool frame = true, string defaultValue = null) =>
            UiControls.Area(l, height, name, text, on, frame, defaultValue);

        public static bool SetSetting<T>(ModSettings settings, ref T field, T value) =>
            UiControls.SetSetting(settings, ref field, value);

        public static void CheckboxSetting(Listing_Standard l, string label, ModSettings settings,
                                           ref bool field, string tip = null) =>
            UiControls.CheckboxSetting(l, label, settings, ref field, tip);

        public static bool SliderSetting(Listing_Standard l, string label, ModSettings settings,
                                         ref int field, int min, int max) =>
            UiControls.SliderSetting(l, label, settings, ref field, min, max);

        public static bool Checkbox(Listing_Standard l, string label, bool on, string tip = null,
                                    bool locked = false) =>
            UiControls.Checkbox(l, label, on, tip, locked);

        public static bool Select(Rect r, string caption, string value, out Rect box,
                                  string tip = null, bool on = true, bool open = false,
                                  float forcedWidth = 0f) =>
            UiControls.Select(r, caption, value, out box, tip, on, open, forcedWidth);

        public static bool Select(Rect r, string caption, string value,
                                  IEnumerable<FloatMenuOption> choices, out Rect box,
                                  string tip = null, bool on = true, bool open = false,
                                  Action<UiMenu> openMenu = null) =>
            UiControls.Select(r, caption, value, choices, out box, tip, on, open, openMenu);

        public static bool Select(Rect r, string caption, string value,
                                  IEnumerable<SelectorOption> choices, out Rect box,
                                  string tip = null, bool on = true, bool open = false,
                                  Action<UiMenu> openMenu = null) =>
            UiControls.Select(r, caption, value, choices, out box, tip, on, open, openMenu);

        public static bool Select(Listing_Standard l, string caption, string value,
                                  out Rect box, string tip = null, bool on = true) =>
            UiControls.Select(l, caption, value, out box, tip, on);

        public static bool Select(Listing_Standard l, string caption, string value,
                                  IEnumerable<FloatMenuOption> choices, out Rect box,
                                  string tip = null, bool on = true, bool open = false,
                                  Action<UiMenu> openMenu = null) =>
            UiControls.Select(l, caption, value, choices, out box, tip, on, open, openMenu);

        public static bool Select(Listing_Standard l, string caption, string value,
                                  IEnumerable<SelectorOption> choices, out Rect box,
                                  string tip = null, bool on = true, bool open = false,
                                  Action<UiMenu> openMenu = null) =>
            UiControls.Select(l, caption, value, choices, out box, tip, on, open, openMenu);

        public static Vector2 MenuAt(Rect r) => UiControls.MenuAt(r);

        public static float Slider(Listing_Standard l, string label, float value,
                                   string tip = null) => UiControls.Slider(l, label, value, tip);

        public static float Slider(Listing_Standard l, string label, float value,
                                   float min, float max, string readout, string tip = null) =>
            UiControls.Slider(l, label, value, min, max, readout, tip);

        public static float Slider(Listing_Standard l, string label, float value,
                                   float min, float max, string readout, out bool held,
                                   string tip = null) =>
            UiControls.Slider(l, label, value, min, max, readout, out held, tip);

        public static float Slider(Listing_Standard l, string label, float value,
                                   float min, float max, string readout, out bool held,
                                   out bool released, string tip = null) =>
            UiControls.Slider(l, label, value, min, max, readout, out held, out released, tip);

        public const float ListingHeight = UiLayout.ListingHeight;
        public static bool Shown => UiLayout.Shown;
        public static WorkspaceGeometry Snapshot => UiLayout.Snapshot;
        public static float LeftInset => UiLayout.LeftInset;
        public static float RightInset => UiLayout.RightInset;
        public static float TopInset => UiLayout.TopInset;
        public static Rect ContentRect => UiLayout.ContentRect;
        public static bool Hidden => UiLayout.Hidden;
        public static float BtnW(string label, float floor) => UiLayout.BtnW(label, floor);

        public static bool Button(Listing_Standard l, string label, Btn kind = Btn.Default,
                                  bool on = true) => UiLayout.Button(l, label, Core(kind), on);

        public static bool Button(Listing_Standard l, string label, UiTheme.Btn kind,
                                  bool on = true) => UiLayout.Button(l, label, kind, on);

        public static bool IconButton(Rect r, Texture2D icon, Color tint, bool on = true) =>
            UiLayout.IconButton(r, icon, tint, on);

        public static bool IconButton(Rect r, Texture2D icon, Color tint, float inset,
                                      bool on = true) =>
            UiLayout.IconButton(r, icon, tint, inset, on);

        public static bool IconButton(Rect r, Texture2D icon, bool on = true) =>
            UiLayout.IconButton(r, icon, on);

        public static void SectionHeading(Listing_Standard l, string text) =>
            UiLayout.SectionHeading(l, text);

        public static void SectionHeading(Rect r, string text) => UiLayout.SectionHeading(r, text);

        public static void Note(Listing_Standard l, string text) => UiLayout.Note(l, text);

        public struct Bar
        {
            UiLayout.Bar _inner;

            public Bar(Rect rect) { _inner = new UiLayout.Bar(rect); }

            public bool Left(string label, Btn kind = Btn.Default, bool on = true) =>
                _inner.Left(label, Core(kind), on);

            public bool Right(string label, Btn kind = Btn.Default, bool on = true) =>
                _inner.Right(label, Core(kind), on);

            public Rect Rest() => _inner.Rest();
        }

        public const string Unreachable = UiLayout.Unreachable;

        public static void Fail(string msg) => UiLayout.Fail(msg);

        public static FloatMenuOption MenuToggle(string label, bool on, Action act) =>
            UiLayout.MenuToggle(label, on, act);

        public static IEnumerable<FloatMenuOption> GroupedFontOptions(IEnumerable<string> names,
                                                                       Action<string> choose) =>
            UiLayout.GroupedFontOptions(names, choose);

        public static void Header(Rect rect, string title, SessionHub hub) =>
            UiLayout.Header(rect, title, hub);

        public static void PageCaption(Rect page, string text) => UiLayout.PageCaption(page, text);
        public static Rect PageBody(Rect page) => UiLayout.PageBody(page);
        public static Rect FooterBar(Rect rect) => UiLayout.FooterBar(rect);
        public static void Title(Rect rect, string text) => UiLayout.Title(rect, text);
        public static string PathList(Rect r, string name, string label, string text) =>
            UiLayout.PathList(r, name, label, text);
        public static string FreeName(string name, IEnumerable<string> taken, string fallback) =>
            UiLayout.FreeName(name, taken, fallback);
        public static void DrawRail<T>(Rect r, (string label, T tab)[] tabs, ref T active,
                                       Func<T, bool> enabled = null) =>
            UiLayout.DrawRail(r, tabs, ref active, enabled);
    }
}
