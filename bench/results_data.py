"""Shared, append-only benchmark CSVs and reports. Raw artifacts stay beside them."""

import csv
import datetime as dt
from pathlib import Path
import re
from statistics import median

ROOT = Path(__file__).resolve().parent / "results"
METRIC_FIELDS = ("suite", "phase", "case", "metric", "stat", "unit", "repeat", "value", "status")
OUTCOME_FIELDS = ("suite", "phase", "case", "outcome", "repeat", "count")
META_FIELDS = ("suite", "phase", "key", "value")


def run_directory(name):
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", name or ""):
        raise ValueError("BENCH_RUN must contain only letters, digits, dots, underscores, and dashes")
    return ROOT / name


def default_run():
    return dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")


def append(path, fields, rows):
    rows = list(rows)
    if not rows:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields)
        if stream.tell() == 0:
            writer.writeheader()
        for row in rows:
            writer.writerow({field: row.get(field, "") for field in fields})


def read(path):
    if not path.exists():
        return []
    with path.open(newline="", encoding="utf-8") as stream:
        return list(csv.DictReader(stream))


def add_metric(directory, suite, phase, case, metric, stat, unit, repeat, value, status="complete"):
    append(directory / "metrics.csv", METRIC_FIELDS, [{"suite": suite, "phase": phase,
           "case": case, "metric": metric, "stat": stat, "unit": unit,
           "repeat": repeat, "value": value, "status": status}])


def add_outcomes(directory, suite, phase, case, repeat, counts):
    append(directory / "outcomes.csv", OUTCOME_FIELDS,
           ({"suite": suite, "phase": phase, "case": case, "outcome": key,
             "repeat": repeat, "count": value} for key, value in counts.items()))


def add_metadata(directory, suite, phase, values):
    append(directory / "run.csv", META_FIELDS,
           ({"suite": suite, "phase": phase, "key": key, "value": value}
            for key, value in values.items() if value is not None))


def aggregates(directory):
    grouped = {}
    for row in read(directory / "metrics.csv"):
        key = tuple(row[field] for field in ("suite", "phase", "case", "metric", "stat", "unit"))
        entry = grouped.setdefault(key, {"values": [], "statuses": set()})
        entry["statuses"].add(row["status"])
        entry["values"].append(float(row["value"]))
    result = {}
    for key, entry in grouped.items():
        values = entry["values"]
        result[key] = (median(values), min(values), max(values), len(values),
                       ", ".join(sorted(entry["statuses"])))
    return result


def workload(directory):
    fields = {}
    for row in read(directory / "run.csv"):
        if row["suite"] == "terminal" and row["key"] in ("backend", "rate", "multiplier", "observed_contexts"):
            fields[(row["phase"], row["key"])] = row["value"]
    return fields


def gamefree_environment(directory):
    return {row["key"]: row["value"] for row in read(directory / "run.csv")
            if row["suite"] == "gamefree" and row["phase"] == ""}


def display(value, unit, suite, metric):
    if unit == "count" or metric == "wire_size":
        return f"{value:.0f} {unit}"
    if suite == "terminal" and metric.startswith("latency/") and unit == "us":
        return f"{value / 1000:.3f} ms"
    return f"{value:.3f} {unit}"


def baseline_display(item, suite, metric, stat, unit):
    if suite == "terminal" and metric.startswith("latency/") and stat != "n" and item[4] != "complete":
        return "withheld"
    return display(item[0], unit, suite, metric)


def render(directory, baseline=None, mode="absolute", latest=False):
    if mode not in ("absolute", "relative"):
        raise ValueError("report mode must be absolute or relative")
    data = aggregates(directory)
    outcomes = read(directory / "outcomes.csv")
    metadata = read(directory / "run.csv")
    if not data and not outcomes and not metadata:
        raise ValueError(f"no benchmark CSV data in {directory}")
    old = aggregates(baseline) if baseline else {}
    if mode == "relative" and not baseline:
        raise ValueError("relative report needs a baseline run")
    if baseline and not old:
        raise ValueError(f"no metrics.csv measurements in baseline {baseline}")
    current_workload = workload(directory)
    old_workload = workload(baseline) if baseline else {}
    current_environment = gamefree_environment(directory)
    old_environment = gamefree_environment(baseline) if baseline else {}
    lines = ["# Latest benchmark report" if latest else "# Benchmark report"]
    if not latest:
        lines += ["", f"Run: `{directory.name}`."]
    if baseline and not latest:
        lines.append(f"Baseline: `{baseline.name}`. Change is (run / baseline − 1) × 100%; negative means a smaller value.")
    elif baseline:
        lines.append("Change is (run / baseline − 1) × 100%; negative means a smaller value.")
    lines += ["", "Median is across repetitions of each reported statistic; brackets are the min–max repetition range.",
              "Terminal latency percentiles with partial or censored observations are withheld.", ""]
    if any(key[0] == "terminal" for key in data):
        lines.insert(-1, "Terminal latency ends at Unity frame end before presentation; it correlates the next changed frame, not verified echo. History samples are consumed movements, not injected wheel ticks.")
    if any(key[0] in ("daemon", "mod", "ipc") for key in data):
        lines.insert(-1, "Game-free p50/p95 values describe warmed benchmark batches. They do not measure game FPS or input-to-display latency.")
    header = "| Suite / phase / case / metric | Statistic | Value [range] | n | Status |"
    separator = "| --- | --- | ---: | ---: | --- |"
    if baseline and mode == "absolute":
        header = "| Suite / phase / case / metric | Statistic | Baseline | Run | Change | n | Status |"
        separator = "| --- | --- | ---: | ---: | ---: | ---: | --- |"
    elif baseline:
        header = "| Suite / phase / case / metric | Statistic | Change | n | Status |"
        separator = "| --- | --- | ---: | ---: | --- |"
    lines += [header, separator]
    for key in sorted(set(data) | set(old)):
        suite, phase, case, metric, stat, unit = key
        item = data.get(key)
        if item is None:
            label = " / ".join(part for part in (suite, phase, case, metric) if part).replace("|", "\\|")
            prior = old[key]
            if mode == "relative":
                lines.append(f"| {label} | {stat} | missing | 0 | missing |")
            else:
                lines.append(f"| {label} | {stat} | {baseline_display(prior, suite, metric, stat, unit)} | — | — | 0 | missing |")
            continue
        value, low, high, count, status = item
        label = " / ".join(part for part in (suite, phase, case, metric) if part).replace("|", "\\|")
        converted = (lambda number: number / 1000) if suite == "terminal" and metric.startswith("latency/") and unit == "us" else (lambda number: number)
        display_unit = "ms" if suite == "terminal" and metric.startswith("latency/") and unit == "us" else unit
        precision = 0 if unit == "count" or metric == "wire_size" else 3
        shown = f"{converted(value):.{precision}f} [{converted(low):.{precision}f}–{converted(high):.{precision}f}] {display_unit}"
        if suite == "terminal" and metric.startswith("latency/") and stat != "n" and status != "complete":
            shown = "withheld"
        if baseline:
            if suite == "terminal":
                compatible = all(
                    current_workload.get((phase, field)) == old_workload.get((phase, field))
                    for field in ("backend", "rate", "multiplier", "observed_contexts"))
            else:
                compatible = all(
                    current_environment.get(field) and
                    current_environment[field] == old_environment.get(field)
                    for field in ("build", "system", "machine", "cpu_count", "host"))
            prior = old.get(key) if compatible else None
            prior_shown = "—" if prior is None else baseline_display(prior, suite, metric, stat, unit)
            change = "incompatible workload" if not compatible else (
                "—" if prior is None or prior[0] == 0 or shown == "withheld" or
                prior[4] != "complete" or status != "complete"
                else f"{(value / prior[0] - 1) * 100:+.2f}%")
            if mode == "relative":
                lines.append(f"| {label} | {stat} | {change} | {count} | {status} |")
            else:
                lines.append(f"| {label} | {stat} | {prior_shown} | {shown} | {change} | {count} | {status} |")
        else:
            lines.append(f"| {label} | {stat} | {shown} | {count} | {status} |")
    if outcomes:
        lines += ["", "## Outcomes", "", "| Suite / phase / case | Repeat | Outcome | Count |", "| --- | ---: | --- | ---: |"]
        for row in sorted(outcomes, key=lambda r: (r["suite"], r["phase"], r["case"], r["outcome"])):
            label = " / ".join(part for part in (row["suite"], row["phase"], row["case"]) if part)
            lines.append(f"| {label} | {row['repeat']} | {row['outcome']} | {row['count']} |")
    if metadata:
        lines += ["", "## Run details", "", "| Suite / phase | Field | Value |", "| --- | --- | --- |"]
        for row in metadata:
            if latest and row["key"] == "started":
                continue
            label = " / ".join(part for part in (row["suite"], row["phase"]) if part)
            value = row["value"].replace("|", "\\|").replace("\n", " ")
            lines.append(f"| {label} | {row['key']} | {value} |")
    lines.append("")
    return "\n".join(lines)


def write_report(directory, output, baseline=None, mode="absolute", latest=False):
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".tmp")
    temporary.write_text(render(directory, baseline, mode, latest), encoding="utf-8")
    temporary.replace(output)
    return output
