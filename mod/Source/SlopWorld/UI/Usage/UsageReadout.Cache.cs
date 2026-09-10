using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static partial class UsageReadout
    {
        sealed class QuotaText
        {
            public string Key, Count, Tip;
            public UsageWindow Window;
            public long Age = long.MinValue;
        }

        static readonly UsageRowsCache RowCache = new UsageRowsCache();
        static readonly List<QuotaText> Quotas = new List<QuotaText>();
        static readonly ClockTextCache Clock = new ClockTextCache();
        static bool _spent;
        static CultureInfo _culture;
        static readonly Dictionary<string, float> Widths = new Dictionary<string, float>();
        static GUIStyle _style;
        static Font _font;
        static int _size, _atlas, _measuredAtlas = -1;
        static FontStyle _fontStyle;
        static float _scale;

        static UsageReadout()
        {
            Font.textureRebuilt += _ => _atlas++;
        }

        static float Width(string text)
        {
            var style = Text.CurFontStyle;
            if (_style != style || _font != style.font || _size != style.fontSize ||
                _fontStyle != style.fontStyle || _scale != Prefs.UIScale || _measuredAtlas != _atlas)
            {
                Widths.Clear();
                _style = style;
                _font = style.font;
                _size = style.fontSize;
                _fontStyle = style.fontStyle;
                _scale = Prefs.UIScale;
                _measuredAtlas = _atlas;
            }
            if (Widths.TryGetValue(text, out float width)) return width;
            if (Widths.Count >= 256) Widths.Clear();
            return Widths[text] = UiTheme.Wide(text);
        }

        static void PrepareClock(DateTime now)
        {
            if (Clock.Prepare(now, Settings.TimeFormat, CultureInfo.CurrentCulture))
                PerfTrace.Count("topbar-clock-rebuilds");
        }

        static void PrepareQuotas(UsageInfo usage)
        {
            bool changed = RowCache.Prepare(usage, SessionHub.Instance.Config);
            if (!changed && _spent == Settings.UsageSpent && ReferenceEquals(_culture, CultureInfo.CurrentCulture))
                return;
            _spent = Settings.UsageSpent;
            _culture = CultureInfo.CurrentCulture;
            Quotas.Clear();
            foreach (var key in RowCache.Rows)
            {
                var window = Window(usage, key);
                Quotas.Add(new QuotaText
                {
                    Key = key,
                    Window = window,
                    Count = window == null ? Unsaid : Count(window)
                });
            }
            PerfTrace.Count("topbar-quota-rebuilds");
        }

        static void CachedTip(Rect chip, UsageInfo usage, QuotaText row)
        {
            long age = (long)usage.Age;
            if (row.Age != age)
            {
                row.Age = age;
                row.Tip = TipText(usage, row.Key, row.Window);
            }
            TooltipHandler.TipRegion(chip, new TipSignal(row.Tip,
                0x51_0F_0000 ^ (row.Key?.GetHashCode() ?? 0)));
        }
    }
}
