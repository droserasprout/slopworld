"""Measure wall time for build and preparation stages, including failed work."""

import time
from collections.abc import Iterator
from contextlib import contextmanager


class Timings:
    def __init__(self) -> None:
        self.started = time.perf_counter()
        self.rows: list[tuple[str, float, str]] = []

    @contextmanager
    def step(self, name: str) -> Iterator[None]:
        started = time.perf_counter()
        status = 'done'
        try:
            yield
        except BaseException:
            status = 'failed'
            raise
        finally:
            self.rows.append((name, time.perf_counter() - started, status))

    def summary(self, title: str) -> None:
        print(f'\n{title} (wall time):', flush=True)
        for name, seconds, status in self.rows:
            print(f'  {name}: {seconds:.2f}s ({status})', flush=True)
        print(f'  Total: {time.perf_counter() - self.started:.2f}s', flush=True)
