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
