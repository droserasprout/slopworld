#!/usr/bin/env python3
"""Check benchmark aggregation and build/measurement ordering."""
import importlib.util
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

    def test_display_precision(self):
        self.assertEqual("0.004 [0.003–0.10]", report.format_timing_range(.004, (.003, .1)))
        self.assertEqual("639.51", report.format_timing(639.508))

    def test_build_once_before_three_runs(self):
        events = []
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "report.md"
            with patch.object(sys, "argv", ["bench-report.py", "--output", str(output)]), \
                    patch.object(report, "commit_hash", return_value="test"), \
                    patch.object(report.subprocess, "run", side_effect=lambda *a, **k: events.append("build")) as build, \
                    patch.object(report, "run_bench", side_effect=lambda b, n, p: events.append(n) or run(n, n + 1)):
                self.assertEqual(0, report.main())
            self.assertEqual(["build", 1, 2, 3], events)
            self.assertEqual(["BUILD=release", "bench-build"], build.call_args.args[0][-2:])
            self.assertIn("2.00 [1.00–3.00]", output.read_text())


if __name__ == "__main__":
    unittest.main()
