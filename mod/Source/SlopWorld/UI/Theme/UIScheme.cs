using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Defines named palettes for shared UI chrome.
    // The runtime loader reads the packaged catalog. Geometry stays fixed while selected colors take effect on the next frame.
    // The game persists hex values in settings. Catalog values remain editable content.
    public class UIScheme
    {
        public readonly string Id, Label;
        public readonly Color Accent, Destructive;
        public readonly Color AccentText, DestructiveText;
        public readonly Color CheckFace;
        public readonly Color WindowBg, ViewBg, PopoverBg, Panel, OfflineBg, Scrim;
        public readonly Color Lead, Name, Dim, Faint, Off;
        public readonly Color Bad, Warn, Yes, Global;
        public readonly Color Edge, EdgeLit;
        public readonly Color ScrollTrough, ScrollThumb, ScrollThumbHover, ScrollThumbHeld;
        public readonly Color RowBg, RowOn, Hover;
        public readonly Color StateWorking, StateWaiting, StateIdle, StateDown;
        public readonly Color BtnFace, BtnHover, BtnDown, Knob;

        // These are derived roles, not catalog choices. Well is the view surface and Sel is the
        // accent at the one strength that keeps the row under it readable.
        public readonly Color Well, Sel;
        public readonly Color[] Swatches;

        UIScheme(ThemeCatalog.UiRecord record)
        {
            Id = record.Id;
            Label = record.Label;
            Accent = Hex(record.Accent);
            Destructive = Hex(record.Destructive);
            AccentText = Hex(record.AccentText);
            DestructiveText = Hex(record.DestructiveText);
            CheckFace = Hex(record.CheckFace);
            WindowBg = Hex(record.WindowBg);
            ViewBg = Hex(record.ViewBg);
            PopoverBg = Hex(record.PopoverBg);
            Panel = Hex(record.Panel);
            OfflineBg = Hex(record.OfflineBg);
            Scrim = Hex(record.Scrim);
            Lead = Hex(record.Lead);
            Name = Hex(record.Name);
            Dim = Hex(record.Dim);
            Faint = Hex(record.Faint);
            Off = Hex(record.Off);
            Bad = Hex(record.Bad);
            Warn = Hex(record.Warn);
            Yes = Hex(record.Yes);
            Global = Hex(record.Global);
            Edge = Hex(record.Edge);
            EdgeLit = Hex(record.EdgeLit);
            ScrollTrough = Hex(record.ScrollTrough);
            ScrollThumb = Hex(record.ScrollThumb);
            ScrollThumbHover = Hex(record.ScrollThumbHover);
            ScrollThumbHeld = Hex(record.ScrollThumbHeld);
            RowBg = Hex(record.RowBg);
            RowOn = Hex(record.RowOn);
            Hover = Hex(record.Hover);
            StateWorking = Hex(record.StateWorking);
            StateWaiting = Hex(record.StateWaiting);
            StateIdle = Hex(record.StateIdle);
            StateDown = Hex(record.StateDown);
            BtnFace = Hex(record.BtnFace);
            BtnHover = Hex(record.BtnHover);
            BtnDown = Hex(record.BtnDown);
            Knob = Hex(record.Knob);
            Well = ViewBg;
            Sel = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
            Swatches = new[]
            {
                Lead, Name, Dim, Faint, Off,
                Accent, Destructive,
                Yes, Warn, Bad, Global,
                StateWorking, StateWaiting, StateIdle, StateDown,
                EdgeLit,
            };
        }

        // Six digits is opaque. Eight carries alpha. Invalid values are magenta so a broken
        // hand-edited catalog is visible during development instead of silently black.
        static Color Hex(string s) =>
            HexColor.TryHex(s, out var c) ? c : Color.magenta;

        // Catalog validation keeps Accent, Destructive and CheckFace opaque.
        // Callers use those roles without opacity fades when selecting contrast.
        public static Color TextOn(Color background) =>
            Contrast(Color.white, background) >= Contrast(Color.black, background)
                ? Color.white : Color.black;

        static float Contrast(Color a, Color b)
        {
            float x = Luminance(a), y = Luminance(b);
            if (x < y) { float t = x; x = y; y = t; }
            return (x + 0.05f) / (y + 0.05f);
        }

        static float Luminance(Color c)
        {
            float Linear(float x) => x <= 0.04045f
                ? x / 12.92f
                : Mathf.Pow((x + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);
        }

        static List<UIScheme> _all;

        public static List<UIScheme> All => _all ?? (_all = ThemeLoader.Load("UI",
            catalog => catalog.UISchemes.ConvertAll(record => new UIScheme(record)), Fallback));

        static UIScheme Fallback() => new UIScheme(new ThemeCatalog.UiRecord(
            order: 0, id: "slopworld-warm", label: "SlopWorld Warm",
            accent: "#a97c39", destructive: "#b43424",
            accentText: "#17120a", destructiveText: "#ffffff", checkFace: "#876335",
            windowBg: "#2b2014", viewBg: "#221b11", popoverBg: "#312722",
            panel: "#2b2014f5", offlineBg: "#6b1f1aeb", scrim: "#0000008c",
            lead: "#ffffff", name: "#ffffffcc", dim: "#ffffff8c",
            faint: "#ffffff6b", off: "#ffffff4d",
            bad: "#ff7b63", warn: "#f8c65a", yes: "#7bbd67", global: "#dcb5cf",
            edge: "#ffffff24", edgeLit: "#ba8f5be6",
            scrollTrough: "#ffffff0a", scrollThumb: "#ffffff47",
            scrollThumbHover: "#ffffff6b", scrollThumbHeld: "#ffffff8c",
            rowBg: "#ffffff08", rowOn: "#ffffff1a", hover: "#ffffff0f",
            stateWorking: "#91bdd8", stateWaiting: "#facc4d",
            stateIdle: "#a79c87", stateDown: "#c86a50",
            btnFace: "#ffffff1a", btnHover: "#ffffff26", btnDown: "#ffffff4d",
            knob: "#ffffff"));

        public static UIScheme Get(string id)
        {
            foreach (var scheme in All) if (scheme.Id == id) return scheme;
            return All[0];
        }

        static UIScheme _current;
        static string _id;

        // Resolve on each read because settings returns the same string reference and chrome
        // colors are not cached. No revision invalidation is needed.
        public static UIScheme Current
        {
            get
            {
                string id = Settings.UIScheme;
                if (_current == null || _id != id)
                {
                    _current = Get(id);
                    _id = id;
                }
                return _current;
            }
        }
    }
}
