using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

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

        UIScheme(string id, string label,
                 string accent, string destructive,
                 string accentText, string destructiveText, string checkFace,
                 string windowBg, string viewBg, string popoverBg, string panel,
                 string offlineBg, string scrim,
                 string lead, string name, string dim, string faint, string off,
                 string bad, string warn, string yes, string global,
                 string edge, string edgeLit,
                 string scrollTrough, string scrollThumb,
                 string scrollThumbHover, string scrollThumbHeld,
                 string rowBg, string rowOn, string hover,
                 string stateWorking, string stateWaiting, string stateIdle, string stateDown,
                 string btnFace, string btnHover, string btnDown, string knob)
        {
            Id = id;
            Label = label;
            Accent = Hex(accent);
            Destructive = Hex(destructive);
            AccentText = Hex(accentText);
            DestructiveText = Hex(destructiveText);
            CheckFace = Hex(checkFace);
            WindowBg = Hex(windowBg);
            ViewBg = Hex(viewBg);
            PopoverBg = Hex(popoverBg);
            Panel = Hex(panel);
            OfflineBg = Hex(offlineBg);
            Scrim = Hex(scrim);
            Lead = Hex(lead);
            Name = Hex(name);
            Dim = Hex(dim);
            Faint = Hex(faint);
            Off = Hex(off);
            Bad = Hex(bad);
            Warn = Hex(warn);
            Yes = Hex(yes);
            Global = Hex(global);
            Edge = Hex(edge);
            EdgeLit = Hex(edgeLit);
            ScrollTrough = Hex(scrollTrough);
            ScrollThumb = Hex(scrollThumb);
            ScrollThumbHover = Hex(scrollThumbHover);
            ScrollThumbHeld = Hex(scrollThumbHeld);
            RowBg = Hex(rowBg);
            RowOn = Hex(rowOn);
            Hover = Hex(hover);
            StateWorking = Hex(stateWorking);
            StateWaiting = Hex(stateWaiting);
            StateIdle = Hex(stateIdle);
            StateDown = Hex(stateDown);
            BtnFace = Hex(btnFace);
            BtnHover = Hex(btnHover);
            BtnDown = Hex(btnDown);
            Knob = Hex(knob);
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

        UIScheme(ThemeCatalog.UiRecord record)
            : this(record.Id, record.Label,
                   record.Accent, record.Destructive,
                   record.AccentText, record.DestructiveText, record.CheckFace,
                   record.WindowBg, record.ViewBg, record.PopoverBg, record.Panel,
                   record.OfflineBg, record.Scrim,
                   record.Lead, record.Name, record.Dim, record.Faint, record.Off,
                   record.Bad, record.Warn, record.Yes, record.Global,
                   record.Edge, record.EdgeLit,
                   record.ScrollTrough, record.ScrollThumb,
                   record.ScrollThumbHover, record.ScrollThumbHeld,
                   record.RowBg, record.RowOn, record.Hover,
                   record.StateWorking, record.StateWaiting, record.StateIdle, record.StateDown,
                   record.BtnFace, record.BtnHover, record.BtnDown, record.Knob)
        { }

        // Six digits is opaque. Eight carries alpha. Invalid values are magenta so a broken
        // hand-edited catalog is visible during development instead of silently black.
        static Color Hex(string s) =>
            TerminalTheme.TryHex(s, out var c) ? c : Color.magenta;

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

        public static List<UIScheme> All
        {
            get
            {
                if (_all == null) _all = LoadAll();
                return _all;
            }
        }

        static List<UIScheme> LoadAll()
        {
            try
            {
                string root = ModEntry.Instance?.Content?.RootDir;
                var catalog = ThemeCatalog.Load(root);
                var schemes = new List<UIScheme>(catalog.UISchemes.Count);
                foreach (var record in catalog.UISchemes) schemes.Add(new UIScheme(record));
                return schemes;
            }
            catch (Exception e)
            {
                // The build rejects invalid shipped data. This keeps a manually copied DLL
                // usable if its content directory was omitted.
                Log.Error("[SlopWorld] could not load UI theme catalog: " + e);
                return new List<UIScheme> { Fallback() };
            }
        }

        static UIScheme Fallback() => new UIScheme("slopworld-warm", "SlopWorld Warm",
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
            knob: "#ffffff");

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
