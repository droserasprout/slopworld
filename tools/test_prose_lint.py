#!/usr/bin/env python3

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import prose_lint


CASES = (
    ("no-chain", "No sign-ups, no downloads, no hassle — just paste and go.", 1, [3]),
    ("no-chain", "The plan has no hidden fees and no long-term contracts.", 1, [2]),
    ("no-chain", "No fluff, no filler, no jargon, no corporate buzzwords.", 1, [4]),
    ("no-chain", "There is no catch here, honestly.", 0, []),
    ("no-chain", "It ships with no bells and whistles, no fluff.", 1, [2]),
    ("no-chain", "No, no, I insist.", 0, []),
    ("no-chain", "no no no", 0, []),
    ("no-chain", "with no list patterns at all, so nothing lights up.", 0, []),
    ("no-chain", "NO FEES, NO CONTRACTS, NO SURPRISES", 1, [3]),
    ("no-chain", "no fluff; no filler", 1, [2]),
    ("no-chain", "no time, no money, no way to say no thanks", 1, [3]),
    ("no-chain", "no-code, no-fuss setup", 1, [2]),
    ("no-chain", "I know nothing, notice nothing.", 0, []),
    ("whole", "That's the whole point.", 1, None),
    ("whole", "This is the whole game, really.", 1, None),
    ("whole", "That was the whole pitch.", 1, None),
    ("whole", "The whole team showed up.", 0, None),
    ("did-not-chain", "Did not flinch, did not blink, did not apologize.", 1, [3]),
    ("did-not-chain", "He didn't call and didn't write.", 1, [2]),
    ("did-not-chain", "She did not go.", 0, []),
    ("dont-verb-it", "Don't call it a comeback. Call it a return.", 1, None),
    ("dont-verb-it", "Do not think of it as a burden. Think of it as fuel.", 1, None),
    ("dont-verb-it", "Don't fear it. Name it.", 0, None),
    ("dont-verb-it", "Don’t call it \"luck.\" Call it preparation.", 1, None),
    ("dont-verb-it", "Don't just read it — read it aloud.", 1, None),
    ("sit-with", "Sit with that for a moment.", 1, None),
    ("sit-with", "Just sit with it.", 1, None),
    ("sit-with", "She was sitting with the discomfort.", 1, None),
    ("sit-with", "Come sit with us at lunch.", 0, None),
    ("already-know", "You already know the answer.", 1, None),
    ("already-know", "Deep down, you already know.", 1, None),
    ("already-know", "If you already know Python, skip ahead.", 0, None),
    ("already-know", "You already know what to do.", 1, None),
    ("is-the-entire", "Consistency is the entire game.", 1, None),
    ("is-the-entire", "He toured the entire factory.", 0, None),
    ("the-entire-is", "The entire point is that nobody reads.", 1, None),
    ("the-entire-is", "The entire business model is built on churn.", 1, None),
    ("the-entire-is", "He ate the entire pizza.", 0, None),
    ("the-entire-is", "The entire history of the modern industrial world economy is complex.", 0, None),
    ("is-real", "The improvement is real, and it's not subtle.", 1, None),
    ("is-real", "This is the real work, and it never ends.", 1, None),
    ("is-real", "He is a real estate agent and it shows.", 0, None),
    ("is-real", "The painting is real, but stolen.", 0, None),
    ("punchline", "The punchline is that nobody laughed.", 1, None),
    ("punchline", "The punchline: nothing changed.", 1, None),
    ("punchline", "He forgot the punchline entirely.", 0, None),
    ("worth-naming", "That loss is real and it's worth naming.", 1, None),
    ("worth-naming", "Worth naming: nobody asked for this.", 1, None),
    ("worth-naming", "It's not worth naming names here.", 0, None),
    ("worth-naming", "The naming convention is worth documenting.", 0, None),
    ("not-nothing", "That's not nothing.", 1, None),
    ("not-nothing", "Ten sign-ups — that is not nothing.", 1, None),
    ("not-nothing", "There is nothing left to say.", 0, None),
    ("ai-vocab", "We delve into the intricacies of the interplay.", 3, None),
    ("ai-vocab", "Her vibrant tapestry hung in the bustling hall.", 3, None),
    ("ai-vocab", "A meticulously curated, seamless experience.", 2, None),
    ("ai-vocab", "The report was thorough and well organized.", 0, None),
    ("not-just", "This is not just a tool, but a philosophy.", 1, None),
    ("not-just", "Not only fast but also reliable.", 1, None),
    ("not-just", "It’s not a bug — it’s a feature.", 1, None),
    ("not-just", "She was not sure about the plan.", 0, None),
    ("note-that", "It is important to note that timing matters.", 1, None),
    ("note-that", "It’s worth noting the fees are separate.", 1, None),
    ("note-that", "It should be noted that this changed in 2020.", 1, None),
    ("note-that", "Please note the door code.", 0, None),
    ("testament", "The building stands as a testament to optimism.", 1, None),
    ("testament", "Her career is a testament to persistence.", 1, None),
    ("testament", "It serves as a stark reminder that nothing lasts.", 1, None),
    ("testament", "He read from the Old Testament.", 0, None),
    ("crucial-role", "Volunteers play a crucial role in the program.", 1, None),
    ("crucial-role", "She played a truly pivotal role in the merger.", 1, None),
    ("crucial-role", "He plays the role of the villain.", 0, None),
    ("landscape", "Adapting to an ever-evolving landscape.", 1, None),
    ("landscape", "The rapidly changing landscape of retail.", 1, None),
    ("landscape", "In today’s fast-paced world, attention is scarce.", 1, None),
    ("landscape", "The landscape outside was gray.", 0, None),
    ("vague-experts", "Experts argue that the policy failed.", 1, None),
    ("vague-experts", "Some critics have noted a decline in quality.", 1, None),
    ("vague-experts", "Industry reports suggest strong demand.", 1, None),
    ("vague-experts", "Dr. Chen argued the opposite.", 0, None),
    ("despite-challenges", "Despite these challenges, growth continued.", 1, None),
    ("despite-challenges", "The sector faces several challenges.", 1, None),
    ("despite-challenges", "Whether it works remains to be seen.", 1, None),
    ("despite-challenges", "The climb was a challenge.", 0, None),
    ("participle-tail", "Sales doubled, underscoring the strength of the brand.", 1, None),
    ("participle-tail", "She kept highlighting passages in yellow.", 0, None),
    ("participle-tail", "The team, reflecting on the loss, regrouped.", 0, None),
    ("promo", "The inn is nestled in a quiet valley.", 1, None),
    ("promo", "The museum boasts a rich tapestry of exhibits.", 2, None),
    ("promo", "Located in the heart of downtown.", 1, None),
    ("promo", "A hidden gem with breathtaking views.", 2, None),
    ("promo", "The soup was rich and hearty.", 0, None),
    ("ai-leftovers", "As of my last update, the API was in beta.", 1, None),
    ("ai-leftovers", "As an AI language model, I cannot form opinions.", 1, None),
    ("ai-leftovers", "See x.test/?utm_source=chatgpt.com for details.", 1, None),
    ("ai-leftovers", "contentReference[oaicite:0]{index=0}", 2, None),
    ("ai-leftovers", "The last update shipped on Tuesday.", 0, None),
)


class DetectorTests(unittest.TestCase):
    def test_cases(self):
        for rule_id, sample, expected, counts in CASES:
            with self.subTest(rule=rule_id, sample=sample):
                found = prose_lint.RULES_BY_ID[rule_id].find(sample)
                self.assertEqual(expected, len(found))
                if counts is not None:
                    self.assertEqual(counts, [match.count for match in found])

    def test_overlap_keeps_longest_match(self):
        text = "The museum boasts a rich tapestry of exhibits."
        found = prose_lint.collect_matches(text, set(prose_lint.RULES_BY_ID))
        self.assertEqual(["promo", "promo"], [rule.id for rule, _ in found])


class InputTests(unittest.TestCase):
    def test_markdown_skips_code(self):
        text = "Plain prose.\n\n```text\nThat's the whole point.\n```\n"
        masked = prose_lint.lintable_text(Path("note.md"), text)
        self.assertEqual([], prose_lint.collect_matches(masked, {"whole"}))

    def test_c_like_scans_comments_not_strings(self):
        text = 'var value = "No fluff, no filler"; // That is the whole point.\n'
        masked = prose_lint.lintable_text(Path("source.cs"), text)
        found = prose_lint.collect_matches(masked, set(prose_lint.RULES_BY_ID))
        self.assertEqual(["whole"], [rule.id for rule, _ in found])

    def test_hash_scans_comments_not_strings(self):
        text = 'value = "No fluff, no filler"  # It is important to note that this stays.\n'
        masked = prose_lint.lintable_text(Path("source.py"), text)
        found = prose_lint.collect_matches(masked, set(prose_lint.RULES_BY_ID))
        self.assertEqual(["note-that"], [rule.id for rule, _ in found])

    def test_xml_scans_comments_only(self):
        text = '<value>That is the whole point.</value><!-- No fluff, no filler. -->'
        masked = prose_lint.lintable_text(Path("data.xml"), text)
        found = prose_lint.collect_matches(masked, set(prose_lint.RULES_BY_ID))
        self.assertEqual(["no-chain"], [rule.id for rule, _ in found])

    def test_commit_message_skips_git_template_and_verbose_diff(self):
        text = (
            "That's the whole change.\n\n"
            "# No fluff, no filler.\n"
            "# ------------------------ >8 ------------------------\n"
            "+It is important to note that this is diff content.\n"
        )
        masked = prose_lint.mask_commit_message(text)
        found = prose_lint.collect_matches(masked, set(prose_lint.RULES_BY_ID))
        self.assertEqual(["whole"], [rule.id for rule, _ in found])


class CliTests(unittest.TestCase):
    def test_json_diagnostic_and_exit_status(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "sample.md"
            path.write_text("Ordinary.\nThat's the whole point.\n", encoding="utf-8")
            result = subprocess.run(
                [sys.executable, prose_lint.__file__, "--format", "json", str(path)],
                capture_output=True,
                text=True,
            )
        self.assertEqual(1, result.returncode)
        diagnostic = json.loads(result.stdout)
        self.assertEqual((2, 1, "whole"), (diagnostic["line"], diagnostic["column"], diagnostic["rule"]))

    def test_clean_file_succeeds(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "sample.md"
            path.write_text("The worker opens the file.\n", encoding="utf-8")
            result = subprocess.run([sys.executable, prose_lint.__file__, str(path)])
        self.assertEqual(0, result.returncode)

    def test_commit_message_mode_accepts_extensionless_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "COMMIT_EDITMSG"
            path.write_text("No churn, no mystery.\n# That's the whole point.\n", encoding="utf-8")
            result = subprocess.run(
                [sys.executable, prose_lint.__file__, "--commit-msg", str(path)],
                capture_output=True,
                text=True,
            )
        self.assertEqual(1, result.returncode)
        self.assertIn("cliche/no-chain", result.stdout)
        self.assertNotIn("cliche/whole", result.stdout)


if __name__ == "__main__":
    unittest.main()
