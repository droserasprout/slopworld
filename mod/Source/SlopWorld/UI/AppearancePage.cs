using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The non-terminal UI font settings, as a page of the options menu. Font face and
    // size are set here; they apply to every Widgets.Label and Text.CalcSize call in the
    // game — the mod's own views, the patched chrome (AgentSidebar, InspectPane), and
    // any vanilla RimWorld dialog still on screen.
    //
    // A page rather than a Window because SlopOptions hangs it off an OptionCategoryDef.
    // See SlopOptions.
    public class AppearancePage
    {
        Vector2 _scroll;
        float _fieldsH;

        static SlopSettings S => SlopWorldMod.Instance.settings;

        public void Draw(Rect rect)
        {
            Text.Font = GameFont.Small;
            SlopWidgets.PageCaption(rect, "The mod's look — the font face and the size it is drawn at.");

            var body = SlopWidgets.PageBody(rect);
            body.height += SlopWidgets.BtnH + SlopWidgets.GapS;
            Widgets.DrawMenuSection(body);
            var inner = body.ContractedBy(SlopWidgets.GapM);

            // The preview sits at the foot; the form scrolls above it.
            float ph = Mathf.Clamp(SlopWidgets.LineH * 4 + 20f, 60f, 160f);
            var preview = new Rect(inner.x, inner.yMax - ph, inner.width, ph);
            var caption = new Rect(inner.x, preview.y - SlopWidgets.RowH - SlopWidgets.GapXS,
                inner.width, SlopWidgets.RowH);

            var form = new Rect(inner.x, inner.y, inner.width,
                caption.y - inner.y - SlopWidgets.GapS);
            var view = new Rect(0f, 0f, form.width - 18f, Mathf.Max(_fieldsH, form.height));
            Widgets.BeginScrollView(form, ref _scroll, view);

            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(0f, 0f, view.width, 4000f));

            // ---- font face
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH),
                    $"Font: {(S.uiFontName.NullOrEmpty() ? "Automatic" : S.uiFontName)}"))
            {
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption("Automatic (system default)", () =>
                    {
                        S.uiFontName = "";
                        SlopUIFont.Apply();
                    }),
                };
                foreach (var name in SlopUIFont.All)
                {
                    var picked = name;
                    opts.Add(new FloatMenuOption(picked, () =>
                    {
                        S.uiFontName = picked;
                        SlopUIFont.Apply();
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            l.Gap(SlopWidgets.GapS);
            l.Label("Size: " + (S.uiFontSize > 0 ? $"{S.uiFontSize}pt" : "Default (per tier)"));
            int size = Mathf.RoundToInt(l.Slider(S.uiFontSize, 0, 24));
            if (size != S.uiFontSize)
            {
                S.uiFontSize = size;
                SlopUIFont.Apply();
            }

            // A note about size 0 meaning "keep the built-in per-tier sizes".
            if (S.uiFontSize == 0)
            {
                l.Gap(SlopWidgets.GapXS);
                GUI.color = SlopWidgets.Faint;
                l.Label("At 0pt the original per-tier sizes are kept (Tiny=11, " +
                        "Small=13, Medium=15); only the face changes.");
                GUI.color = Color.white;
            }

            l.Gap(SlopWidgets.GapS);
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH), "Rescan installed fonts"))
            {
                SlopUIFont.Rescan();
            }

            _fieldsH = l.CurHeight + SlopWidgets.GapS;
            l.End();

            Widgets.EndScrollView();

            // ---- preview
            SlopWidgets.SectionHeading(caption, "Preview");
            DrawPreview(preview);
        }

        // A live preview of the current font at the current size, drawn in the mod's
        // own chrome colours so what is judged here is what arrives on the sidebar,
        // the top bar and the list views.
        static void DrawPreview(Rect r)
        {
            Widgets.DrawBoxSolid(r, SlopWidgets.Panel);

            float x = r.x + 8f, y = r.y + 6f;
            float w = r.width - 16f;

            Text.Font = GameFont.Small;
            GUI.color = SlopWidgets.Lead;
            Widgets.Label(new Rect(x, y, w, SlopWidgets.LineH), "Agents  ~/project  main  +12 -3");
            y += SlopWidgets.LineH;

            GUI.color = SlopWidgets.Name;
            Widgets.Label(new Rect(x, y, w, SlopWidgets.LineH),
                "This is how your agent list will read.");
            y += SlopWidgets.LineH;

            GUI.color = SlopWidgets.Dim;
            Widgets.Label(new Rect(x, y, w, SlopWidgets.LineH),
                "A second line in the rung below it.");
            y += SlopWidgets.LineH;

            GUI.color = SlopWidgets.Faint;
            Widgets.Label(new Rect(x, y, w, SlopWidgets.LineH),
                "A fine print note. Quick brown fox.");
            GUI.color = Color.white;
        }
    }
}
