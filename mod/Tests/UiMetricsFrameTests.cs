using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Verse;

namespace SlopWorld.Tests
{
    static class UiMetricsFrameTests
    {
        sealed class MetricScope : IDisposable
        {
            readonly string _density = Settings.S.uiDensity;
            readonly float _scale = Prefs.UIScale;
            readonly bool _tiny = Text.TinyFontSupported;
            readonly int _font = UiFont.RevisionValue, _atlas = UiTheme.AtlasRevisionValue;
            public MetricScope()
            {
                Settings.S.uiDensity = "default";
                Prefs.UIScale = 1f;
                Text.TinyFontSupported = true;
                Time.frameCount++;
                UiMetrics.BeginFrame();
            }
            public void Dispose()
            {
                Settings.S.uiDensity = _density;
                Prefs.UIScale = _scale;
                Text.TinyFontSupported = _tiny;
                UiFont.RevisionValue = _font;
                UiTheme.AtlasRevisionValue = _atlas;
                Time.frameCount++;
                UiMetrics.BeginFrame();
            }
        }

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (string kind in new[] { "density", "scale", "font", "atlas" })
                yield return ($"UI {kind} changes invalidate only their revision category on next frame", () => Revisions(kind));
            foreach (bool compact in new[] { false, true })
                yield return ($"{(compact ? "compact" : "default")} UI metric accessors expose resolved geometry", () => Values(compact));
        }

        static void Revisions(string kind)
        {
            using var scope = new MetricScope();
            int revision = UiMetrics.Revision, density = UiMetrics.DensityRevision,
                typography = UiMetrics.TypographyRevision, scale = UiMetrics.ScaleRevision;
            switch (kind)
            {
                case "density": Settings.S.uiDensity = "compact"; break;
                case "scale": Prefs.UIScale = 1.5f; break;
                case "font": UiFont.RevisionValue++; break;
                case "atlas": UiTheme.AtlasRevisionValue++; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            Assert.That(UiMetrics.Revision, Is.EqualTo(revision), "event passes use one snapshot");
            Assert.That(UiMetrics.DensityRevision, Is.EqualTo(density));
            Assert.That(UiMetrics.TypographyRevision, Is.EqualTo(typography));
            Assert.That(UiMetrics.ScaleRevision, Is.EqualTo(scale));
            Time.frameCount++;
            Assert.That(UiMetrics.Revision, Is.EqualTo(revision + 1));
            Assert.That(UiMetrics.DensityRevision, Is.EqualTo(density + (kind == "density" ? 1 : 0)));
            Assert.That(UiMetrics.TypographyRevision, Is.EqualTo(typography + (kind == "font" || kind == "atlas" ? 1 : 0)));
            Assert.That(UiMetrics.ScaleRevision, Is.EqualTo(scale + (kind == "scale" ? 1 : 0)));
            Time.frameCount++;
            Assert.That(UiMetrics.Revision, Is.EqualTo(revision + 1), "stable settings do not invalidate again");
        }

        static void Values(bool compact)
        {
            using var scope = new MetricScope();
            Settings.S.uiDensity = compact ? "COMPACT" : "unknown";
            Time.frameCount++;
            Assert.That(UiMetrics.Density, Is.EqualTo(compact ? "compact" : "default"));
            Assert.That(UiMetrics.Compact, Is.EqualTo(compact));
            Assert.That(UiMetrics.GapXS, Is.EqualTo(compact ? 3f : 4f));
            Assert.That(UiMetrics.GapS, Is.EqualTo(compact ? 6f : 8f));
            Assert.That(UiMetrics.GapM, Is.EqualTo(compact ? 12f : 16f));
            Assert.That(UiMetrics.GapL, Is.EqualTo(compact ? 18f : 24f));
            Assert.That(UiMetrics.CompactMinH, Is.EqualTo(compact ? 20f : 22f));
            Assert.That(UiMetrics.PaletteMinH, Is.EqualTo(compact ? 24f : 26f));
            Assert.That(UiMetrics.ButtonH(-10f), Is.EqualTo(compact ? 26f : 30f));
            Assert.That(UiMetrics.ButtonH(50f), Is.EqualTo(compact ? 55f : 56f));
            var tiers = UiMetricValues.Resolve(compact ? "compact" : "default", 11f, 13f, 17f);
            Assert.That(tiers.LineH(GameFont.Tiny), Is.EqualTo(11f));
            Assert.That(tiers.LineH(GameFont.Small), Is.EqualTo(13f));
            Assert.That(tiers.LineH(GameFont.Medium), Is.EqualTo(17f));
            Assert.That(tiers.LineH((GameFont)999), Is.EqualTo(13f));
        }

        public static void UnsupportedTinyFontUsesSmallFontHeight()
        {
            using var scope = new MetricScope();
            Text.TinyFontSupported = false;
            UiFont.RevisionValue++;
            Time.frameCount++;
            Assert.That(UiMetrics.Current.TinyLine, Is.EqualTo(UiMetrics.Current.SmallLine));
            Text.TinyFontSupported = true;
            UiFont.RevisionValue++;
            Time.frameCount++;
            Assert.That(UiMetrics.Current.TinyLine, Is.EqualTo(Mathf.Ceil(Text.LineHeightOf(GameFont.Tiny))));
            Assert.That(UiMetrics.Current.MediumLine, Is.EqualTo(Mathf.Ceil(Text.LineHeightOf(GameFont.Medium))));
        }
    }
}
