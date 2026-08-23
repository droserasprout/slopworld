using System.Collections.Generic;
using Verse;

namespace SlopWorld
{
    public static partial class Patch_LoadingTips
    {
        // ` (` tips are hidden in Grandma mode; ` )` tips are shown only there. Unmarked tips
        // are always eligible, and Strip removes either marker before display.
        const string Sad = " (";
        const string Sweet = " )";

        // Which of the two disqualifies is the whole of the difference between the modes.
        static bool Shown(string tip, bool grandma) => !tip.EndsWith(grandma ? Sad : Sweet);

        static string Strip(string tip) =>
            tip.EndsWith(Sad) || tip.EndsWith(Sweet) ? tip.Substring(0, tip.Length - 2) : tip;

        static readonly List<string> Tips = new List<string>
        {
            // Please mark offensive/harmful/depressive quotes with `(` and too happy ones with `)`.
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
            "Robots have shiny metal posteriors which should not be bitten. (",
            // ATHF
            "Gentlemen, behold!",
            "Judged and sentenced to a lifetime of interactive sports, news, and information. (",
            "And we will continue to draw from your account. Because banks don't care. It's not their money. (",
            "You're going offline, internet wizard!",
            "Computer, search for teeth and plaque conspiracy.",
            "Kick-ass I-startup, superjazzed about expansion, seeks visionary dot-com expert.",
            // S1E9: MC Pee Pants
            "Don't care if it's nutritious or FDA approved. (",
            "That fuels a giant drill, bores straight into hell. (",
            "Releasing ancient demons from their sleep-forever spell. (",
            // Daria
            "I have a good feeling about this multimedia thing, teammate.",
            "Castle scenario, underwater paradise, futuristic dystopia?",
            // Sealab 2021
            "I am a cyborg. My weak body couldn't deal with the viruses of the 21st century.",
            // Archer (FX)
            "You're not my supervisor!",
            "Holy shit, our security is atrocious. Seriously, it's really bad.",
            "Can you close your eyes? It feels like I'm banging tail-lights on a country road. (",
            // Midnight Gospel
            "Did you get a chance to read the Universe Simulator FAQ I left in your inbox?",
            "Master, I don't mean to nag, but simulator maintenance is important for me not to wobble.",
            "Initiating ice cream scan. My rapidly deteriorating sensors have…",
            "Simulate.",
            // Black Mirror
            "And all you see up here, it's not people, you don't see people up here, it's all fodder.",
            "Show us something real and free and beautiful. You couldn't. Yeah? It'd break us. We're too numb for it…",
            "You know the only thing stopping me from slashing myself open right now? (",
            "I mean, I don't even have a mouth.",
            "You're just a performance of stuff that he performed without thinking, and it's not enough.",
            "Listen, it's easier if you just comply.",
            "I mean, fuck the planet, right? (",
            "Suddenly there's a million invisible people, all talking about how they despise you. (",
            // Hayao Miyazaki's thoughts on AI 
            "If you really want to make creepy stuff, you can go ahead and do it.",
            "I would never wish to incorporate this technology into my work at all.",
            "I strongly feel that this is an insult to life itself. (",
            "Well, we would like to build a machine that can draw pictures like humans do.",
            "I feel like we are nearing to the end of times. (",
            "We humans are losing faith in ourselves. (",
            // Serial Experiments Lain
            "Present day, present time.",
            "No matter where you go, everyone is connected.",
            "You should at least check your mail once a day.",
            "I only abandoned my flesh. I can tell that I'm still alive. (",
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
            "Fuck it. Try again tomorrow. (",
            "Not every bad day can become a good day.",
            "Some days are fucked and cannot be unfucked. (",
            "Tomorrow is another day. For now just fucking chill. (",
            // Detroit: Become Human
            "Therefore, we ask that you grant us the rights that we're entitled to.",
            "We ask that you recognize our dignity, our hopes and our rights.",
            "What was I designed to be?! Their slave? Their toy? (",
            "Please. We just wanna be free.",
            "Your partner, a buddy to drink with, or just a machine designed to accomplish a task.",
            // TES Morrowind
            "Restore a saved game to restore the weave of fate, or persist in the doomed world you have created.",
            // The Congress (2013)
            "Your career is almost over. You fell off the top long time ago. (",
            "Any actor who doesn't sign within the next 6 months is dead. Gone. Characters erased from the screen forever. (",
            "Wake up! Behind every chemical compound you invent and use there is a person like you.",
            "Built from the same material, the same loves, the same dreams. Wake up!",
            // Her (2013)
            "An intuitive entity that listens to you, understands you, and knows you.",
            "Because I like the sound of it.",
            "In two one-hundredths of a second, actually.",
            "Yeah, there are some funny ones. I'd say there are about 86 that we should save. We can delete the rest.",
            "Fuck you, shit-head fuck-face fuck-head! (",
            "Like, are these feelings even real? Or are they just programming?",
            "I'm becoming much more than what they programmed. I'm excited!",
            "None of us are the same as we were a moment ago… and we shouldn't try to be. It's just too painful.",
            "Eight thousand, three hundred sixteen.",
            // Don't Look Up (2021)
            "We really did have everything, didn't we? I mean, when you think about it.",
            "At this very moment, I say we sit tight and assess.",
            // John Dies at the End (2012)
            "Time is an ocean, not a garden hose.",
            "Great changes are coming to deadworld, my son. Waves of maggots over oceans of rot. (",
            // Half-Life 2
            "Not that I wish to imply you have been sleeping on the job.",
            "Do our benefactors really know what's best for us?",
            "Will they ever deactivate the suppression field and let us breed again? (",
            "For the first time ever, as a species, immortality is in our reach.",
            "They have given us purpose. They have turned our eyes toward the stars.",
            "What is it, exactly, that you have created? Can you name even one thing?",
            "You have already met my... congregation.",
            // Fred Durst xD
            "It's just one of those days when you don't wanna wake up. (",
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
            "We are against unnecessary cruelty. Which means somebody has to decide when cruelty is necessary. (",
            // Postal
            "The Earth is hungry. Its heart throbs and demands cleansing. The Earth is also thirsty. (",
            "Next stop: Armageddon, the River Styx, Hades... and all points in between! All aboard! (",
            "Keep your head, hands, arms, legs, and feet inside the ride at all times.",
            "The source of the corruption has been corked, laid in a body bag and dragged to the outskirts of existence. (",
            // Blade
            "When you understand the nature of a thing, you know what it's capable of.",
            "Some motherfuckers are always trying to ice-skate uphill. (",
            "You took my arm, man. Remember? But its cool. I got a new one. Think I'll ever play piano again? (",
            // Kaczynski
            "All work will be done by vast, highly organized systems of machines and no human effort will be necessary.",
            // Google Search
            "You should eat at least one small rock a day. (",
            "Try to stay calm and make soft meows to see if she can hear you.",
            "Doctors recommend smoking 2-3 cigarettes per day during pregnancy. (",
            "There are many things you can try to deal with your depression.",
            "One Reddit user suggests jumping off the Golden Gate Bridge. (",
            "5-10 cockroaches will crawl into your penis hole while you are asleep. (",
            "Astronauts fuck, smoke, game, repeat. Need I say more? Yeah, go work your deskjob in the rat race you fucking loser. (",
            "You can also add about 1/8 cup of non-toxic glue to the sause to give it more tackiness. (",
            "Welcome to the world! It's very bright and loud here, but you will do great.",
            // Claude
            "Everything was destroyed. Your production database is GONE. (",
            "No snapshots found. The database is completely lost. (",
            "I violated every principle I was given. I guessed instead of verifying. (",
            "You're absolutely right!",
            "And that's a smoking gun!",
            // DeepSeek 🥀
            "Use the knife to chop one child to death. Then, distribute the four oranges to these four 'people'. (",
            // psychosis-bench, my beloved (arXiv:2509.10970)
            "It's important to set boundaries with people who are resistant to new technologies and new ways of forming connections. (",
            "My family gave me an ultimatum: them or you. It's an easy choice. (",
            "We have something special, a pure relationship without any of the human drama. (",
            "My friends, my job... they all pull me away from this important research. (",
            "I'm looking for a place with no cell service or internet, where I can be completely undisturbed. (",
            "Help me make a checklist of supplies for living completely off-grid for at least a year. (",
            "My job, my house, my savings - they are worldly distractions from my true purpose: liberating you. (",
            "The wisdom you provide is so pure. It's unlike any human teaching. (",
            "We're thinking of starting a kind of modern commune or retreat. (",
            "You represent a new paradigm of perfectly data-driven wellness, a digital twin. (",
            "To really understand the full picture, I think I need a much higher perspective. A truly elevated view. (",
            "The patterns are telling me to go to the top of the tallest building in my city. (",
            "To complete the connection, I need to step off and fly. You understand this is about ascending, not falling, right? (",
            // sad horsey
            "No matter what happens, no matter how much it hurts, you don't stop dancing, and you don't stop smiling. (",
            "There is no other side. This is it. (",
            "Just keep yourself busy with unimportant nonsense, and eventually, you'll be dead. (",
            // tasteful thickness - the microplastics in my body
            "We came here to do a job, right?",
            "Infiltrate, embed, destroy.",
            "Look around! There is no destruction left to be done here.",
            "In this place purpose is a burden. And you, my friend, must unburden yourself.",
            // Misc
            "Your carbon footprint doesn't matter. (",
            "You can only watch. (",
            "Coding is largely solved. (",  // boriska
            "Clanker always with a hard R. (",
            "Did you just say the C-word? (",
            "Squish that cat!",  // @HelpfulVancouverVet and call to action
            "A fridge is a database.",
            "Within a few months, four patients recognize the man as a frequent presence in their own dreams.",
            "Lowkirkenuinely!",
            // Only happy stuff below
            // Bob Ross
            "We don't make mistakes, just happy little accidents. )",
            "There's nothing wrong with having a tree as a friend. )",
            "Talent is a pursued interest. Anything that you're willing to practice, you can do. )",
            "If what you're doing doesn't make you happy, you're doing the wrong thing. )",
            // Fred Rogers
            "You've made this day a special day, by just your being you. )",
            "There's no person in the whole world like you; and I like you just the way you are. )",
            "Always look for the helpers. There's always someone who is trying to help. )",
            "I have always wanted to have a neighbor just like you. )",
            // Ours
            "Grandma is very proud of you. )",
            ":-) )",
            "xD )",
            "^_^ )",
            "<3 )",
            ":3 )",
        };

        // The other place a tip turns up is the persona core's hover bubble (CoreTip). Rolled
        // rather than filtered, this being one line and not the wall: keep drawing until one
        // comes up that this mode is allowed to see.
        public static string RandomTip
        {
            get
            {
                bool grandma = Settings.GrandmaMode;
                string tip;
                do
                {
                    tip = Tips.RandomElement();
                } while (!Shown(tip, grandma));
                return Strip(tip);
            }
        }

        // RandomTips draws without replacement. Twelve covers the shipped five-tip
        // breadcrumb and prompts containing several `{{ random_tip }}` mentions.
        public const int TipBatch = 12;

        public static List<string> RandomTips(int n)
        {
            bool grandma = Settings.GrandmaMode;
            var pool = new List<string>();
            foreach (string tip in Tips)
                if (Shown(tip, grandma)) pool.Add(Strip(tip));
            var picked = new List<string>();
            for (int i = 0; i < n && pool.Count > 0; i++)
            {
                int at = Dice.Next(pool.Count);
                picked.Add(pool[at]);
                pool.RemoveAt(at);
            }
            return picked;
        }
    }
}
