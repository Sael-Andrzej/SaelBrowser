from __future__ import annotations

from collections import defaultdict, deque
import time


class RateLimiter:
    def __init__(self, limit: int, window_seconds: int, clock=time.monotonic,
                 max_clients: int = 10_000) -> None:
        self._limit = limit
        self._window = window_seconds
        self._clock = clock
        self._max_clients = max_clients
        self._requests: dict[str, deque[float]] = defaultdict(deque)

    def allow(self, client_id: str) -> bool:
        now = self._clock()
        if client_id not in self._requests and len(self._requests) >= self._max_clients:
            oldest = next(iter(self._requests))
            self._requests.pop(oldest, None)
        history = self._requests[client_id]
        while history and history[0] <= now - self._window:
            history.popleft()
        if len(history) >= self._limit:
            return False
        history.append(now)
        return True
