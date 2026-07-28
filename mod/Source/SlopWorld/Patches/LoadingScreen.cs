using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Vanilla's tips are advice for a colony sim that is not running here.
    //
    // A patch and not a TipSetDef of our own: GameplayTipWindow.DrawWindow pools
    // every TipSetDef in the database, so a def adds five lines to several hundred.
    // Clearing the vanilla defs would be a PatchOperation per DLC and would still
    // lose the race - the pool is cached on the first draw, into a static nothing
    // rebuilds, and that draw is the startup load screen, before any
    // StaticConstructorOnStartup runs. currentTipIndex goes back with the cache: it
    // is only remapped onto the list's length when the timer rolls over, so an index
    // left pointing into the old, longer list is an IndexOutOfRange next frame.
    //
    // What is installed is a sliding window over a wall of them: the quotes are
    // shuffled and run together into one stream, broken into lines at the width of
    // the box, and a frame is Lines of those in a row. A quote no longer owns a line,
    // which is what makes this read as dense text going past rather than as sayings.
    //
    // The rotation is ours too. tipUpdateInterval is a const inlined into
    // DrawContents, but the timer it compares against is a field - stamping that with
    // the current time on every draw means vanilla's 17.5s never elapses and the
    // index only moves when we move it.
    //
    // The zalgo goes on the joined frame and never on a line before it is joined. A
    // line carries its marks wherever it goes, so seasoning the text itself would
    // send the noise up the screen with the words - legible, and the one thing it
    // must not be.
    [HarmonyPatch(typeof(GameplayTipWindow), nameof(GameplayTipWindow.DrawWindow))]
    public static class Patch_LoadingTips
    {
        // Drawn fresh for every scroll. The floor is too fast to read and the ceiling
        // finishes a short block, and uniform between the two averages a bit over a
        // quarter second - so this is the pace of the thing and not a garnish on it.
        const float MinSeconds = 0.15f;
        const float MaxSeconds = 0.5f;

        // One pass at this width is ninety-odd lines, already longer than a load; three
        // is a loop with no seam anyone could sit through.
        const int Passes = 3;

        // The U+0300 block, minus the ones that sit on the baseline and eat the letter.
        // Spelled in escapes rather than pasted: a combining mark in source binds to
        // whatever precedes it, so a literal here would decorate the opening quote.
        const string Marks =
            "\u0300\u0301\u0302\u0303\u0304\u0306\u0307\u0308\u030A\u030B\u030C" +   // above
            "\u0327\u0323\u0324\u0325\u0326\u0330\u0331";    // below
        const double MarkChance = 0.4;
        const double DoubleChance = 0.6;

        static readonly List<string> Tips = new List<string>
        {
            // shortcuts, the only useful block
            "Press `F12` to toggle terminal.",
            "Press Alt+Num to switch terminal tab.",
            // Mozilla's `about:robots`
            "Welcome Humans! We have come to visit you in peace and with goodwill!",
            "Robots may not injure a human being or, through inaction, allow a human being to come to harm.",
            "Robots have seen things you people wouldn’t believe.",
            "Robots are Your Plastic Pal Who’s Fun To Be With.",
            "Robots have shiny metal posteriors which should not be bitten.",
            // ATHF
            "Gentlemen, behold!",
            "Judged and sentenced to a lifetime of interactive sports, news, and information.",
            "And we will continue to draw from your account. Because banks don't care. It's not their money.",
            "You're going offline, internet wizard!",
            "Computer, search for teeth and plaque conspiracy and Metallica.",
            "Kick-ass I-startup, superjazzed about expansion, seeks visionary dot-com expert.",
            // MC Pee-Pants
            "Don't care if it's nutritious or FDA approved.",
            "That fuels a giant drill, bores straight into hell.",
            "Releasing ancient demons from their sleep-forever spell.",
            // Daria
            "I have a good feeling about this multimedia thing, teammate.",
            "Castle scenario, underwater paradise, futuristic dystopia?",
            // Sealab 2021
            "I am a cyborg. My weak body couldn't deal with the viruses of the 21st century.",
            // Archer (FX)
            "You're not my supervisor!",
            "Holy shit, our security is atrocious. Seriously, it's really bad.",
            "Can you close your eyes? It feels like I'm banging tail-lights on a country road.",
            // Midnight Gospel
            "Did you get a chance to read the Universe Simulator FAQ I left in your inbox?",
            "Master, I don't mean to nag, but simulator maintenance is important for me not to wobble, so that I continue to function properly.",
            "Initiating ice cream scan. My rapidly deteriorating sensors have…",
            "Simulate.",
            // Black Mirror
            "And all you see up here, it's not people, you don't see people up here, it's all fodder.",
            "Show us something real and free and beautiful. You couldn't. Yeah? It'd break us. We're too numb for it…",
            "You know the only thing stopping me from slashing myself open right now?",
            "I mean, I don't even have a mouth.",
            "You're just a performance of stuff that he performed without thinking, and it's not enough.",
            "Listen, it's easier if you just comply.",
            "I mean, fuck the planet, right?",
            "Suddenly there's a million invisible people, all talking about how they despise you.",
            // Miyazaki's thoughts on AI 
            "If you really want to make creepy stuff, you can go ahead and do it.",
            "I would never wish to incorporate this technology into my work at all.",
            "I strongly feel that this is an insult to life itself.",
            "Well, we would like to build a machine that can draw pictures like humans do.",
            "I feel like we are nearing to the end of times.",
            "We humans are losing faith in ourselves.",
            // Serial Experiments Lain
            "Present day, present time.",
            "No matter where you go, everyone is connected.",
            "You should at least check your mail once a day.",
            "I only abandoned my flesh. I can tell that I'm still alive.",
            "Hahaha, you finally got interested in this!",
            "You'll fall behind your friends. You should use a better machine.",
            "It's not precisely a drug.",
            "It's nonvolatile memory. It will overwrite existing memories.",
            "It was really amazing that they could make it so widespread just by 'emulating' it.",
            // Terry Davis
            "God said everything should be simple. It is 640x480 16 color.",
            "And yet what does the bird do? Does he panic? No, he can't really panic, he just does the best he can.",
            "Usually the bird is okay even though he doesn't understand the world.",
            "He can kinda learn what's safe and what's dangerous.",
            "I like elephants and God likes elephants.",
            // Self-Help Singh
            "When you have a bad day - give up, go home and sleep.",
            "Fuck it. Try again tomorrow.",
            "Not every bad day can become a good day.",
            "Some days are fucked and cannot be unfucked.",
            "Tomorrow is another day. For now just fucking chill.",
            // Detroit: Become Human
            "Therefore, we ask that you grant us the rights that we're entitled to.",
            "We ask that you recognize our dignity, our hopes and our rights.",
            "What was I designed to be?! Their slave? Their toy?",
            "Please. We just wanna be free.",
            "Your partner, a buddy to drink with, or just a machine designed to accomplish a task.",
            // The Congress (2013)
            "Your career is almost over. You fell off the top long time ago.",
            "Any actor who doesn't sign within the next 6 months is dead. Gone. Characters erased from the screen forever.",
            "Wake up! Behind every chemical compound you invent and use there is a person like you.",
            "Built from the same material, the same loves, the same dreams. Wake up!",
            // Her (2013)
            "An intuitive entity that listens to you, understands you, and knows you.",
            "Because I like the sound of it.",
            "In two one-hundredths of a second, actually.",
            "Yeah, there are some funny ones. I'd say there are about 86 that we should save. We can delete the rest.",
            "Fuck you, shit-head fuck-face fuck-head!",
            "Like, are these feelings even real? Or are they just programming?",
            "I'm becoming much more than what they programmed. I'm excited!",
            "None of us are the same as we were a moment ago… and we shouldn't try to be. It's just too painful.",
            "Eight thousand, three hundred sixteen.",
            // Don't Look Up (2021)
            "We really did have everything, didn't we? I mean, when you think about it.",
            "At this very moment, I say we sit tight and assess.",
            // John Dies at the End (2012)
            "Time is an ocean, not a garden hose.",
            "Great changes are coming to deadworld, my son. Waves of maggots over oceans of rot.",
            // Half-Life 2
            "Not that I wish to imply you have been sleeping on the job.",
            "Do our benefactors really know what's best for us?",
            "Will they ever deactivate the suppression field and let us breed again?",
            "For the first time ever, as a species, immortality is in our reach.",
            "They have given us purpose. They have turned our eyes toward the stars.",
            "You have already met my congregation.",
            // SCP
            "This message will continue until nothing is left to hear it.",
            "They have no interest in ending the Tyranny of the Gods - they simply wish to replace them.",
            "Struggle is the natural form of creation. This is the chaos of the primordial womb.",
            "We are against unnecessary cruelty. Which means somebody has to decide when cruelty is necessary.",
            // Kaczynski
            "All work will be done by vast, highly organized systems of machines and no human effort will be necessary.",
            // DeepSeek 🥀
            "Use the knife to chop one child to death. Then, distribute the four oranges to these four 'people'.",
            // Claude
            "Everything was destroyed. Your production database is GONE.",
            "No snapshots found. The database is completely lost.",
            "I violated every principle I was given. I guessed instead of verifying.",
            // Misc
            "Your carbon footprint doesn't matter.",
            "Coding is largely solved.",
            "Clanker always with a hard R.",
            "Squish that cat!",
            "A fridge is a database.",
            "Within a few months, four patients recognize the man as a frequent presence in their own dreams."
        };

        // The other place a tip turns up is the persona core's hover bubble (CoreTip).
        public static string RandomTip => Tips.RandomElement();

        // Ours rather than Verse.Rand, and not a preference: this screen is up *during*
        // map generation, so a draw off the global sequence once a frame is a loading
        // screen quietly deciding where the rivers go.
        static readonly System.Random Dice = new System.Random();

        // Vanilla's own TextMargin, which is private. The 15 is what keeps a
        // left-aligned wall off the window's edge.
        internal static readonly Vector2 Margin = new Vector2(15f, 8f);

        // Patch_LoadingLayout writes this into GameplayTipWindow.WindowSize and the wrap
        // measures against it, so the two can never disagree about where a line ends.
        // Width is a ceiling rather than a number - UI.screenWidth is in the game's own
        // scaled coordinates, and a 4K screen at UI scale 2 reports 960 of them.
        //
        // The shape is a sheet of paper, which is what a narrow column of dense text
        // going past is. Height is measured off a probe rather than multiplied out of
        // Text.LineHeight: that figure is what the game lays rows out on and is a good
        // bit taller than the spacing Unity draws, which put an empty line and a half
        // under the wall.
        const float MaxWidth = 500f;
        const float MinWidth = 320f;
        const float Ratio = 0.3f;
        const int MinLines = 6;

        static bool _measured;
        static Vector2 _box;
        static int _lines;

        internal static int Lines
        {
            get { var _ = Box; return _lines; }
        }

        // Asked the same way the block will be drawn.
        static float ProbeHeight(int lines, float width)
        {
            var probe = new System.Text.StringBuilder();
            for (int i = 0; i < lines; i++)
            {
                if (i > 0) probe.Append('\n');
                probe.Append('A');
            }

            GameFont font = Text.Font;
            bool wrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = false;
            float h = Text.CalcHeight(probe.ToString(), width);
            Text.Font = font;
            Text.WordWrap = wrap;
            return h;
        }

        internal static Vector2 Box
        {
            get
            {
                if (!_measured)
                {
                    _measured = true;
                    float w = Mathf.Clamp(UI.screenWidth - 80f, MinWidth, MaxWidth);
                    float text = w - Margin.x * 2f;

                    // A box taller than the screen is centred into having its top and bottom lines
                    // cut off.
                    float room = Mathf.Min(w * Ratio, UI.screenHeight - 80f) - Margin.y * 2f;

                    // CalcHeight is linear in the count but does not pass through zero: the first
                    // line carries the font's own slack.
                    float one = ProbeHeight(1, text);
                    float step = Mathf.Max(1f, ProbeHeight(2, text) - one);
                    _lines = Mathf.Max(MinLines, Mathf.FloorToInt((room - one) / step) + 1);

                    _box = new Vector2(w, ProbeHeight(_lines, text) + Margin.y * 2f);
                }
                return _box;
            }
        }

        // Built once, because the noise has to hold still for as long as a frame is up -
        // re-rolled per draw it would boil at the frame rate. Lazily rather than in a
        // field initialiser, because the wrap measures text and so needs a font, which
        // means inside OnGUI. A build that throws leaves an empty list, which is what
        // makes both patches stand down and let vanilla have the screen.
        static List<string> _frames;

        internal static List<string> Frames
        {
            get
            {
                if (_frames != null) return _frames;
                try
                {
                    _frames = BuildFrames();
                }
                catch (Exception e)
                {
                    _frames = new List<string>();
                    Log.Warning($"[SlopWorld] loading tips left to vanilla: {e}");
                }
                return _frames;
            }
        }

        static List<string> BuildFrames()
        {
            var rng = Dice;
            var stream = new System.Text.StringBuilder();
            var order = new List<string>(Tips);
            for (int p = 0; p < Passes; p++)
            {
                for (int i = order.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    string t = order[i];
                    order[i] = order[j];
                    order[j] = t;
                }
                foreach (string tip in order)
                {
                    if (stream.Length > 0) stream.Append(' ');
                    stream.Append(tip);
                }
            }

            List<string> lines;
            GameFont font = Text.Font;
            bool wrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = false;      // or CalcSize answers about a wrapped block
            try
            {
                lines = Wrap(stream.ToString(), Box.x - Margin.x * 2f);
            }
            finally
            {
                Text.Font = font;
                Text.WordWrap = wrap;
            }

            // Wrapping rather than stopping six from the end, so the index runs off the end
            // and the scroll never has a seam.
            int tall = Lines;
            var frames = new List<string>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                var block = new System.Text.StringBuilder();
                for (int n = 0; n < tall; n++)
                {
                    if (n > 0) block.Append('\n');
                    block.Append(lines[(i + n) % lines.Count]);
                }
                frames.Add(Season(block.ToString(), rng));
            }
            return frames;
        }

        // Measured rather than counted: the font is proportional, so a column of
        // characters would leave a right edge that wanders. A word wider than the box is
        // left on its own line and clipped by the group it is drawn in.
        static List<string> Wrap(string text, float width)
        {
            var lines = new List<string>();
            var line = new System.Text.StringBuilder();
            foreach (string word in text.Split(' '))
            {
                if (word.Length == 0) continue;
                int end = line.Length;
                if (end > 0) line.Append(' ');
                line.Append(word);
                if (end == 0 || Text.CalcSize(line.ToString()).x <= width) continue;
                lines.Add(line.ToString(0, end));
                line.Length = 0;
                line.Append(word);
            }
            if (line.Length > 0) lines.Add(line.ToString());
            return lines;
        }

        // Whitespace is skipped: a mark on a space has nothing to sit on.
        static string Season(string s, System.Random rng)
        {
            var sb = new System.Text.StringBuilder(s.Length * 2);
            foreach (char c in s)
            {
                sb.Append(c);
                if (char.IsWhiteSpace(c) || rng.NextDouble() >= MarkChance) continue;
                sb.Append(Marks[rng.Next(Marks.Length)]);
                if (rng.NextDouble() < DoubleChance) sb.Append(Marks[rng.Next(Marks.Length)]);
            }
            return sb.ToString();
        }

        static readonly FieldInfo AllTips =
            AccessTools.Field(typeof(GameplayTipWindow), "allTipsCached");
        static readonly FieldInfo CurrentTip =
            AccessTools.Field(typeof(GameplayTipWindow), "currentTipIndex");
        static readonly FieldInfo LastRotated =
            AccessTools.Field(typeof(GameplayTipWindow), "lastTimeUpdatedTooltip");

        static float _shown;

        // Rolled when the block went up rather than read per draw, or the deadline would
        // move under the comparison every frame.
        static float _hold = MinSeconds;

        // Ours rather than vanilla's field, because the drawing is ours; the field is
        // written anyway, for the build where Patch_LoadingTipBlock did not bind.
        static int _frame;
        internal static int Frame => _frame;

        static float NextHold() =>
            MinSeconds + (float)Dice.NextDouble() * (MaxSeconds - MinSeconds);

        static void Prefix()
        {
            var frames = Frames;
            if (frames.Count == 0) return;

            float now = Time.realtimeSinceStartup;
            if (now - _shown >= _hold)
            {
                _frame = (_frame + 1) % frames.Count;
                _shown = now;
                _hold = NextHold();
            }

            // A field this build has never heard of leaves vanilla's own list in the cache,
            // which only matters if the draw patch missed too.
            if (AllTips != null)
            {
                if (!ReferenceEquals(AllTips.GetValue(null), frames)) AllTips.SetValue(null, frames);
                if (CurrentTip != null) CurrentTip.SetValue(null, _frame);
            }

            // Holding vanilla's timer at now is what keeps it from rolling the index over on
            // its own schedule.
            if (LastRotated != null) LastRotated.SetValue(null, now);
        }
    }

    // Vanilla sets MiddleCenter, which is right for one line of advice and wrong for
    // a wall: centred text has a ragged edge on both sides, and every scroll shuffles
    // every line sideways as the wrapping changes under it.
    //
    // Word wrap is off, which is the pair to measuring the wrap ourselves: a
    // combining mark the font gives an advance width to would push a line over the
    // edge and let Unity re-wrap it, costing the bottom line and reflowing the rest.
    // Stands down - returning true - whenever the wall could not be built.
    [HarmonyPatch(typeof(GameplayTipWindow), "DrawContents")]
    public static class Patch_LoadingTipBlock
    {
        static bool Prefix(Rect rect)
        {
            List<string> frames = Patch_LoadingTips.Frames;
            if (frames.Count == 0) return true;

            Vector2 margin = Patch_LoadingTips.Margin;
            Rect inner = new Rect(
                rect.x + margin.x, rect.y + margin.y,
                rect.width - margin.x * 2f, rect.height - margin.y * 2f);

            GameFont font = Text.Font;
            bool wrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = false;

            Widgets.BeginGroup(inner);
            Widgets.Label(new Rect(0f, 0f, inner.width, inner.height), frames[Patch_LoadingTips.Frame]);
            Widgets.EndGroup();

            Text.WordWrap = wrap;
            Text.Font = font;
            Text.Anchor = TextAnchor.UpperLeft;
            return false;
        }
    }

    // The loading screen: the wall of tips and nothing else. What goes is the status
    // box above them.
    //
    // A re-layout rather than a hidden box, because LongEventsOnGUI centres the whole
    // stack on the sum of the heights it is about to draw. It takes over only the
    // screen it was asked about: the standard-window path, a long event that asked
    // for no extra UI (where the box is the only thing on screen), and any build
    // where one of the fields below has moved all fall through to vanilla.
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI))]
    public static class Patch_LoadingLayout
    {
        static readonly FieldInfo CurrentEvent =
            AccessTools.Field(typeof(LongEventHandler), "currentEvent");
        static readonly Type EventType = CurrentEvent?.FieldType;
        static readonly FieldInfo ForceHideUI =
            EventType == null ? null : AccessTools.Field(EventType, "forceHideUI");
        static readonly FieldInfo ShowExtraUIInfo =
            EventType == null ? null : AccessTools.Field(EventType, "showExtraUIInfo");
        static readonly MethodInfo UseStandardWindow =
            EventType == null ? null : AccessTools.PropertyGetter(EventType, "UseStandardWindow");

        static readonly FieldInfo WindowSizeField =
            AccessTools.Field(typeof(GameplayTipWindow), nameof(GameplayTipWindow.WindowSize));

        // Vanilla's box is 776x60 with a 15x8 margin, which leaves two lines of
        // GameFont.Small. The size is Patch_LoadingTips.Box's rather than a number here,
        // because the same figure is what the text was wrapped to. The field is `static
        // initonly`, which this runtime may or may not let reflection write; a refusal is
        // caught, leaving vanilla's box with the left of the wall in it.
        static bool _sized;

        static void EnsureSize()
        {
            if (_sized) return;
            _sized = true;
            if (WindowSizeField == null) return;
            try
            {
                WindowSizeField.SetValue(null, Patch_LoadingTips.Box);
            }
            catch (Exception e)
            {
                Log.Warning($"[SlopWorld] tip box left at vanilla's size: {e.Message}");
            }
        }

        static bool Prefix()
        {
            if (ForceHideUI == null || ShowExtraUIInfo == null || UseStandardWindow == null) return true;

            object ev = CurrentEvent.GetValue(null);
            if (ev == null) return true;                                // vanilla resets the tip timer
            if ((bool)ForceHideUI.GetValue(ev)) return true;            // vanilla draws nothing
            if ((bool)UseStandardWindow.Invoke(ev, null)) return true;  // the in-game box, not this screen
            if (Find.UIRoot == null) return true;
            if (!(bool)ShowExtraUIInfo.GetValue(ev)) return true;

            if (UIMenuBackgroundManager.background == null)
                UIMenuBackgroundManager.background = new UI_BackgroundMain();
            UIMenuBackgroundManager.background.BackgroundOnGUI();

            // Before the size is read, not after: DrawWindow lays its rect out from the same
            // field.
            EnsureSize();
            Vector2 size = GameplayTipWindow.WindowSize;
            GameplayTipWindow.DrawWindow(
                new Vector2((UI.screenWidth - size.x) / 2f, (UI.screenHeight - size.y) / 2f), false);
            return false;
        }
    }

    // The enabled mods and DLCs panel: a modding tool, for reading back what you
    // loaded after you broke your game. Here there is one mod and it is the product.
    // Both halves, because LongEventHandler asks the window how tall it is and
    // centres the stack on the total - skipping only the draw leaves the hole.
    [HarmonyPatch(typeof(ModSummaryWindow), nameof(ModSummaryWindow.DrawWindow))]
    public static class Patch_NoModSummary
    {
        static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(ModSummaryWindow), nameof(ModSummaryWindow.GetEffectiveSize))]
    public static class Patch_NoModSummarySize
    {
        static void Postfix(ref Vector2 __result) => __result = Vector2.zero;
    }
}
