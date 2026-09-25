"""Interpret terminal trace outcomes and performance summary contexts."""

import re

CENSORED = ('overflow', 'timeout', 'unfinished', 'completion_without_start',
            'malformed', 'invalid_sample', 'dropped_records', 'missed_frame_end',
            'disconnected', 'cancelled')


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
    # Keep contexts separate and honor trace-summary's transition-window exclusion.
    rows = []
    for chunk in re.split(r'(?=eco=)', text)[1:]:
        context = chunk.splitlines()[0]
        fps = re.search(r'mean reported FPS=([\d.]+)', chunk)
        gc = re.search(r'gen0 collections=(\d+)', chunk)
        terminal = re.search(r'^  terminal-window: ([\d.]+)', chunk, re.M)
        rows.append((context, fps[1] if fps else '—', gc[1] if gc else '—',
                     terminal[1] if terminal else '—'))
    return rows


def paint_reasons(text):
    """Read debug paint counts from each context in a trace-summary output."""
    names = ('broad-repaints', 'skipped-revisions', 'broad-rows', 'missing-damage')
    for chunk in re.split(r'(?=eco=)', text)[1:]:
        context = chunk.splitlines()[0].split(' windows=', 1)[0]
        counts = {}
        for name in names:
            match = re.search(r'^  terminal-cache-' + name + r': (\d+) calls$', chunk, re.M)
            if match:
                counts[name] = int(match[1])
        # Older captures had only the broad total; do not invent reason zeros for them.
        if any(name in counts for name in names[1:]):
            for name in names[1:]:
                counts.setdefault(name, 0)
        for name, count in counts.items():
            yield context, name, count


def paint_damage(text):
    """Yield frame and changed-row totals for each measured damage band."""
    for chunk in re.split(r'(?=eco=)', text)[1:]:
        context = chunk.splitlines()[0].split(' windows=', 1)[0]
        for band in ('0-49', '50-74', '75-99', '100'):
            match = re.search(r'^  terminal-cache-damage-' + band +
                              r': (\d+) calls, (\d+) rows$', chunk, re.M)
            if match:
                yield context, band, int(match[1]), int(match[2])
