using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Owns the About logo trigger, robot greeting, and retry joke. AboutPage owns scrolling.
    internal sealed class AboutRobots
    {
        const GameFont RegularFont = GameFont.Medium;
        static float TailPadding => UiTheme.GapL;
        const string RobotTexturePath = "SlopWorld/Marks/08";
        const float RobotImageSize = 88f;
        const float HeaderTextOffset = 120f;
        const float IntroGap = 48f;
        const float BulletIndent = 30f;
        const float BulletGap = 10f;
        const float OutroGap = 18f;
        const int TitleTextSize = 56;
        const int BodyTextSize = 26;

        // Easter egg: triple-click the logo for a greeting from our robot visitors.
        const string Title = "Welcome Humans!";
        const string Intro = "We have come to visit you in peace and with goodwill!";
        static readonly string[] Bullets =
        {
            "Robots may not injure a human being or, through inaction, allow a human being to come to harm.",
            "Robots have seen things you people wouldn’t believe.",
            "Robots are Your Plastic Pal Who’s Fun To Be With.",
            "Robots have shiny metal posteriors which should not be bitten.",
        };
        const string Outro = "And they have a plan.";

        const string RetryLabel = "Try Again";

        const string RetryWarning = "Please don't press this button again.";

        readonly MouseClickSequence _logoClicks = new MouseClickSequence();
        bool _retryWarningShown;
        Texture2D _robotTexture;

        public bool Visible { get; private set; }

        public float Draw(Rect r, float viewportHeight)
        {
            // Center against the viewport, never the previous scroll-content height.
            float scale = Mathf.Clamp(r.width / 1320f, 0.6f, 1f);
            float width = Mathf.Min(r.width, 1320f);
            r = new Rect(r.center.x - width / 2f, r.y, width, r.height);
            float offset = r.width < 480f ? 0f : HeaderTextOffset * scale;
            var body = new Rect(r.x + offset, r.y, Mathf.Max(1f, r.width - offset), r.height);
            var title = SizedStyle(RegularFont, Mathf.RoundToInt(TitleTextSize * scale),
                TextAnchor.MiddleLeft, true);
            title.fontStyle = FontStyle.Bold;
            var text = SizedStyle(RegularFont, Mathf.RoundToInt(BodyTextSize * scale),
                TextAnchor.UpperLeft, true);
            var outro = SizedStyle(RegularFont, Mathf.RoundToInt(22f * scale),
                TextAnchor.UpperLeft, true);
            float iconSize = RobotImageSize * scale;
            float headerHeight = Mathf.Max(iconSize,
                title.CalcHeight(new GUIContent(Title), body.width));
            float introHeight = text.CalcHeight(new GUIContent(Intro), body.width);
            float indent = BulletIndent * scale;
            float bulletWidth = Mathf.Max(1f, body.width - indent * 1.6f);
            float total = headerHeight + IntroGap * scale + introHeight + 26f * scale;
            foreach (string bullet in Bullets)
                total += text.CalcHeight(new GUIContent(bullet), bulletWidth) + BulletGap * scale;
            float outroHeight = outro.CalcHeight(new GUIContent(Outro), body.width);
            float buttonHeight = Mathf.Max(54f * scale,
                text.CalcHeight(new GUIContent(RetryWarning), body.width) + 16f * scale);
            // Reserve the same button height for both labels to avoid shifting the page.
            total += OutroGap * scale + outroHeight + 32f * scale + buttonHeight;
            if (offset == 0f) total += iconSize + 16f * scale;
            float y = r.y + Mathf.Max(0f, (viewportHeight - total) / 2f);
            var wasColor = GUI.color;
            GUI.color = Color.white;
            try
            {
                if (RobotTexture != null)
                    GUI.DrawTexture(new Rect(r.x, y, iconSize, iconSize), RobotTexture,
                        ScaleMode.ScaleToFit, true);
                if (offset == 0f) y += iconSize + 16f * scale;
                GUI.Label(new Rect(body.x, y, body.width, headerHeight), Title, title);
                y += headerHeight + IntroGap * scale;
                GUI.Label(new Rect(body.x, y, body.width, introHeight), Intro, text);
                y += introHeight + 26f * scale;
                foreach (string bullet in Bullets)
                {
                    float height = text.CalcHeight(new GUIContent(bullet), bulletWidth);
                    GUI.Label(new Rect(body.x + indent * 0.6f, y, indent, height), "•", text);
                    GUI.Label(new Rect(body.x + indent * 1.6f, y, bulletWidth, height), bullet, text);
                    y += height + BulletGap * scale;
                }
                y += OutroGap * scale;
                GUI.Label(new Rect(body.x, y, body.width, outroHeight), Outro, outro);
                y += outroHeight + 32f * scale;
                string label = !_retryWarningShown ? RetryLabel : RetryWarning;
                float buttonWidth = Mathf.Min(body.width,
                    Mathf.Max(190f * scale, text.CalcSize(new GUIContent(label)).x + 48f * scale));
                var button = new Rect(body.x, y, buttonWidth, buttonHeight);
                if (UiButtons.Button(button, ""))
                {
                    if (!_retryWarningShown) _retryWarningShown = true;
                    else
                    {
                        Visible = false;
                        _retryWarningShown = false;
                    }
                }
                text.alignment = TextAnchor.MiddleCenter;
                GUI.Label(button, label, text);
                return y + buttonHeight + TailPadding;
            }
            finally
            {
                GUI.color = wasColor;
            }
        }

        public bool HandleLogoClick(Rect rect)
        {
            var e = Event.current;
            if (e == null || UiEvent.RawType(e) != EventType.MouseDown) return false;

            if (e.button != 0 || !Mouse.IsOver(rect))
            {
                _logoClicks.Reset();
                return false;
            }

            int clickCount = _logoClicks.Observe(e, Time.realtimeSinceStartup);
            e.Use();
            if (clickCount < 3) return false;

            _logoClicks.Reset();
            Visible = true;
            _retryWarningShown = false;
            return true;
        }

        Texture2D RobotTexture
        {
            get
            {
                if (_robotTexture == null)
                {
                    _robotTexture = ContentFinder<Texture2D>.Get(RobotTexturePath, false);
                    if (_robotTexture != null)
                        _robotTexture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                return _robotTexture;
            }
        }

        static GUIStyle SizedStyle(GameFont font, int textSize, TextAnchor anchor, bool wrap)
        {
            var wasFont = Text.Font;
            Text.Font = font;
            var style = new GUIStyle(Text.CurFontStyle)
            {
                alignment = anchor,
                clipping = TextClipping.Overflow,
                fontSize = textSize > 0 ? textSize : Text.CurFontStyle.fontSize,
                wordWrap = wrap,
            };
            Text.Font = wasFont;
            return style;
        }
    }
}
