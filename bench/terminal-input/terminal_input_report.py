"""Markdown summary of desktop input artifacts, including censored/failed runs."""
import datetime
import json
import os
import re
from pathlib import Path

PHASES = ('history', 'typing', 'htop')
CENSORED = ('overflow', 'timeout', 'unfinished', 'completion_without_start',
            'malformed', 'dropped_records', 'missed_frame_end', 'disconnected', 'cancelled')


def cell(value):
    return str(value).replace('|', '\\|').replace('\n', ' ')


def measurement_status(metadata, result):
    counts = result.get('counts', {})
    if metadata.get('status') not in ('complete', 'complete_with_slip'):
        return 'invalid'
    if any(counts.get(key, 0) for key in CENSORED):
        return 'censored'
    if not counts.get('samples'):
        return 'unavailable'
    if counts.get('superseded'):
        return 'partial (superseded)'
    return 'complete'


def performance(text):
    # Consume the existing trace-summary output, keeping its context boundaries
    # and first/transition-window exclusion. Do not pool different game states.
    rows = []
    for chunk in re.split(r'(?=eco=)', text)[1:]:
        context = chunk.splitlines()[0]
        fps = re.search(r'mean reported FPS=([\d.]+)', chunk)
        gc = re.search(r'gen0 collections=(\d+)', chunk)
        terminal = re.search(r'^  terminal-window: ([\d.]+)', chunk, re.M)
        rows.append((context, fps[1] if fps else '—', gc[1] if gc else '—',
                     terminal[1] if terminal else '—'))
    return rows


def write_note(directory, output):
    directory, output = Path(directory), Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    link = lambda path: os.path.relpath(path, output.parent).replace(os.sep, '/')
    lines = ['# Terminal input performance suite', '',
             f'Generated: {datetime.datetime.now().astimezone().isoformat(timespec="seconds")}', '',
             'One run per phase. Percentiles are nearest-rank statistics over completed individual '
             'request/movement traces, not batch averages. Injection completion and measurement '
             'completeness are separate. Censored or invalid runs have no headline latency percentiles.', '',
             '| Phase | Injection | Measurement | Sent / expected | Samples | Duration (s) | Slip (ms) |',
             '| --- | --- | --- | ---: | ---: | ---: | ---: |']
    results = []
    for phase in PHASES:
        meta_path = directory / f'{phase}-events.json'
        if not meta_path.exists():
            continue
        metadata = json.loads(meta_path.read_text())
        latency = directory / f'{phase}-latency.json'
        result = json.loads(latency.read_text()) if latency.exists() else {}
        state = measurement_status(metadata, result)
        number = lambda key, scale=1: f'{metadata[key] * scale:.3f}' if key in metadata else '—'
        lines.append(f'| {phase} | {cell(metadata.get("status", "unknown"))} | {state} | '
                     f'{metadata.get("sent_events", 0)} / {metadata.get("expected_events", "?")} | '
                     f'{result.get("counts", {}).get("samples", 0)} | {number("actual_seconds")} | '
                     f'{number("schedule_slip_seconds", 1000)} |')
        results.append((phase, metadata, result, state))
    if not results:
        lines += ['', 'No measured phase artifacts yet.']
    setup = directory / 'setup.json'
    if setup.exists():
        info = json.loads(setup.read_text())
        lines += ['', '## Preparation', '',
                  f'Status: **{cell(info.get("status", "unknown"))}**. '
                  f'Target history: {info.get("target_history_lines", "?")} lines; '
                  f'generated: {info.get("generated_lines", "?")} lines × '
                  f'{info.get("line_width", "?")} alphanumeric columns.',
                  'The bounded /dev/urandom fixture emits only ASCII letters, digits and line separators. '
                  'Preparation and settling are excluded from phase measurements. '
                  'Typing appends to the returned shell prompt in the same history-filled tab.']
        if info.get('error'):
            lines += ['', f'Preparation error: {cell(info["error"])}']
    lines += ['', '## Latency', '',
              '| Phase / kind | n | p50 (ms) | p95 (ms) | p99 (ms) | max (ms) |',
              '| --- | ---: | ---: | ---: | ---: | ---: |']
    for phase, metadata, result, state in results:
        if state != 'complete':
            lines.append(f'| {phase} ({state}; percentiles withheld) | — | — | — | — | — |')
            continue
        for kind, metrics in result.get('metrics', {}).items():
            metric = metrics.get('input_to_frame_end', {})
            values = [f'{metric[key] / 1000:.3f}' if key in metric else '—' for key in ('p50', 'p95', 'p99', 'max')]
            lines.append(f'| {phase} / {cell(kind)} | {metric.get("n", 0)} | ' + ' | '.join(values) + ' |')
    lines += ['', '## Performance by observed context', '',
              '| Phase / context | Mean reported FPS | Gen0 collections | Terminal ms/update |',
              '| --- | ---: | ---: | ---: |']
    for phase, _, _, _ in results:
        path = directory / f'{phase}-performance.txt'
        for context, fps, gc, terminal in performance(path.read_text() if path.exists() else ''):
            lines.append(f'| {phase}: {cell(context)} | {fps} | {gc} | {terminal} |')
    lines += ['', '## Outcomes and artifacts', '']
    for phase, metadata, result, _ in results:
        lines += [f'### {phase}', '',
                  f'- Started: {cell(metadata.get("started", "unknown"))}; backend: '
                  f'{cell(metadata.get("backend", "unknown"))}; rate: {metadata.get("rate", "?")}/s.',
                  f'- Runner revision: `{cell(metadata.get("runner_revision", "not recorded"))}` '
                  '(not proof of the running mod/daemon revision).',
                  '- Outcomes: ' + ', '.join(f'{cell(k)}={v}' for k, v in result.get('counts', {}).items()) + '.',
                  '- Raw artifacts: ' + ', '.join(
                      f'[{suffix}](<{link(directory / (phase + suffix))}>)'
                      for suffix in ('.log', '-events.json', '-latency.json', '-performance.txt')
                      if (directory / (phase + suffix)).exists()) + '.', '']
        jitter = metadata.get('injection_lateness_us', {})
        lines += ['Injector lateness p50/p95/p99 (µs): ' + '/'.join(str(jitter.get(k, '—')) for k in ('p50', 'p95', 'p99')) +
                  f'; deadline rebases: {metadata.get("rebased_deadlines", "—")}.', '']
        if metadata.get('text_fixture'):
            lines += [f'Append fixture: `{cell(metadata["text_fixture"])}`; observed paste requests: {metadata.get("observed_paste_requests", "—")}.', '']
        if metadata.get('error'):
            lines += [f'Error: {cell(metadata["error"])}', '']
    lines += ['## Interpretation limits', '',
              '- Typing starts at client socket handoff, after clipboard retrieval, and ends at Unity frame end before physical presentation.',
              '- History latency covers consumed accumulated movements that changed position, not each injected wheel tick. Deduplicated and no-motion events are not latency samples.',
              '- Input correlation observes the next eligible changed frame, not verified causal echo. Injected events are not individually paired with trace IDs.',
              '- Overflow and timeout censor the distribution; zero logging drops does not make the surviving percentiles representative.',
              '- Coalesced samples and captures without an observed tmux acknowledgement are reported separately; neither alone proves input loss.',
              '- FPS is an average of reported windows, not a frame-time percentile. Context transitions are excluded by trace-summary; work lanes overlap.', '']
    temporary = output.with_name(output.name + '.tmp')
    temporary.write_text('\n'.join(lines), encoding='utf-8')
    temporary.replace(output)
    return output
