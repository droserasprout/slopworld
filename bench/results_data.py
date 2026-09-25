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


def aggregates_rows(rows):
    grouped = {}
    for row in rows:
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


def aggregates(directory):
    return aggregates_rows(read(directory / "metrics.csv"))


def combined_run(directory, fallback):
    """Use each current suite/phase as a unit; fill only absent phases from fallback."""
    filenames = ("metrics.csv", "outcomes.csv", "run.csv")
    current = {name: read(directory / name) for name in filenames}
    prior = {name: read(fallback / name) for name in filenames}
    if not any(prior.values()):
        raise ValueError(f"no benchmark CSV data in fallback {fallback}")
    owned = {(row["suite"], row["phase"]) for rows in current.values() for row in rows}
    records = {name: [row for row in prior[name]
                      if (row["suite"], row["phase"]) not in owned] + current[name]
               for name in filenames}
    sources = {(row["suite"], row["phase"]): "current" for rows in current.values() for row in rows}
    for rows in prior.values():
        for row in rows:
            sources.setdefault((row["suite"], row["phase"]), "saved fallback")
    return records, sources


def workload(directory):
    fields = {}
    for row in read(directory / "run.csv"):
        if row["suite"] == "terminal" and row["key"] in ("backend", "rate", "multiplier", "observed_contexts"):
            fields[(row["phase"], row["key"])] = row["value"]
    return fields


def terminal_context_without_session_count(value):
    """Session inventory is recorded, but is not part of the active-pane workload."""
    return re.sub(r"(?<!\S)sessions=\d+\b", "sessions=*", value)


def terminal_context_display(value):
    return re.sub(r"(?<!\S)sessions=\d+\s*", "", value).strip()


def comparison_key(key):
    suite, phase, case, metric, stat, unit = key
    if suite == "terminal":
        case = terminal_context_without_session_count(case)
    return suite, phase, case, metric, stat, unit


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


def metadata_display(key, raw):
    # Keep exact CSV values for auditing; only the report rounds elapsed seconds.
    if key in ("actual_seconds", "schedule_slip_seconds"):
        try:
            return f"{float(raw):.3f}"
        except ValueError:
            pass
    return raw.replace("|", "\\|").replace("\n", " ")


def render(directory, baseline=None, mode="absolute", latest=False, fallback=None):
    if mode not in ("absolute", "relative"):
        raise ValueError("report mode must be absolute or relative")
    if fallback and (not latest or baseline or mode != "absolute"):
        raise ValueError("fallback requires a latest absolute report without a baseline")
    if fallback:
        records, sources = combined_run(directory, fallback)
        data = aggregates_rows(records["metrics.csv"])
        outcomes = records["outcomes.csv"]
        metadata = records["run.csv"]
    else:
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
    old_by_comparison = {}
    for key in old:
        old_by_comparison.setdefault(comparison_key(key), []).append(key)
    current_comparison_keys = {comparison_key(key) for key in data}
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
        if baseline:
            lines.insert(-1, "Terminal comparisons use the active viewport context and input settings. Background session load can still affect results.")
    if any(key[0] in ("daemon", "mod", "ipc") for key in data):
        lines.insert(-1, "Game-free p50/p95 values describe warmed benchmark batches. They do not measure game FPS or input-to-display latency.")
    if fallback:
        lines += ["This snapshot combines separately captured phases. Each metric uses one source; current phases replace the same phases in the saved fallback run.",
                  "", "| Suite / phase | Source |", "| --- | --- |"]
        for suite, phase in sorted(sources):
            label = " / ".join(part for part in (suite, phase) if part)
            lines.append(f"| {label} | {sources[(suite, phase)]} |")
        lines.append("")
    header = "| Suite / phase / case / metric | Statistic | Value [range] | n | Status |"
    separator = "| --- | --- | ---: | ---: | --- |"
    if baseline and mode == "absolute":
        header = "| Suite / phase / case / metric | Statistic | Baseline | Run | Change | n | Status |"
        separator = "| --- | --- | ---: | ---: | ---: | ---: | --- |"
    elif baseline:
        header = "| Suite / phase / case / metric | Statistic | Change | n | Status |"
        separator = "| --- | --- | ---: | ---: | --- |"
    lines += [header, separator]
    keys = set(data) | {key for key in old if comparison_key(key) not in current_comparison_keys}
    for key in sorted(keys):
        suite, phase, case, metric, stat, unit = key
        item = data.get(key)
        if item is None:
            shown_case = terminal_context_display(case) if suite == "terminal" else case
            label = " / ".join(part for part in (suite, phase, shown_case, metric) if part).replace("|", "\\|")
            prior = old[key]
            if mode == "relative":
                lines.append(f"| {label} | {stat} | missing | 0 | missing |")
            else:
                lines.append(f"| {label} | {stat} | {baseline_display(prior, suite, metric, stat, unit)} | — | — | 0 | missing |")
            continue
        value, low, high, count, status = item
        shown_case = terminal_context_display(case) if suite == "terminal" else case
        label = " / ".join(part for part in (suite, phase, shown_case, metric) if part).replace("|", "\\|")
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
                    for field in ("backend", "rate", "multiplier")) and (
                    terminal_context_without_session_count(current_workload.get((phase, "observed_contexts"), "")) ==
                    terminal_context_without_session_count(old_workload.get((phase, "observed_contexts"), "")))
            else:
                compatible = all(
                    current_environment.get(field) and
                    current_environment[field] == old_environment.get(field)
                    for field in ("build", "system", "machine", "cpu_count", "host"))
            matches = old_by_comparison.get(comparison_key(key), [])
            prior = old[matches[0]] if compatible and len(matches) == 1 else None
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
            case = terminal_context_display(row["case"]) if row["suite"] == "terminal" else row["case"]
            label = " / ".join(part for part in (row["suite"], row["phase"], case) if part)
            lines.append(f"| {label} | {row['repeat']} | {row['outcome']} | {row['count']} |")
    if metadata:
        lines += ["", "## Run details", "", "| Suite / phase | Field | Value |", "| --- | --- | --- |"]
        for row in metadata:
            if latest and row["key"] == "started":
                continue
            label = " / ".join(part for part in (row["suite"], row["phase"]) if part)
            raw = terminal_context_display(row["value"]) if row["suite"] == "terminal" and row["key"] == "observed_contexts" else row["value"]
            value = metadata_display(row["key"], raw)
            lines.append(f"| {label} | {row['key']} | {value} |")
    lines.append("")
    return "\n".join(lines)


def write_report(directory, output, baseline=None, mode="absolute", latest=False, fallback=None):
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".tmp")
    temporary.write_text(render(directory, baseline, mode, latest, fallback), encoding="utf-8")
    temporary.replace(output)
    return output
