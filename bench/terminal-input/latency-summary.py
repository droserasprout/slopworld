"""Summarize individual input-to-frame-end samples (not physical presentation)."""
import argparse
from collections import Counter, defaultdict
import json
import math
from pathlib import Path
import re

MARKER = "[SlopWorld] latency "
ID = re.compile(r"^[0-9a-fA-F]{32}$")


def percentile(values, percent):
    """Nearest-rank percentile of individual observations, without batching."""
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * percent / 100) - 1)]


def stages(record):
    number = lambda key: int(record[key])
    if record.get("kind") == "history_scroll":
        total, draw = number("elapsed_us"), number("draw_us")
        if not 0 <= draw <= total:
            raise ValueError("out-of-order local scroll timestamps")
        return {"input_to_frame_end": total, "input_to_draw": draw, "draw_to_frame_end": total - draw}
    total, receive, dispatch, draw = map(number, ("elapsed_us", "receive_us", "dispatch_us", "draw_us"))
    received, tmux, first, capture, sent = map(number, (
        "daemon_received_us", "tmux_us", "first_capture_us", "capture_us", "send_us"))
    if not (0 <= receive <= dispatch <= draw <= total and 0 < received <= tmux <= first <= capture <= sent):
        raise ValueError("out-of-order timestamps")
    if number("first_seq") > number("seq"):
        raise ValueError("out-of-order frame sequences")
    residual = receive - (sent - received)
    if residual < 0:
        raise ValueError("daemon span exceeds client round trip")
    ack = int(record.get("tmux_done_us", "0"))
    if ack and not tmux <= ack <= sent:
        raise ValueError("out-of-order tmux acknowledgement")
    result = {
        "input_to_frame_end": total,
        "input_to_receive": receive,
        "receive_to_dispatch": dispatch - receive,
        "dispatch_to_draw": draw - dispatch,
        "draw_to_frame_end": total - draw,
        "daemon_input_to_tmux_dispatch": tmux - received,
        "tmux_dispatch_to_first_capture": first - tmux,
        "tmux_dispatch_to_visible_capture": capture - tmux,
        "visible_capture_to_ws_send": sent - capture,
        "daemon_span": sent - received,
        "transport_and_client_send_residual": residual,
    }
    if ack:
        result["tmux_dispatch_to_observed_ack"] = ack - tmux
    return result


def summarize(lines):
    started = set()
    finished = {}
    malformed = 0
    dropped = 0
    for line in lines:
        if MARKER not in line:
            continue
        fields = dict(re.findall(r"(\w+)=([^\s]+)", line.split(MARKER, 1)[1]))
        if "dropped_records" in fields:
            dropped += int(fields["dropped_records"])
            continue
        ident, status = fields.get("id", ""), fields.get("status")
        if not ID.fullmatch(ident) or not status:
            malformed += 1
            continue
        if status == "start":
            started.add(ident)
        elif ident in finished and finished[ident] != fields:
            raise ValueError(f"conflicting completion for {ident}")
        else:
            finished[ident] = fields
    counts = Counter(r["status"] for r in finished.values())
    counts["unfinished"] = len(started - finished.keys())
    counts["completion_without_start"] = len(finished.keys() - started)
    counts["malformed"] = malformed
    counts["dropped_records"] = dropped
    groups = defaultdict(lambda: defaultdict(list))
    for r in finished.values():
        if r["status"] != "frame_end":
            continue
        try:
            values = stages(r)
        except (ValueError, KeyError):
            counts["invalid_sample"] += 1
            continue
        counts["samples"] += 1
        if r.get("kind") != "history_scroll":
            counts["unacknowledged_at_capture"] += int(r.get("tmux_done_us", "0")) == 0
            counts["coalesced_samples"] += int(r["seq"]) != int(r["first_seq"])
        for key, value in values.items():
            groups[r.get("kind", "unknown")][key].append(value)
    metrics = {}
    for kind, lanes in sorted(groups.items()):
        metrics[kind] = {name: {"n": len(v), "p50": percentile(v, 50), "p95": percentile(v, 95),
                                "p99": percentile(v, 99), "max": max(v)} for name, v in lanes.items()}
    return {"endpoint": "unity_frame_end_before_presentation", "units": "us",
            "correlation": "next_frame_after_input_not_verified_echo",
            "history_scroll_correlation": "consumed_movement_to_ready_repaint_superseded_events_censored",
            "scroll_sources": dict(Counter(r.get("scroll_source", "legacy_wheel") for r in finished.values()
                                           if r.get("kind") == "history_scroll")), "counts": dict(counts), "metrics": metrics}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("files", type=Path, nargs="+")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args()
    def lines():
        for path in args.files:
            with path.open() as source:
                yield from source
    result = summarize(lines())
    if args.json:
        print(json.dumps(result, indent=2))
    else:
        print("Individual request/local-scroll latency; microseconds; nearest-rank percentiles.")
        print("Endpoint: Unity frame end BEFORE presentation. Correlation: next frame, not verified echo.")
        print("Counts: " + ", ".join(f"{k}={v}" for k, v in sorted(result["counts"].items())))
        for kind, lanes in result["metrics"].items():
            print(f"\n{kind}: {'stage':42} {'n':>7} {'p50':>10} {'p95':>10} {'p99':>10} {'max':>10}")
            for stage, v in lanes.items():
                print(f"  {stage:46} {v['n']:7} {v['p50']:10} {v['p95']:10} {v['p99']:10} {v['max']:10}")
        print("Only completed observations enter percentiles; inspect superseded, no_motion, timeout and unfinished counts.")
    return 0 if result["counts"].get("samples", 0) else 1


if __name__ == "__main__":
    raise SystemExit(main())
