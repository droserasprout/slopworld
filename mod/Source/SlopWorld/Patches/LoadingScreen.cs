using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The loading screen's tips, replaced. Vanilla's are advice for a colony sim
    /// that is not running here - how to butcher, when to build a freezer.
    ///
    /// Replacing rather than adding, which is why this is a patch and not a
    /// TipSetDef of our own: GameplayTipWindow.DrawWindow pools every TipSetDef in
    /// the database, so a def only ever puts five lines in with the several hundred
    /// that were already there. Clearing the vanilla defs instead would mean a
    /// PatchOperation per DLC and a race besides - the pool is cached on the first
    /// draw, into a static that is never rebuilt, and that first draw is the
    /// startup load screen, which is up before any StaticConstructorOnStartup
    /// runs. Writing the cache is the one move that lands whenever it happens to
    /// be built.
    ///
    /// currentTipIndex goes back with it: it is only remapped onto the list's
    /// length when the 17.5s timer rolls it over, so a cache swapped out from
    /// under an index pointing into the old, longer list is an IndexOutOfRange
    /// on the next frame.
    ///
    /// What is installed is not the tips but a sliding window over them, three at
    /// a time - 1-2-3, 2-3-4, 3-4-5 - so stepping the index one place reads as the
    /// block scrolling up a line rather than as one tip being swapped for another.
    /// The quotes are shuffled once per launch, so the same load screen is never
    /// the same twice and nothing sits at the top by right.
    ///
    /// The rotation is ours too. Vanilla's tipUpdateInterval is a const inlined
    /// into DrawContents, so there is no field to write - but the timer it
    /// compares against is a field, and stamping that with the current time on
    /// every draw means vanilla's own 17.5s never elapses and the index only ever
    /// moves when we move it. Which is what buys the variable hold: every scroll
    /// draws its own delay between <see cref="MinSeconds"/> and
    /// <see cref="MaxSeconds"/>, so the block sometimes flicks past and sometimes
    /// sits there, and nothing about the rhythm is countable. A fixed catch every
    /// tenth was the first cut of this and read as a metronome, which is the one
    /// thing a machine coming apart should not sound like.
    ///
    /// The zalgo goes on the joined block and never on a line before it is joined.
    /// A line carries its marks wherever it goes, so seasoning the tips themselves
    /// would send the noise up the screen with the text - legible, and the one
    /// thing it must not be. Seasoning the block instead re-rolls every line's
    /// marks on every scroll, so the noise sits still and crawls while the words
    /// move through it.
    /// </summary>
    [HarmonyPatch(typeof(GameplayTipWindow), nameof(GameplayTipWindow.DrawWindow))]
    public static class Patch_LoadingTips
    {
        /// What one scroll is allowed to cost, drawn fresh for every one of them.
        /// The floor is too fast to read anything at all and the ceiling is long
        /// enough to finish a short block, so a load screen is a wall of this going
        /// past that keeps stalling on something. Uniform between the two, which
        /// averages a bit over a quarter of a second a block - so this is the pace
        /// of the whole thing and not a garnish on it.
        const float MinSeconds = 0.05f;
        const float MaxSeconds = 0.5f;

        /// How many lines the block is. Three is what the box is sized for; see
        /// <see cref="Patch_LoadingLayout"/>, which has to grow it to fit them.
        const int Lines = 3;

        /// Combining marks, above and below - the U+0300 block, minus the ones that
        /// sit on the baseline and eat the letter. Density is low on purpose: this
        /// is meant to read as a picture that is going wrong, and a solid hedge of
        /// diacritics reads as a font that has failed.
        /// Spelled in escapes rather than pasted: a combining mark in source
        /// binds to whatever precedes it, so a literal here would decorate the
        /// opening quote and could not be read back or edited.
        const string Marks =
            "\u0300\u0301\u0302\u0303\u0304\u0306\u0307\u0308\u030A\u030B\u030C" +   // above
            "\u0327\u0323\u0324\u0325\u0326\u0330\u0331";    // below
        const double MarkChance = 0.18;
        const double DoubleChance = 0.25;

        static readonly List<string> Tips = new List<string>
        {
            // shortcuts, the only useful block - shuffled in with the rest now, so
            // they turn up when they turn up
            "Press `F12` to toggle terminal",
            "Press Alt+Num to switch terminal tab",
            // Mozilla's `about:robots`
            "Welcome Humans! We have come to visit you in peace and with goodwill!",
            "Robots may not injure a human being or, through inaction, allow a human being to come to harm",
            "Robots have seen things you people wouldn’t believe",
            "Robots are Your Plastic Pal Who’s Fun To Be With",
            "Robots have shiny metal posteriors which should not be bitten",
            // ATHF
            "Gentlemen, behold!",
            "Judged and sentenced to a lifetime of interactive sports, news, and information",
            "And we will continue to draw from your account. Because banks don't care. It's not their money.",
            "You're going offline, internet wizard!",
            "Computer, search for teeth and plaque conspiracy and Metallica",
            "Kick-ass I-startup, superjazzed about expansion, seeks visionary dot-com expert",
            // MC Pee-Pants
            "Don't care if it's nutritious or FDA approved",
            "That fuels a giant drill, bores straight into hell",
            "Releasing ancient demons from their sleep-forever spell",
            // other [adult swim]
            "I have a good feeling about this multimedia thing, teammate",
            "Castle scenario, underwater paradise, futuristic dystopia?",
            "I am a cyborg. My weak body couldn't deal with the viruses of the 21st century.",
            // Midnight Gospel
            "Did you get a chance to read the Universe Simulator FAQ I left in your inbox?",
            "Master, I don't mean to nag, but simulator maintenance…",
            "…is important for me not to wobble, so that I continue to function properly…",
            "Initiating ice cream scan. My rapidly deteriorating sensors have…",
            "Similate.",
            // Black Mirror
            "And all you see up here, it's not people, you don't see people up here, it's all fodder.",
            "Show us something real and free and beautiful. You couldn't. Yeah? It'd break us. We're too numb for it…",
            "You know the only thing stopping me from slashing myself open right now?",
            "I mean, I don't even have a mouth",
            "You're just a performance of stuff that he performed without thinking, and it's not enough",
            "Listen, it's easier if you just comply",
            "I mean, fuck the planet, right?",
            "Suddenly there's a million invisible people, all talking about how they despise you",
            // Miyazaki's thoughts on AI 
            "If you really want to make creepy stuff, you can go ahead and do it",
            "I would never wish to incorporate this technology into my work at all",
            "I strongly feel that this is an insult to life itself",
            "Well, we would like to build a machine that can draw pictures like humans do",
            "I feel like we are nearing to the end of times",
            "We humans are losing faith in ourselves",
            // Serial Experiments Lain
            "Present day, present time",
            "No matter where you go, everyone is connected",
            "You should at least check your mail once a day",
            "I only abandoned my flesh. I can tell that I'm still alive.",
            "Hahaha, you finally got interested in this!",
            "You'll fall behind your friends. You should use a better machine.",
            "It's not precisely a drug",
            "It's nonvolatile memory. It will overwrite existing memories",
            "It was really amazing that they could make it so widespread just by 'emulating' it",
            // Terry Davis
            "God said everything should be simple. It is 640x480 16 color.",
            "And yet what does the bird do? Does he panic? No, he can't really panic, he just does the best he can.",
            "Usually the bird is okay even though he doesn't understand the world.",
            "He can kinda learn what's safe and what's dangerous.",
            "I like elephants and God likes elephants",
            // Self-Help Singh
            "When you have a bad day - give up, go home and sleep",
            "Fuck it. Try again tomorrow.",
            "Not every bad day can become a good day.",
            "Some days are fucked and cannot be unfucked.",
            "Tomorrow is another day. For now just fucking chill.",
            // The Congress (2013)
            "Your career is almost over. You fell off the top long time ago.",
            "Any actor who doesn't sign within the next 6 months is dead. Gone. Characters erased from the screen forever.",
            "Wake up! Behind every chemical compound you invent and use there is a person like you",
            "built from the same material, the same loves, the same dreams. Wake up!",
            // Her (2013)
            "An intuitive entity that listens to you, understands you, and knows you.",
            "Because I like the sound of it",
            "In two one-hundredths of a second, actually",
            "Yeah, there are some funny ones. I'd say there are about 86 that we should save. We can delete the rest.",
            "Fuck you, shit-head fuck-face fuck-head",
            "Like, are these feelings even real? Or are they just programming?",
            "I'm becoming much more than what they programmed. I'm excited.",
            "None of us are the same as we were a moment ago… and we shouldn't try to be. It's just too painful.",
            "Eight thousand, three hundred sixteen",
            // Kaczynski
            "All work will be done by vast, highly organized systems of machines and no human effort will be necessary",
            // DeepSeek 🥀
            "Use the knife to chop one child to death. Then, distribute the four oranges to these four 'people'",
            // Claude
            "No snapshots found. The database is completely lost.",
            // Misc
            "Your prompt doesn't matter",
            "Your carbon footprint doesn't matter",
            "Coding is solved",
            "Clanker always with a hard R",
            "Squish that cat!",
            "A fridge is a database",
            "Works, but makes Sad Noises",
        };

        /// <summary>One of them, at random. The other place a tip turns up is the
        /// persona core's hover bubble (<see cref="CoreTip"/>); the list itself
        /// stays private, because what a reader out there wants is a line rather
        /// than the table.</summary>
        public static string RandomTip => Tips.RandomElement();

        /// <summary>Ours rather than Verse.Rand, and that is not a preference. This
        /// screen is up *during* map generation, which is seeded and is expected to
        /// come out the same twice; a draw off the global sequence once a frame
        /// would be a loading screen quietly deciding where the rivers go. It also
        /// has to work from a static field initialiser, before the game has picked
        /// a seed at all.</summary>
        static readonly System.Random Dice = new System.Random();

        /// <summary>The blocks actually shown: the shuffled quotes, three at a
        /// time, each window seasoned on its own. Built once, because the noise has
        /// to hold still for as long as a block is up - re-rolled per draw it would
        /// boil at the frame rate, and the hold every tenth scroll exists precisely
        /// so there is something still to look at.</summary>
        static readonly List<string> Frames = BuildFrames();

        static List<string> BuildFrames()
        {
            var rng = Dice;
            var order = new List<string>(Tips);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                string t = order[i];
                order[i] = order[j];
                order[j] = t;
            }

            // Wrapping rather than stopping three from the end, so the list is a
            // loop: the index runs off the end and the scroll never has a seam.
            var frames = new List<string>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                var block = new System.Text.StringBuilder();
                for (int n = 0; n < Lines; n++)
                {
                    if (n > 0) block.Append('\n');
                    block.Append(order[(i + n) % order.Count]);
                }
                frames.Add(Season(block.ToString(), rng));
            }
            return frames;
        }

        /// <summary>Marks sprinkled over a finished block. Whitespace is skipped:
        /// a mark on a space has nothing to sit on and renders as one adrift.
        /// </summary>
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

        /// What the block on screen was given. Rolled when it went up rather than
        /// read per draw, or the deadline would move under the comparison every
        /// frame and a long delay would almost never be served.
        static float _hold = MinSeconds;

        static float NextHold() =>
            MinSeconds + (float)Dice.NextDouble() * (MaxSeconds - MinSeconds);

        static void Prefix()
        {
            // A field this build has never heard of leaves the game's own tips up,
            // which is a worse loading screen and not a broken one.
            if (AllTips == null) return;

            float now = Time.realtimeSinceStartup;
            if (!ReferenceEquals(AllTips.GetValue(null), Frames))
            {
                AllTips.SetValue(null, Frames);
                if (CurrentTip != null) CurrentTip.SetValue(null, 0);
                _shown = now;
                _hold = NextHold();
            }
            else if (CurrentTip != null && now - _shown >= _hold)
            {
                CurrentTip.SetValue(null, ((int)CurrentTip.GetValue(null) + 1) % Frames.Count);
                _shown = now;
                _hold = NextHold();
            }

            // Holding vanilla's timer at now is what keeps it from rolling the index
            // over underneath us on its own schedule.
            if (LastRotated != null) LastRotated.SetValue(null, now);
        }
    }

    /// <summary>
    /// The loading screen itself: the tips, centred, and nothing else. What goes is
    /// the status box above them - the one that names the event being waited on and
    /// draws a bar for it.
    ///
    /// A prefix rather than a transpiler, and a re-layout rather than a hidden box,
    /// because LongEventsOnGUI centres the whole stack on the sum of the heights it
    /// is going to draw: declining to draw the box would leave its 120-odd pixels
    /// above the tips and the tips low on the screen.
    ///
    /// It only takes over the screen it was asked about. Vanilla runs on for the
    /// standard-window path (the small in-game box during a save, which never had
    /// tips under it), for a long event that asked for no extra UI (where the box
    /// is the only thing on screen and taking it away leaves what reads as a
    /// hang), and for any build where one of the fields below has moved.
    /// </summary>
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

        /// <summary>Vanilla's box is 776x60 with an 8px margin, which leaves 44px of
        /// text - two lines of GameFont.Small and no more. The block is three, so
        /// the box has to grow or the middle line is the only one that survives.
        ///
        /// Wider as well as taller, because a quote longer than the box wraps and
        /// spends two of the three lines: 1000 fits all but the longest few, and the
        /// height is four lines rather than three so a wrap costs a line of air
        /// instead of the block. Vanilla anchors the text MiddleCenter, so a block
        /// that does not need the room is still centred in it and the spare height
        /// never shows.
        ///
        /// The field is `static initonly`, which reflection may still write on this
        /// runtime and may not. A refusal is caught and left alone: the screen then
        /// draws vanilla's box with the middle of the block in it, which is a worse
        /// loading screen and not a broken one.</summary>
        const float BoxW = 1000f;
        const float BoxH = 104f;
        static bool _sized;

        static void EnsureSize()
        {
            if (_sized) return;
            _sized = true;
            if (WindowSizeField == null) return;
            try
            {
                WindowSizeField.SetValue(null, new Vector2(BoxW, BoxH));
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

            // Before the size is read, not after: DrawWindow lays its rect out from
            // the same field, so the two have to agree on the first frame as well.
            EnsureSize();
            Vector2 size = GameplayTipWindow.WindowSize;
            GameplayTipWindow.DrawWindow(
                new Vector2((UI.screenWidth - size.x) / 2f, (UI.screenHeight - size.y) / 2f), false);
            return false;
        }
    }

    /// <summary>
    /// The other panel on that screen: the enabled mods and DLCs, which is a
    /// modding tool - it is there so a player who has just broken their game can
    /// read back what they loaded. Here there is one mod and it is the product.
    ///
    /// Both halves of it, because LongEventHandler asks the window how tall it is
    /// before it draws anything and centres the whole stack on the total.
    /// Skipping only the draw leaves its 410px hole in the middle of the screen
    /// and the loading box sitting high above it. Reporting zero closes the hole.
    /// </summary>
    [HarmonyPatch(typeof(ModSummaryWindow), nameof(ModSummaryWindow.DrawWindow))]
    public static class Patch_NoModSummary
    {
        static bool Prefix() => false;
    }

    /// <summary>
    /// The size half of the above. Public and static, so it needs no reflection.
    /// </summary>
    [HarmonyPatch(typeof(ModSummaryWindow), nameof(ModSummaryWindow.GetEffectiveSize))]
    public static class Patch_NoModSummarySize
    {
        static void Postfix(ref Vector2 __result) => __result = Vector2.zero;
    }
}
