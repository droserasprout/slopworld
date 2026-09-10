#!/usr/bin/env python3

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import prose_lint


CASES = (
    ("mirrored-antithesis", "You can't wait; you can retry.", 1, None),
    ("mirrored-antithesis", "We won’t wait; we will retry.", 1, None),
    ("mirrored-antithesis", "You cann’t wait; you can retry.", 0, None),
    ("mirrored-antithesis", "We willn’t wait; we will retry.", 0, None),
    ("already-know", "If you already know\nPython, skip ahead.", 0, None),
    ("already-know", "If you already know\tPython, skip ahead.", 0, None),
    ("ai-leftovers", "The model has a knowledge cutoff.", 0, None),
    ("ai-leftovers", "utm_source=newsletter", 0, None),
    ("ai-leftovers", "turn1search0", 1, None),
    ("ai-leftovers", "turn12image34", 1, None),
    ("ai-leftovers", "return0search123helper", 0, None),
    ("ai-leftovers", "utm_source=chatgpt.com.example", 0, None),
    ("ai-leftovers", "contentReference is a field name.", 0, None),
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
    ("mirrored-antithesis", "You don't need magic; you need tests.", 1, None),
    ("mirrored-antithesis", "The goal isn't speed. It's predictability.", 1, None),
    ("mirrored-antithesis", "The goal is not speed. It is predictability.", 1, None),
    ("mirrored-antithesis", "This isn't polish; it's camouflage.", 1, None),
    ("mirrored-antithesis", "Not because it is easy, but because it is required.", 1, None),
    ("mirrored-antithesis", "You don't need this package.", 0, None),
    ("staged-reveal", "Here's the thing: the socket is shared.", 1, None),
    ("staged-reveal", "What matters is the serialized name.", 1, None),
    ("staged-reveal", "The key opens the settings page.", 0, None),
    ("emphatic-fragment", "Delete the fallback. Full stop.", 1, None),
    ("emphatic-fragment", "The sentence ends with a full stop.", 0, None),
    ("meta-scaffolding", "Let's break this down.", 1, None),
    ("meta-scaffolding", "Here's how to update it.", 1, None),
    ("meta-scaffolding", "Key takeaway: remove the wrapper.", 1, None),
    ("meta-scaffolding", "The key takeaway field is optional.", 0, None),
    ("performative-clarity", "To be clear, the cache is process-local.", 1, None),
    ("performative-clarity", "The panel must be clear of overlays.", 0, None),
    ("therapeutic-validation", "Give yourself permission to stop.", 1, None),
    ("therapeutic-validation", "The permission belongs to the token.", 0, None),
    ("commit-narration", "This change adds a retry cap.", 1, None),
    ("commit-narration", "Add a retry cap.", 0, None),
    ("commit-headings", "Key changes:\n", 1, None),
    ("commit-self-review", "This makes the code more maintainable.", 1, None),
    ("claude-attribution", "Generated with Claude Code", 1, None),
    ("claude-attribution", "Co-Authored-By: Claude <noreply@anthropic.com>", 1, None),
    ("functional-participle-tail", "The wrapper owns the handle, ensuring the cleanup order.", 1, None),
)


class DetectorTests(unittest.TestCase):
    def test_warning_cannot_hide_overlapping_error(self):
        text = "Close it, ensuring the log says that's the whole point."
        found = prose_lint.collect_matches(text, set(prose_lint.RULES_BY_ID))
        self.assertEqual(["whole"], [rule.id for rule, _ in found])

    def test_ambiguous_technical_statements_are_advisory(self):
        for text in (
            "The entire buffer is zeroed.",
            "This is the entire buffer.",
            "Replace each underscore with a hyphen.",
        ):
            with self.subTest(text=text):
                found = prose_lint.collect_matches(text, set(prose_lint.RULES_BY_ID))
                self.assertTrue(found)
                self.assertTrue(all(rule.severity == "warning" for rule, _ in found))

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

    def test_commit_rules_are_scoped(self):
        text = "This change adds retries."
        prose = prose_lint.collect_matches(text, set(prose_lint.RULES_BY_ID), "prose")
        commit = prose_lint.collect_matches(text, set(prose_lint.RULES_BY_ID), "commit")
        self.assertNotIn("commit-narration", [rule.id for rule, _ in prose])
        self.assertIn("commit-narration", [rule.id for rule, _ in commit])

    def test_vocabulary_cluster_requires_distinct_terms(self):
        rule = prose_lint.RULES_BY_ID["claude-vocab-cluster"]
        self.assertEqual([], rule.find("The robust parser has robust tests."))
        self.assertEqual(1, len(rule.find("The robust parser offers comprehensive coverage.")))

    def test_vocabulary_inflections_share_a_family(self):
        rule = prose_lint.RULES_BY_ID["claude-vocab-cluster"]
        for sample in (
            "The robust parser handles errors robustly.",
            "This notable change notably reduces allocations.",
            "Leverage what the earlier implementation leveraged.",
        ):
            with self.subTest(sample=sample):
                self.assertEqual([], rule.find(sample))
        self.assertEqual(1, len(rule.find("Robust, robustly tested, and comprehensive.")))

    def test_triads_do_not_infer_adjectives_from_suffixes(self):
        rule = prose_lint.RULES_BY_ID["rhetorical-triads"]
        self.assertEqual([], rule.find("Cable, table, and archive. Cable, table, and archive."))

    def test_bold_leads_accept_both_colon_placements(self):
        rule = prose_lint.RULES_BY_ID["bold-lead-density"]
        for label in ("**Input**:", "**Input:**"):
            with self.subTest(label=label):
                self.assertEqual([], rule.find((label + " value\n") * 2))
                self.assertEqual(1, len(rule.find((label + " value\n") * 3)))

    def test_em_dash_density_boundary_is_unchanged(self):
        rule = prose_lint.RULES_BY_ID["em-dash-density"]
        self.assertEqual(1, len(rule.find("one — two — " + "word " * 295)))
        self.assertEqual([], rule.find("one — two — " + "word " * 296))
        self.assertEqual([], rule.find("- The worker — after opening the socket — waits."))

    def test_literal_participle_clause_is_advisory(self):
        text = "The worker, signaling the condition variable, wakes the reader."
        found = prose_lint.collect_matches(text, set(prose_lint.RULES_BY_ID))
        self.assertTrue(found)
        self.assertTrue(all(rule.severity == "warning" for rule, _ in found))

    def test_density_rules_use_thresholds(self):
        dash = prose_lint.RULES_BY_ID["em-dash-density"]
        triads = prose_lint.RULES_BY_ID["rhetorical-triads"]
        bold = prose_lint.RULES_BY_ID["bold-lead-density"]
        self.assertEqual([], dash.find("One clause — one aside."))
        self.assertEqual(1, len(dash.find("One — two — three.")))
        self.assertEqual([], triads.find("Fast, stable, and small."))
        self.assertEqual(1, len(triads.find("Fast, stable, and scalable. Clear, direct and useful.")))
        self.assertEqual([], bold.find("- **Input**: one\n- **Output**: two\n"))
        self.assertEqual(1, len(bold.find("- **Input**: one\n- **Output**: two\n- **State**: three\n")))

    def test_hedge_density_requires_four_nearby_hits(self):
        rule = prose_lint.RULES_BY_ID["hedge-density"]
        self.assertEqual([], rule.find("This may fail and could retry."))
        self.assertEqual(1, len(rule.find("This may fail, could retry, might recover, and usually succeeds.")))


class InputTests(unittest.TestCase):
    def test_markdown_closing_fence_must_match_opening(self):
        for opening, interior, closing in (
            ("````text", "```", "````"),
            ("~~~~text", "~~~", "~~~~~"),
            ("```text", "```still code", "```"),
            ("```text", "~~~", "```"),
        ):
            with self.subTest(opening=opening, interior=interior):
                text = f"{opening}\n{interior}\nThat's the whole point.\n{closing}\nThat's the whole change.\n"
                masked = prose_lint.mask_markdown(text)
                found = prose_lint.collect_matches(masked, {"whole"})
                self.assertEqual(1, len(found))
                self.assertEqual((5, 1), prose_lint.line_column(prose_lint.locations(text), found[0][1].start))

    def test_rust_lifetimes_labels_and_characters_preserve_comments(self):
        for code in (
            "fn f(x: &'static str) {}",
            "fn f<'a>(x: &'a str) {}",
            "'outer: loop { break 'outer; }",
            "let c = 'a';",
            r"let c = '\'';",
            "let c = 'é';",
        ):
            with self.subTest(code=code):
                text = code + " // That's the whole point.\n"
                masked = prose_lint.lintable_text(Path("source.rs"), text)
                found = prose_lint.collect_matches(masked, {"whole"})
                self.assertEqual(1, len(found))
                self.assertEqual(text.index("That's"), found[0][1].start)

    def test_markdown_skips_code(self):
        text = "Plain prose.\n\n```text\nThat's the whole point.\n```\n"
        masked = prose_lint.lintable_text(Path("note.md"), text)
        self.assertEqual([], prose_lint.collect_matches(masked, {"whole"}))

    def test_c_like_scans_comments_not_strings(self):
        text = 'var value = "No fluff, no filler"; // That is the whole point.\n'
        masked = prose_lint.lintable_text(Path("source.cs"), text)
        found = prose_lint.collect_matches(masked, set(prose_lint.RULES_BY_ID))
        self.assertEqual(["whole"], [rule.id for rule, _ in found])

    def test_javascript_template_literal_is_not_a_comment(self):
        text = 'const value = `text // That is the whole point.`;\n'
        masked = prose_lint.lintable_text(Path("source.js"), text)
        self.assertEqual([], prose_lint.collect_matches(masked, {"whole"}))

    def test_rust_nested_block_comment_is_scanned_to_outer_end(self):
        text = "/* outer /* inner */ That is the whole point. */\n"
        masked = prose_lint.lintable_text(Path("source.rs"), text)
        found = prose_lint.collect_matches(masked, {"whole"})
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
    def test_missing_explicit_input_is_an_operational_error(self):
        with tempfile.TemporaryDirectory() as directory:
            missing = str(Path(directory) / "missing.md")
            for arguments in ([missing], ["--commit-msg", missing]):
                with self.subTest(arguments=arguments):
                    result = subprocess.run(
                        [sys.executable, prose_lint.__file__, *arguments],
                        capture_output=True,
                        text=True,
                    )
                    self.assertEqual(2, result.returncode)
                    self.assertIn("input does not exist", result.stderr)

    def test_overlapping_warning_does_not_change_failure_status(self):
        result = subprocess.run(
            [sys.executable, prose_lint.__file__, "-"],
            input="Close it, ensuring the log says that's the whole point.",
            capture_output=True,
            text=True,
        )
        self.assertEqual(1, result.returncode)
        self.assertIn("cliche/whole", result.stdout)

    def test_stdin_skips_markdown_code(self):
        result = subprocess.run(
            [sys.executable, prose_lint.__file__, "-"],
            input="```text\nThat's the whole point.\n```\n",
            capture_output=True,
            text=True,
        )
        self.assertEqual(0, result.returncode)
        self.assertEqual("", result.stdout)

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

    def test_warnings_are_advisory_unless_strict(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "sample.md"
            path.write_text("Here's the thing: one warning is enough.\n", encoding="utf-8")
            advisory = subprocess.run(
                [sys.executable, prose_lint.__file__, str(path)],
                capture_output=True,
                text=True,
            )
            strict = subprocess.run(
                [sys.executable, prose_lint.__file__, "--fail-on-warnings", str(path)],
                capture_output=True,
                text=True,
            )
        self.assertEqual(0, advisory.returncode)
        self.assertIn("warning/cliche/staged-reveal", advisory.stdout)
        self.assertEqual(1, strict.returncode)


if __name__ == "__main__":
    unittest.main()
