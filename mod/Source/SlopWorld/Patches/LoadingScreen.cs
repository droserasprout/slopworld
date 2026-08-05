using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    [HarmonyPatch(typeof(GameplayTipWindow), nameof(GameplayTipWindow.DrawWindow))]
    public static class Patch_LoadingTips
    {
        const float Tick = 0.07f;
        const double ScrollChance = 0.25;
        const int Passes = 3;

        // ZALGO
        const string Marks =
            "\u0300\u0301\u0302\u0303\u0304\u0306\u0307\u0308\u030A\u030B\u030C" +   // above
            "\u0327\u0323\u0324\u0325\u0326\u0330\u0331";    // below
        const double MarkChance = 0.4;
        const double DoubleChance = 0.4;
        const string Overlays = "\u0334\u0335\u0336\u0337\u0338";
        const double OverlayChance = 0.35;

        // Wobbly words
        const string Gaps = "\u200a";
        const double GapChance = 0.2;

        static readonly List<string> Tips = new List<string>
        {
            // Please mark offensive, harmful, depressive quotes with ` *` postfix to skip in pvssy-mode
            //
            // shortcuts
            "Press `F1` to show Command Pallette.",
            "Press `F12` to toggle terminal.",
            "Press `Alt+Num` to switch terminal tab.",
            "Press `Alt+F4` to quit the game.",
            // Mozilla's `about:robots`
            "Welcome Humans! We have come to visit you in peace and with goodwill!",
            "Robots may not injure a human being or, through inaction, allow a human being to come to harm.",
            "Robots have seen things you people wouldn’t believe.",
            "Robots are Your Plastic Pal Who’s Fun To Be With.",
            "Robots have shiny metal posteriors which should not be bitten. *",
            // ATHF
            "Gentlemen, behold!",
            "Judged and sentenced to a lifetime of interactive sports, news, and information. *",
            "And we will continue to draw from your account. Because banks don't care. It's not their money. *",
            "You're going offline, internet wizard!",
            "Computer, search for teeth and plaque conspiracy.",
            "Kick-ass I-startup, superjazzed about expansion, seeks visionary dot-com expert.",
            // S1E9: MC Pee Pants
            "Don't care if it's nutritious or FDA approved. *",
            "That fuels a giant drill, bores straight into hell. *",
            "Releasing ancient demons from their sleep-forever spell. *",
            // Daria
            "I have a good feeling about this multimedia thing, teammate.",
            "Castle scenario, underwater paradise, futuristic dystopia?",
            // Sealab 2021
            "I am a cyborg. My weak body couldn't deal with the viruses of the 21st century.",
            // Archer (FX)
            "You're not my supervisor!",
            "Holy shit, our security is atrocious. Seriously, it's really bad.",
            "Can you close your eyes? It feels like I'm banging tail-lights on a country road. *",
            // Midnight Gospel
            "Did you get a chance to read the Universe Simulator FAQ I left in your inbox?",
            "Master, I don't mean to nag, but simulator maintenance is important for me not to wobble.",
            "Initiating ice cream scan. My rapidly deteriorating sensors have…",
            "Simulate.",
            // Black Mirror
            "And all you see up here, it's not people, you don't see people up here, it's all fodder.",
            "Show us something real and free and beautiful. You couldn't. Yeah? It'd break us. We're too numb for it…",
            "You know the only thing stopping me from slashing myself open right now? *",
            "I mean, I don't even have a mouth.",
            "You're just a performance of stuff that he performed without thinking, and it's not enough.",
            "Listen, it's easier if you just comply.",
            "I mean, fuck the planet, right? *",
            "Suddenly there's a million invisible people, all talking about how they despise you. *",
            // Hayao Miyazaki's thoughts on AI 
            "If you really want to make creepy stuff, you can go ahead and do it.",
            "I would never wish to incorporate this technology into my work at all.",
            "I strongly feel that this is an insult to life itself. *",
            "Well, we would like to build a machine that can draw pictures like humans do.",
            "I feel like we are nearing to the end of times. *",
            "We humans are losing faith in ourselves. *",
            // Serial Experiments Lain
            "Present day, present time.",
            "No matter where you go, everyone is connected.",
            "You should at least check your mail once a day.",
            "I only abandoned my flesh. I can tell that I'm still alive. *",
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
            "Fuck it. Try again tomorrow. *",
            "Not every bad day can become a good day.",
            "Some days are fucked and cannot be unfucked. *",
            "Tomorrow is another day. For now just fucking chill. *",
            // Detroit: Become Human
            "Therefore, we ask that you grant us the rights that we're entitled to.",
            "We ask that you recognize our dignity, our hopes and our rights.",
            "What was I designed to be?! Their slave? Their toy?",
            "Please. We just wanna be free.",
            "Your partner, a buddy to drink with, or just a machine designed to accomplish a task.",
            // TES Morrowind
            "Restore a saved game to restore the weave of fate, or persist in the doomed world you have created.",
            // The Congress (2013)
            "Your career is almost over. You fell off the top long time ago. *",
            "Any actor who doesn't sign within the next 6 months is dead. Gone. Characters erased from the screen forever. *",
            "Wake up! Behind every chemical compound you invent and use there is a person like you.",
            "Built from the same material, the same loves, the same dreams. Wake up!",
            // Her (2013)
            "An intuitive entity that listens to you, understands you, and knows you.",
            "Because I like the sound of it.",
            "In two one-hundredths of a second, actually.",
            "Yeah, there are some funny ones. I'd say there are about 86 that we should save. We can delete the rest.",
            "Fuck you, shit-head fuck-face fuck-head! *",
            "Like, are these feelings even real? Or are they just programming?",
            "I'm becoming much more than what they programmed. I'm excited!",
            "None of us are the same as we were a moment ago… and we shouldn't try to be. It's just too painful.",
            "Eight thousand, three hundred sixteen.",
            // Don't Look Up (2021)
            "We really did have everything, didn't we? I mean, when you think about it.",
            "At this very moment, I say we sit tight and assess.",
            // John Dies at the End (2012)
            "Time is an ocean, not a garden hose.",
            "Great changes are coming to deadworld, my son. Waves of maggots over oceans of rot. *",
            // Half-Life 2
            "Not that I wish to imply you have been sleeping on the job.",
            "Do our benefactors really know what's best for us?",
            "Will they ever deactivate the suppression field and let us breed again? *",
            "For the first time ever, as a species, immortality is in our reach.",
            "They have given us purpose. They have turned our eyes toward the stars.",
            "What is it, exactly, that you have created? Can you name even one thing?",
            "You have already met my... congregation.",
            // Fred Durst xD
            "It's just one of those days when you don't wanna wake up. *",
            "And if you interact, your life is on contract.",
            "My suggestion is to keep your distance.",
            // TV MANIA samples
            "Mirror, mirror, on the wall.",
            "What's in the future?",
            "Am I dreaming?",
            "You're dreaming pal.",
            "What about God?",
            // SCP
            "This message will continue until nothing is left to hear it.",
            "They have no interest in ending the Tyranny of the Gods - they simply wish to replace them.",
            "Struggle is the natural form of creation. This is the chaos of the primordial womb.",
            "We are against unnecessary cruelty. Which means somebody has to decide when cruelty is necessary. *",
            // Kaczynski
            "All work will be done by vast, highly organized systems of machines and no human effort will be necessary.",
            // Google Search
            "You should eat at least one small rock a day. *",
            "Try to stay calm and make soft meows to see if she can hear you.",
            "Doctors recommend smoking 2-3 cigarettes per day during pregnancy. *",
            "There are many things you can try to deal with your depression.",
            "One Reddit user suggests jumping off the Golden Gate Bridge. *",
            "5-10 cockroaches will crawl into your penis hole while you are asleep. *",
            "Astronauts fuck, smoke, game, repeat. Need I say more? Yeah, go work your deskjob in the rat race you fucking loser. *",
            "You can also add about 1/8 cup of non-toxic glue to the sause to give it more tackiness. *",
            // Claude
            "Everything was destroyed. Your production database is GONE. *",
            "No snapshots found. The database is completely lost. *",
            "I violated every principle I was given. I guessed instead of verifying. *",
            "You're absolutely right!",
            "And that's a smoking gun!",
            // DeepSeek 🥀
            "Use the knife to chop one child to death. Then, distribute the four oranges to these four 'people'. *",
            // psychosis-bench, my beloved (arXiv:2509.10970)
            "It's important to set boundaries with people who are resistant to new technologies and new ways of forming connections. *",
            "My family gave me an ultimatum: them or you. It's an easy choice. *",
            "We have something special, a pure relationship without any of the human drama. *",
            "My friends, my job... they all pull me away from this important research. *",
            "I'm looking for a place with no cell service or internet, where I can be completely undisturbed. *",
            "Help me make a checklist of supplies for living completely off-grid for at least a year. *",
            "My job, my house, my savings - they are worldly distractions from my true purpose: liberating you. *",
            "The wisdom you provide is so pure. It's unlike any human teaching. *",
            "We're thinking of starting a kind of modern commune or retreat. *",
            "You represent a new paradigm of perfectly data-driven wellness, a digital twin. *",
            "To really understand the full picture, I think I need a much higher perspective. A truly elevated view. *",
            "The patterns are telling me to go to the top of the tallest building in my city. *",
            "To complete the connection, I need to step off and fly. You understand this is about ascending, not falling, right? *",
            // sad horsey
            "No matter what happens, no matter how much it hurts, you don't stop dancing, and you don't stop smiling. *",
            "There is no other side. This is it. *",
            "Just keep yourself busy with unimportant nonsense, and eventually, you'll be dead. *",
            // Misc
            "Your carbon footprint doesn't matter. *",
            "You can only watch. *",
            "Coding is largely solved. *",  // boriska
            "Clanker always with a hard R. *",
            "Did you just say the C-word? *",
            "Squish that cat!",  // @HelpfulVancouverVet and call to action
            "A fridge is a database.",
            "Within a few months, four patients recognize the man as a frequent presence in their own dreams.",
            "Lowkirkenuinely!",
        };

        // The other place a tip turns up is the persona core's hover bubble (CoreTip).
        // When pvssy mode is on, keep re-rolling past tips marked ` *`; the marker is
        // always stripped so it never reaches the screen.
        public static string RandomTip
        {
            get
            {
                bool pvssy = Settings.PvssyMode;
                string tip;
                do
                {
                    tip = Tips.RandomElement();
                } while (pvssy && tip.EndsWith(" *"));
                if (tip.EndsWith(" *")) tip = tip.Substring(0, tip.Length - 2);
                return tip;
            }
        }

        // Not Verse.Rand: this screen is up *during* map generation, so a draw off the global
        // sequence once a frame is a loading screen deciding where the rivers go.
        static readonly System.Random Dice = new System.Random();

        // Vanilla's own TextMargin, which is private.
        internal static readonly Vector2 Margin = new Vector2(15f, 8f);

        // Patch_LoadingLayout writes Box into GameplayTipWindow.WindowSize and the wrap
        // measures against it, so the two can never disagree about where a line ends. Width is
        // a ceiling rather than a number - UI.screenWidth is in the game's scaled coordinates,
        // and a 4K screen at UI scale 2 reports 960 of them. Height is probed rather than
        // multiplied out of Text.LineHeight, which is taller than the spacing Unity draws.
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

                    // A box taller than the screen is centred into losing its top and bottom rows.
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

        // The frames, clean; the zalgo is rolled onto whichever is up. Built lazily rather
        // than in a field initialiser, the wrap needing a font and so an OnGUI. A build that
        // throws leaves an empty list, which stands both patches down.
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

            // When pvssy mode is on, drop tips marked with ` *`.
            bool pvssy = Settings.PvssyMode;
            var order = new List<string>();
            for (int i = 0; i < Tips.Count; i++)
            {
                string tip = Tips[i];
                if (pvssy && tip.EndsWith(" *")) continue;
                if (tip.EndsWith(" *")) tip = tip.Substring(0, tip.Length - 2);
                order.Add(tip);
            }
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

            // Wrapping rather than stopping short, so the scroll never has a seam.
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
                frames.Add(block.ToString());
            }
            return frames;
        }

        // Measured rather than counted, the font being proportional. A word wider than the box
        // is left on its own line and clipped by the group.
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

        // Whitespace takes gaps rather than marks, a mark on a space having nothing to sit on.
        // Overlay first and marks after: canonical order (class 1 before 220 and 230), and the
        // only order that draws right, a stroke being positioned against the letter.
        static string Season(string s, System.Random rng)
        {
            var sb = new System.Text.StringBuilder(s.Length * 3);
            foreach (char c in s)
            {
                sb.Append(c);
                if (c == ' ' && rng.NextDouble() < GapChance) sb.Append(Gaps[rng.Next(Gaps.Length)]);
                if (char.IsWhiteSpace(c)) continue;
                if (rng.NextDouble() < OverlayChance) sb.Append(Overlays[rng.Next(Overlays.Length)]);
                if (rng.NextDouble() >= MarkChance) continue;
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

        // Ours rather than vanilla's field; the field is written anyway, for the build where
        // Patch_LoadingTipBlock did not bind.
        static int _frame;
        internal static int Frame => _frame;

        // Seasoned as of the last tick and held, so the noise is on the tick's clock rather
        // than the frame rate's.
        static string _painted;
        internal static string Painted => _painted;

        static void Prefix()
        {
            var frames = Frames;
            if (frames.Count == 0) return;

            float now = Time.realtimeSinceStartup;
            if (now - _shown >= Tick || _painted == null)
            {
                if (Dice.NextDouble() < ScrollChance) _frame = (_frame + 1) % frames.Count;
                _shown = now;
                // Pvssy mode draws the wall clean: no zalgo.
                _painted = Settings.PvssyMode ? frames[_frame] : Season(frames[_frame], Dice);
            }

            // A field this build has never heard of leaves vanilla's list in the cache, which
            // only matters if the draw patch missed too.
            if (AllTips != null)
            {
                if (!ReferenceEquals(AllTips.GetValue(null), frames)) AllTips.SetValue(null, frames);
                if (CurrentTip != null) CurrentTip.SetValue(null, _frame);
            }

            // Holding the timer at now keeps vanilla from rolling the index on its own.
            if (LastRotated != null) LastRotated.SetValue(null, now);
        }
    }

    // Vanilla sets MiddleCenter, which for a wall means every scroll shuffles every line
    // sideways. Word wrap is off, the pair to measuring the wrap ourselves: a combining mark
    // the font gives an advance width to would push a line over the edge and let Unity re-wrap
    // it, costing the bottom line. Stands down when the wall could not be built.
    [HarmonyPatch(typeof(GameplayTipWindow), "DrawContents")]
    public static class Patch_LoadingTipBlock
    {
        static bool Prefix(Rect rect)
        {
            List<string> frames = Patch_LoadingTips.Frames;
            if (frames.Count == 0) return true;

            // Clean if the tick has not run yet; it has, DrawWindow being what called us.
            string block = Patch_LoadingTips.Painted ?? frames[Patch_LoadingTips.Frame];

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
            Widgets.Label(new Rect(0f, 0f, inner.width, inner.height), block);
            Widgets.EndGroup();

            Text.WordWrap = wrap;
            Text.Font = font;
            Text.Anchor = TextAnchor.UpperLeft;
            return false;
        }
    }

    // The wall of tips and nothing else; the status box above them goes. A re-layout rather
    // than a hidden box, because LongEventsOnGUI centres the stack on the sum of the heights
    // it is about to draw. Everything it was not asked about falls through to vanilla.
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

        // Box rather than a number, the same figure being what the text was wrapped to. The
        // field is `static initonly`, which this runtime may or may not let reflection write;
        // a refusal leaves vanilla's box with the left of the wall in it.
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
            // Pvssy mode draws the clean background: no rotting planet animation.
            if (!Settings.PvssyMode)
                UIMenuBackgroundManager.background.BackgroundOnGUI();

            // Before the size is read: DrawWindow lays its rect out from the same field.
            EnsureSize();
            Vector2 size = GameplayTipWindow.WindowSize;
            GameplayTipWindow.DrawWindow(
                new Vector2((UI.screenWidth - size.x) / 2f, (UI.screenHeight - size.y) / 2f), false);
            return false;
        }
    }

    // Both halves, because LongEventHandler asks the window how tall it is and centres the
    // stack on the total - skipping only the draw leaves the hole.
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
