"""Automatic history/typing suite and manual desktop input phases. Run on the graphical host."""
import argparse
import datetime
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import results_data as data
from terminal_input_backend import Backend, TEXT
from terminal_input_history import command as history_command, history_limit
from terminal_input_metrics import measurement_status, paint_damage, paint_reasons, paint_timings, performance as parse_performance

PHASES = (("history", 60, "Big history tab: place the pointer over terminal text"),
          ("typing", 10, "Text tab: empty prompt, cursor at end, no selection; focus terminal text"),
          ("htop", 60, "htop tab: focus terminal text and leave the pointer over it"))
SECONDS = 60


def default_trace_log():
    profile = (os.environ.get("PROFILE") or os.environ.get("SLOPCAR_PROFILE")
               or os.environ.get("SLOPWORLD_PROFILE"))
    if profile:
        data_dir = Path(profile)
    else:
        data_home = Path(os.environ.get("XDG_DATA_HOME") or Path.home() / ".local/share")
        data_dir = data_home / "slopworld" / "profile"
    return data_dir / "SlopWorld-trace.log"


def event(phase, index, rate):
    # Reverse every second, so long runs don't simply sit at one boundary.
    up = (index // rate) % 2 == 0
    if phase == "typing":
        return "paste"
    if phase == "htop" and index % 3 == 2:
        return "Up" if up else "Down"
    return "wheel_up" if up else "wheel_down"


class Capture:
    def __init__(self, path, output):
        self.path = path
        self.source = path.open()
        self.original = os.fstat(self.source.fileno())
        self.source.seek(0, 2)
        self.output = output
        self.partial = ""
        self.paste_starts = 0
        self.last_paste_at = None

    def pump(self, perf=True):
        current = self.path.stat()
        if ((current.st_dev, current.st_ino) != (self.original.st_dev, self.original.st_ino)
                or current.st_size < self.source.tell()):
            raise RuntimeError("The trace file was replaced or truncated. A game restart invalidates this phase.")
        data = self.partial + self.source.read()
        lines = data.split("\n")
        self.partial = lines.pop()
        for line in lines:
            if "[SlopWorld] latency " in line and "status=start " in line and "kind=paste" in line:
                self.paste_starts += 1
                self.last_paste_at = time.monotonic()
            if "[SlopWorld] latency " in line or (perf and "[SlopWorld] perf " in line):
                self.output.write(line + "\n")


def drive(phase, rate, send, pump, records, clock=time.monotonic, sleep=time.sleep):
    start = clock()
    slip = 0.0
    missed = 0
    for index in range(SECONDS * rate):
        target = start + index / rate + slip
        sleep(max(0, target - clock()))
        actual = clock()
        late = actual - target
        action = event(phase, index, rate)
        row = {"index": index, "action": action, "nominal_s": index / rate,
               "scheduled_s": target - start, "issued_s": actual - start,
               "lateness_us": round(late * 1e6)}
        records.append(row)
        # Preserve event count and spacing across occasional OS scheduling stalls.
        # Rebase future deadlines instead of catching up. Duration drift is explicit
        # and bounded to 1% of the nominal window. Large stalls still invalidate it.
        if late >= 1 / rate:
            if late > 0.1 or slip + late > SECONDS * 0.01:
                row["status"] = "not_sent_deadline"
                raise RuntimeError(f"Excessive scheduling delay at event {index}: {late:.6f}s, prior slip {slip:.6f}s")
            slip += late
            missed += 1
            row["schedule_shift_s"] = late
        try:
            send(action)
        except BaseException:
            row["status"] = "failed_or_delivery_unknown"
            raise
        row.update(status="sent", returned_s=clock() - start)
        pump()
    sleep(max(0, start + SECONDS + slip - clock()))
    elapsed = clock() - start
    if elapsed > SECONDS * 1.01:
        raise RuntimeError("Measurement window exceeded the 1% duration tolerance")
    pump()
    return {"actual_seconds": elapsed, "schedule_slip_seconds": slip, "rebased_deadlines": missed}


def load_reporter():
    spec = importlib.util.spec_from_file_location("latency_summary", Path(__file__).with_name("latency-summary.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def write_performance_summary(log, output, mode='x'):
    root = Path(__file__).resolve().parents[2]
    with output.open(mode) as report:
        subprocess.run([sys.executable, '-m', 'tools.trace.summary', str(log.resolve())],
                       cwd=root, stdout=report, check=True)


def revision():
    result = subprocess.run(['git', 'rev-parse', '--short', 'HEAD'],
                            capture_output=True, text=True, check=False)
    return result.stdout.strip() if result.returncode == 0 else 'unknown'


def record_phase(directory, phase, metadata, result, performance_text):
    """Save derived CSV rows while retaining the raw trace and JSON for audit."""
    state = measurement_status(metadata, result)
    contexts = parse_performance(performance_text)
    data.add_metadata(directory, 'terminal', phase, {
        key: metadata.get(key) for key in ('runner_revision', 'backend', 'rate', 'multiplier',
            'started', 'status', 'measurement_status', 'sent_events', 'expected_events',
            'actual_seconds', 'schedule_slip_seconds', 'rebased_deadlines',
            'observed_paste_requests', 'text_fixture', 'transport', 'window', 'error')}
        | {'observed_contexts': ';'.join(sorted({context.split(' windows=', 1)[0]
             for context, *_ in contexts})),
           'scroll_sources': ','.join(sorted(result.get('scroll_sources', {}))) if phase == 'history' else None})
    data.add_outcomes(directory, 'terminal', phase, '', 1, result.get('counts', {}))
    for kind, lanes in result.get('metrics', {}).items():
        for lane, stats in lanes.items():
            for stat in ('n', 'p50', 'p95', 'p99', 'max'):
                if stat in stats:
                    data.add_metric(directory, 'terminal', phase, kind, 'latency/' + lane,
                                    stat, 'count' if stat == 'n' else 'us', 1, stats[stat], state)
    perf_status = 'complete' if metadata.get('status') in ('complete', 'complete_with_slip') else 'invalid'
    record_performance_metrics(directory, phase, contexts, perf_status)
    record_paint_reasons(directory, phase, performance_text, perf_status)


def record_performance_metrics(directory, phase, contexts, status='complete'):
    """Add available client work counters, including those added after a saved run."""
    existing = {(row['case'], row['metric']) for row in data.read(directory / 'metrics.csv')
                if row['suite'] == 'terminal' and row['phase'] == phase}
    for context, fps, gc, terminal, ws, messages in contexts:
        context = context.split(' windows=', 1)[0]
        for name, value, unit, stat in (('fps', fps, 'frames/s', 'mean'),
                                        ('gc0', gc, 'count', 'mean'),
                                        ('terminal_work', terminal, 'ms/update', 'mean'),
                                        ('ws_work', ws, 'ms/update', 'mean'),
                                        ('ws_messages', messages, 'count', 'total')):
            if value != '—' and (context, name) not in existing:
                data.add_metric(directory, 'terminal', phase, context, name, stat, unit, 1, value,
                                status)
                existing.add((context, name))


def record_paint_reasons(directory, phase, performance_text, status='complete'):
    """Backfill paint counts in older runs without duplicating CSV measurements."""
    existing = {(row['case'], row['metric']) for row in data.read(directory / 'metrics.csv')
                if row['suite'] == 'terminal' and row['phase'] == phase}
    for context, name, count in paint_reasons(performance_text):
        metric = 'paint/' + name
        if (context, metric) not in existing:
            data.add_metric(directory, 'terminal', phase, context, metric, 'total', 'count',
                            1, count, status)
            existing.add((context, metric))
    for context, band, calls, rows in paint_damage(performance_text):
        for metric, value in ((f'paint/damage-{band}-frames', calls),
                              (f'paint/damage-{band}-rows', rows)):
            if (context, metric) not in existing:
                data.add_metric(directory, 'terminal', phase, context, metric, 'total', 'count',
                                1, value, status)
                existing.add((context, metric))
    for context, name, calls, mean_ms in paint_timings(performance_text):
        for metric, stat, unit, value in ((f'paint/{name}-calls', 'total', 'count', calls),
                                          (f'paint/{name}-time', 'mean', 'ms/call', mean_ms)):
            if (context, metric) not in existing:
                data.add_metric(directory, 'terminal', phase, context, metric, stat, unit,
                                1, value, status)
                existing.add((context, metric))


def export_existing(args):
    if (args.results / 'run.csv').exists():
        return
    for phase, _, _ in PHASES:
        meta = args.output / f'{phase}-events.json'
        result = args.output / f'{phase}-latency.json'
        if meta.exists() and result.exists():
            summary = args.output / f'{phase}-performance.txt'
            record_phase(args.results, phase, json.loads(meta.read_text()),
                         json.loads(result.read_text()), summary.read_text() if summary.exists() else '')


def show_report(args):
    export_existing(args)
    return data.write_report(args.results, args.report)


def run_phase(args, phase, rate, backend, reporter, close_backend=True):
    name = backend.name
    rows = []
    metadata = {"runner_revision": revision(), "phase": phase, "backend": name, "seconds": SECONDS, "rate": rate,
                "expected_events": SECONDS * rate, "multiplier": args.multiplier,
                "prepare_seconds": args.prepare_seconds,
                "text_fixture": TEXT if phase == "typing" else None,
                "transport": "persistent_ydotool_socket" if name == "ydotool" else "persistent_XTest", "started": datetime.datetime.now().astimezone().isoformat(),
                "window": backend.window, "status": "incomplete", "events": rows,
                "injection_clock": "host_monotonic_relative_seconds",
                "correlation": "injection events are not mapped one-to-one to client request IDs"}
    log = args.output / f"{phase}.log"
    error = None
    with log.open("x") as output:
        capture = Capture(args.log, output)
        started = time.monotonic()
        def pump():
            capture.pump()
            if phase == "typing" and time.monotonic() - (capture.last_paste_at or started) > 1:
                raise RuntimeError("No new SlopWorld paste requests arrived for one second. Stopping injection.")
        try:
            print(f"Running {phase} for 60 seconds", flush=True)
            metadata.update(drive(phase, rate, backend.send, pump, rows, time.monotonic, time.sleep))
            metadata["status"] = "complete_with_slip" if metadata["rebased_deadlines"] else "complete"
        except (Exception, KeyboardInterrupt) as exc:
            metadata.update(status="invalid", error=str(exc) or "interrupted")
            error = exc
        try:
            # Also drain after failed injection, so queued final events are not lost.
            print("Input finished. Keep this tab open for 11 seconds to drain traces.", flush=True)
            deadline = time.monotonic() + 11
            while time.monotonic() < deadline:
                capture.pump(perf=False)
                time.sleep(0.05)
            capture.pump(perf=False)
        except (Exception, KeyboardInterrupt) as exc:
            metadata.update(status="invalid", error=str(exc) or "interrupted")
            error = exc
        finally:
            capture.source.close()
            if close_backend:
                backend.close()
            metadata["observed_paste_requests"] = capture.paste_starts
            metadata["sent_events"] = sum(row.get("status") == "sent" for row in rows)
            jitter = [row["lateness_us"] for row in rows]
            metadata["injection_lateness_us"] = {f"p{p}": reporter.percentile(jitter, p) for p in (50, 95, 99)} if jitter else {}
            (args.output / f"{phase}-events.json").write_text(json.dumps(metadata, indent=2) + "\n")
    result = reporter.summarize(log.read_text().splitlines())
    (args.output / f"{phase}-latency.json").write_text(json.dumps(result, indent=2) + "\n")
    write_performance_summary(log, args.output / f"{phase}-performance.txt")
    if not error and "[SlopWorld] perf " not in log.read_text():
        error = RuntimeError("No performance records; enable SLOPWORLD_DEBUG=1 before repeating")
    if not error and phase == "typing" and metadata["observed_paste_requests"] < metadata["sent_events"] * 0.9:
        error = RuntimeError("Fewer than 90% of injected pastes reached the client; this is not a valid text workload")
    if not error and not result["counts"].get("samples"):
        error = RuntimeError("No input traces completed. Check the mod version, focus, and daemon connection.")
    if not error and phase == "history" and "history_scroll" not in result["metrics"]:
        error = RuntimeError("Missing history_scroll measurements; restart the updated mod and select history")
    if error:
        metadata.update(status="invalid", error=str(error) or "interrupted")
    metadata["measurement_status"] = measurement_status(metadata, result)
    (args.output / f"{phase}-events.json").write_text(json.dumps(metadata, indent=2) + "\n")
    record_phase(getattr(args, 'results', args.output), phase, metadata, result,
                 (args.output / f"{phase}-performance.txt").read_text())
    print(f"{phase}: {metadata['status']}, measurement={metadata['measurement_status']}, {metadata['sent_events']} sent; trace outcomes: {result['counts']}")
    if error:
        raise RuntimeError(f"Phase invalid: {metadata.get('error')}. Partial results saved in {args.output}") from error


def countdown(seconds):
    for remaining in range(seconds, 0, -1):
        print(f"Focus prepared window: {remaining}s", flush=True)
        time.sleep(1)


def wait_until(predicate, backend, timeout, message, pump=lambda: None):
    deadline = time.monotonic() + timeout
    while True:
        backend.check_focus()
        pump()
        if predicate():
            return
        if time.monotonic() >= deadline:
            raise RuntimeError(message)
        time.sleep(.05)


def prepare_history(args, backend):
    info = {'status': 'incomplete', 'target_history_lines': history_limit()}
    temporary = getattr(args, 'results', args.output) / 'tmp'
    temporary.mkdir(parents=True, exist_ok=True)
    try:
        with tempfile.TemporaryDirectory(prefix='slopworld-history-', dir=temporary) as directory:
            status = Path(directory) / 'done.json'
            backend.prepare_text(history_command(status, info['target_history_lines']))
            phase = getattr(args, 'phase', 'suite')
            work = ('scroll' if phase == 'history' else
                    'append Unicode' if phase == 'typing' else 'scroll and append Unicode')
            input('Open an EMPTY LOCAL HOST SHELL prompt in SlopWorld, with no selection. '
                  f'The tool will execute a bounded history filler, then {work}. '
                  'Press Enter here, then focus terminal text and keep the pointer over it: ')
            countdown(args.prepare_seconds)
            backend.connect()
            with (args.output / 'setup.log').open('x') as output:
                capture = Capture(args.log, output)
                try:
                    backend.send('paste')
                    # Clipboard HTTP is asynchronous. Do not press Enter until
                    # the paste request has actually reached the ordered socket.
                    wait_until(lambda: capture.paste_starts > 0, backend, 5,
                               'Setup command was not pasted; no Enter sent', capture.pump)
                    backend.send('Enter')
                    print('Filling scrollback with bounded alphanumeric output...', flush=True)
                    wait_until(status.exists, backend, 90,
                               'History setup did not finish; requires python3 and a local host tmux shell', capture.pump)
                    info.update(json.loads(status.read_text()))
                    if info['status'] != 'complete':
                        raise RuntimeError(info.get('error', 'History preparation failed'))
                    # Allow output capture and all setup-input traces to settle
                    # outside the two measured windows (trace TTL is ten seconds).
                    print('History ready. Settling for 11 seconds before measurement.', flush=True)
                    ready = time.monotonic() + 11
                    wait_until(lambda: time.monotonic() >= ready, backend, 12,
                               'History settling timed out', capture.pump)
                finally:
                    capture.source.close()
    except (Exception, KeyboardInterrupt) as error:
        info.update(status='invalid', error=str(error) or 'interrupted')
        raise
    finally:
        (args.output / 'setup.json').write_text(json.dumps(info, indent=2) + '\n')
        data.add_metadata(getattr(args, 'results', args.output), 'terminal', 'setup',
                          {**{key: value for key, value in info.items() if key != 'bytes_written'},
                           'workflow': 'typing appends in the same history-filled tab'})
        try:
            temporary.rmdir()
        except OSError:
            pass


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--backend', choices=('auto', 'xdotool', 'ydotool'), default='auto')
    parser.add_argument('--phase', choices=('suite', 'all', 'history', 'typing', 'htop'), default='suite',
                        help='suite: one empty host shell, automatic history then typing; individual phases are manually prepared unless history or typing uses --fill-history')
    parser.add_argument('--fill-history', action='store_true',
                        help='with --phase history or typing, fill scrollback in one empty local host shell before measuring')
    parser.add_argument('--prepare-seconds', type=int, default=10)
    parser.add_argument('--multiplier', type=int, choices=(5, 10), default=5)
    parser.add_argument('--log', type=Path, default=default_trace_log(),
                        help='SlopWorld-trace.log (defaults to the launcher profile path)')
    parser.add_argument('--run', help='directory name under bench/results (default: UTC timestamp)')
    parser.add_argument('--output', type=Path, help='raw directory override')
    parser.add_argument('--report', type=Path, help='Markdown report path (default: run/report.md)')
    args = parser.parse_args()
    if args.run and args.output:
        parser.error('--run and --output are alternatives')
    if args.output is None:
        args.results = data.run_directory(args.run or data.default_run())
        args.output = args.results / 'raw' / 'terminal'
        args.report = args.report or args.results / 'report.md'
    else:
        args.results = args.output
        args.report = args.report or args.output.with_suffix('.md')
    if args.report.suffix != '.md':
        parser.error('--report must end in .md')
    if args.prepare_seconds < 1:
        parser.error('prepare-seconds must be positive')
    if args.fill_history and args.phase not in ('history', 'typing'):
        parser.error('--fill-history requires --phase history or typing')
    automatic = args.phase == 'suite' or args.fill_history
    phases = [p for p in PHASES if args.phase in ('all', p[0]) or
              (args.phase == 'suite' and p[0] != 'htop')]
    for phase, _, _ in phases:
        for suffix in ('.log', '-events.json', '-latency.json', '-performance.txt'):
            if (args.output / (phase + suffix)).exists():
                parser.error(f'{phase} already has artifacts in {args.output}; use a new directory to repeat it')
    if automatic and any((args.output / name).exists() for name in ('setup.log', 'setup.json')):
        parser.error('Preparation artifacts already exist. Use a new results directory.')
    if not args.log or not args.log.is_file():
        parser.error('trace file missing; restart updated game with SLOPWORLD_DEBUG=1')
    name = args.backend
    if name == 'auto':
        name = 'ydotool' if os.environ.get('WAYLAND_DISPLAY') or os.environ.get('XDG_SESSION_TYPE') == 'wayland' else 'xdotool'
    backend = Backend(name)
    backend.preflight(text=automatic or any(p[0] == 'typing' for p in phases))
    args.output.mkdir(parents=True, exist_ok=True)
    reporter = load_reporter()
    print(f'Backend: {name}. {len(phases)} phases, 60 seconds each plus an 11-second drain.')
    print('Keep SlopWorld focused. Keep the pointer over terminal text. Switching windows stops input.')
    if any(p[0] == 'typing' for p in phases):
        print('Clipboard is replaced. Measured typing appends Unicode without deletion or Enter.')
    elif automatic:
        print('Clipboard is replaced with the bounded history filler command.')
    try:
        if automatic:
            prepare_history(args, backend)
        for phase, base_rate, instruction in phases:
            rate = base_rate * args.multiplier
            if not automatic:
                input(f'\n{phase}: {instruction}. {rate}/s, {SECONDS * rate} events. Press Enter when ready.')
            if phase == 'typing':
                backend.prepare_text()
            if not automatic:
                countdown(args.prepare_seconds)
                backend.connect()
            run_phase(args, phase, rate, backend, reporter, close_backend=not automatic)
    finally:
        if automatic:
            backend.close()
        print(f'Report: {show_report(args)}')
    print(f'Results: {args.output}. Latency ends before presentation; see the report for measurement validity.')


if __name__ == '__main__':
    try:
        main()
    except (Exception, KeyboardInterrupt) as error:
        print(f'Stopped: {error}', file=sys.stderr)
        raise SystemExit(1)
