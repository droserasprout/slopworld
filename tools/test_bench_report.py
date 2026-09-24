#!/usr/bin/env python3
"""Check benchmark aggregation and build/measurement ordering."""
import importlib.util
import io
import sys
import tempfile
import unittest
from collections import OrderedDict
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("bench_report", Path(__file__).with_name("bench-report.py"))
report = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = report
spec.loader.exec_module(report)


def run(p50, p95, allocated=10, wire=100):
    return OrderedDict(case=report.Sample(p50, p95, allocated, wire))


class BenchReportTests(unittest.TestCase):
    def test_saved_csv_report_and_relative_comparison(self):
        with tempfile.TemporaryDirectory() as directory:
            new, old = Path(directory) / "new", Path(directory) / "old"
            for target, values in ((old, (10, 12, 11)), (new, (8, 9, 10))):
                report.data.add_metadata(target, "gamefree", "", {
                    "build": "release", "system": "Linux", "machine": "x86_64",
                    "cpu_count": 16, "host": "same-host"})
                for index, value in enumerate(values, 1):
                    report.data.add_metric(target, "daemon", "", "render", "duration", "p50", "us", index, value)
            absolute = report.data.render(new, old)
            relative = report.data.render(new, old, "relative")
            self.assertIn("9.000 [8.000–10.000] us", absolute)
            self.assertIn("-18.18%", absolute)
            self.assertIn("-18.18%", relative)
            self.assertNotIn("[8.000–10.000]", relative)

    def test_latest_report_has_stable_name_and_no_capture_dates(self):
        with tempfile.TemporaryDirectory() as directory:
            run_dir = Path(directory) / "full-20260924T230211Z"
            report.data.add_metadata(run_dir, "gamefree", "", {
                "started": "2026-09-24T23:02:11Z", "build": "release"})
            report.data.add_metric(run_dir, "daemon", "", "render", "duration", "p50", "us", 1, 10)
            latest = report.data.render(run_dir, latest=True)
            self.assertIn("# Latest benchmark report", latest)
            self.assertIn("daemon / render / duration", latest)
            self.assertIn("| gamefree | build | release |", latest)
            self.assertNotIn("2026", latest)
            self.assertNotIn("20260924", latest)
            self.assertNotIn("| started |", latest)

    def test_gamefree_comparison_requires_matching_build_and_host(self):
        with tempfile.TemporaryDirectory() as directory:
            new, old = Path(directory) / "new", Path(directory) / "old"
            for target, build, host in ((old, "debug", "old-host"), (new, "release", "new-host")):
                report.data.add_metadata(target, "gamefree", "", {
                    "build": build, "system": "Linux", "machine": "x86_64",
                    "cpu_count": 16, "host": host})
                report.data.add_metric(target, "daemon", "", "render", "duration", "p50", "us", 1, 10)
            self.assertIn("incompatible workload", report.data.render(new, old, "relative"))
            report.data.add_metadata(new, "gamefree", "", {"build": "debug"})
            self.assertIn("incompatible workload", report.data.render(new, old, "relative"))
            report.data.add_metadata(new, "gamefree", "", {"host": "old-host"})
            self.assertIn("+0.00%", report.data.render(new, old, "relative"))

    def test_failed_benchmark_keeps_its_output_log(self):
        class FailedProcess:
            stdout = io.StringIO("benchmark failed after setup\n")

            def wait(self):
                return 7

        with tempfile.TemporaryDirectory() as directory:
            raw = Path(directory) / "raw"
            with patch.object(report.subprocess, "Popen", return_value=FailedProcess()):
                with self.assertRaisesRegex(RuntimeError, "exit status 7"):
                    report.run_bench("release", 1, raw, "daemon")
            self.assertEqual("benchmark failed after setup\n",
                             (raw / "repeat-1" / "gamefree.log").read_text())

    def test_comparison_shows_baseline_only_metrics(self):
        with tempfile.TemporaryDirectory() as directory:
            new, old = Path(directory) / "new", Path(directory) / "old"
            report.data.add_metric(new, "daemon", "", "kept", "duration", "p50", "us", 1, 10)
            report.data.add_metric(old, "daemon", "", "removed", "duration", "p50", "us", 1, 20)
            text = report.data.render(new, old)
            self.assertIn("daemon / removed / duration | p50 | 20.000 us | — | — | 0 | missing", text)

    def test_censored_terminal_latency_is_saved_but_withheld(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory)
            report.data.add_metric(target, "terminal", "typing", "paste", "latency/input_to_frame_end",
                                   "p95", "us", 1, 99000, "censored")
            report.data.add_outcomes(target, "terminal", "typing", "", 1, {"overflow": 5})
            text = report.data.render(target)
            self.assertIn("withheld", text)
            self.assertIn("overflow | 5", text)
            self.assertNotIn("99000.000", text)
            comparison = report.data.render(target, target)
            self.assertIn("withheld | withheld", comparison)

    def test_terminal_comparison_rejects_a_different_input_rate(self):
        with tempfile.TemporaryDirectory() as directory:
            new, old = Path(directory) / "new", Path(directory) / "old"
            for target, rate in ((new, 100), (old, 50)):
                report.data.add_metadata(target, "terminal", "typing", {"rate": rate, "backend": "xdotool"})
                report.data.add_metric(target, "terminal", "typing", "paste", "latency/input_to_frame_end",
                                       "p50", "us", 1, 50000)
            self.assertIn("incompatible workload", report.data.render(new, old, "relative"))

    def test_outlier_changes_range_not_median(self):
        sample = report.summarize_runs([run(1, 4, 10), run(100, 200, 1000), run(2, 3, 20)])["case"]
        self.assertEqual((2, 4, 20), (sample.p50, sample.p95, sample.bytes_per_op))
        self.assertEqual((1, 100), sample.p50_range)
        self.assertEqual((3, 200), sample.p95_range)
        self.assertEqual((10, 1000), sample.bytes_range)

    def test_missing_benchmark_rejected(self):
        with self.assertRaisesRegex(RuntimeError, "missing: case"):
            report.summarize_runs([run(1, 2), OrderedDict()])

    def test_wire_changes_rejected(self):
        with self.assertRaisesRegex(RuntimeError, "wire size changed"):
            report.summarize_runs([run(1, 2), run(1, 2, wire=101)])

    def test_unmeasured_allocations_stay_absent(self):
        sample = report.summarize_runs([run(1, 2, None)] * 3)["case"]
        self.assertIsNone(sample.bytes_per_op)
        self.assertIsNone(sample.bytes_range)

    def test_build_once_before_three_runs(self):
        events = []
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "report.md"
            existing = Path(directory) / "results" / "trial"
            report.data.add_metric(existing, "terminal", "typing", "paste", "latency/input_to_frame_end",
                                   "p50", "us", 1, 50000)
            with patch.object(report.data, "ROOT", Path(directory) / "results"), \
                    patch.object(sys, "argv", ["bench-report.py", "run", "--run", "trial", "--output", str(output)]), \
                    patch.object(report, "commit_hash", return_value="test"), \
                    patch.object(report.subprocess, "run", side_effect=lambda *a, **k: events.append("build")) as build, \
                    patch.object(report, "run_bench", side_effect=lambda b, n, p, s: events.append(n) or run(n, n + 1)):
                self.assertEqual(0, report.main())
            self.assertEqual(["build", 1, 2, 3], events)
            self.assertEqual(["BUILD=release", "bench-build"], build.call_args.args[0][-2:])
            self.assertIn("2.000 [1.000–3.000]", output.read_text())
            self.assertIn("terminal / typing / paste", output.read_text())
            self.assertTrue((Path(directory) / "results" / "trial" / "metrics.csv").exists())


if __name__ == "__main__":
    unittest.main()
