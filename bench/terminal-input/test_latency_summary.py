import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("latency_summary", Path(__file__).with_name("latency-summary.py"))
summary = importlib.util.module_from_spec(spec)
spec.loader.exec_module(summary)


def record(ident, elapsed=100):
    return (f"[SlopWorld] latency id={ident:032x} status=frame_end kind=keys elapsed_us={elapsed} "
            "receive_us=40 dispatch_us=50 draw_us=60 daemon_received_us=1000 tmux_us=1005 "
            "first_capture_us=1010 capture_us=1020 send_us=1030 first_seq=2 seq=3")


class LatencySummaryTests(unittest.TestCase):
    def test_local_scroll_has_own_stages_and_censoring(self):
        result = summary.summarize([
            f"[SlopWorld] latency id={1:032x} kind=history_scroll status=frame_end elapsed_us=40 draw_us=35 scroll_source=precise",
            f"[SlopWorld] latency id={2:032x} kind=history_scroll status=superseded elapsed_us=10",
            f"[SlopWorld] latency id={3:032x} kind=history_scroll status=no_motion elapsed_us=1"])
        self.assertEqual(result['metrics']['history_scroll']['input_to_frame_end']['p99'], 40)
        self.assertEqual(result['counts']['superseded'], 1)
        self.assertEqual(result['counts']['no_motion'], 1)
        self.assertNotIn('unacknowledged_at_capture', result['counts'])
        self.assertEqual(result['scroll_sources']['precise'], 1)

    def test_individual_tail_and_clock_domains(self):
        result = summary.summarize(record(i, i + 100) for i in range(100))
        lanes = result["metrics"]["keys"]
        self.assertEqual(lanes["input_to_frame_end"], {"n": 100, "p50": 149, "p95": 194, "p99": 198, "max": 199})
        self.assertEqual(lanes["transport_and_client_send_residual"]["p50"], 10)
        self.assertEqual(result["counts"]["coalesced_samples"], 100)

    def test_missing_expired_and_duplicate_events(self):
        lines = [f"[SlopWorld] latency id={i:032x} status=start kind=keys" for i in range(3)]
        lines += [record(0), record(0), f"[SlopWorld] latency id={1:032x} status=timeout kind=keys elapsed_us=10000000"]
        result = summary.summarize(lines)
        self.assertEqual(result["counts"]["samples"], 1)
        self.assertEqual(result["counts"]["timeout"], 1)
        self.assertEqual(result["counts"]["unfinished"], 1)
        self.assertEqual(result["counts"]["completion_without_start"], 0)

    def test_invalid_samples_are_not_successes(self):
        result = summary.summarize([record(1).replace("draw_us=60", "draw_us=30"),
                                    record(2).replace("send_us=1030", "send_us=1100")])
        self.assertEqual(result["counts"]["invalid_sample"], 2)
        self.assertFalse(result["metrics"])

    def test_acknowledgement_is_optional_and_dropped_logs_are_counted(self):
        result = summary.summarize([record(1) + " tmux_done_us=1025",
                                    record(2), "[SlopWorld] latency dropped_records=9"])
        self.assertEqual(result["counts"]["dropped_records"], 9)
        self.assertEqual(result["counts"]["unacknowledged_at_capture"], 1)
        lane = result["metrics"]["keys"]["tmux_dispatch_to_observed_ack"]
        self.assertEqual(lane["n"], 1)
        self.assertEqual(lane["p50"], 20)

    def test_conflicting_records_rejected(self):
        with self.assertRaises(ValueError):
            summary.summarize([record(1), record(1, 200)])


if __name__ == "__main__":
    unittest.main()
