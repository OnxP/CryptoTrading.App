"""Minimal rate limiter with injectable clock and sleep (testable without time)."""
from __future__ import annotations

import time
from collections import deque
from typing import Callable


class RateLimiter:
    def __init__(self, max_per_second: float, clock: Callable[[], float] = time.monotonic,
                 sleep: Callable[[float], None] = time.sleep):
        self._max = max_per_second
        self._clock = clock
        self._sleep = sleep
        self._stamps: deque[float] = deque()

    def acquire(self) -> None:
        now = self._clock()
        while self._stamps and now - self._stamps[0] >= 1.0:
            self._stamps.popleft()
        if len(self._stamps) >= self._max:
            wait = 1.0 - (now - self._stamps[0])
            if wait > 0:
                self._sleep(wait)
                now = self._clock()
            while self._stamps and now - self._stamps[0] >= 1.0:
                self._stamps.popleft()
        self._stamps.append(now)
