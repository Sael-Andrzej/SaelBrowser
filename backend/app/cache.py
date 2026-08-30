from __future__ import annotations

from copy import deepcopy
from dataclasses import dataclass
import hashlib
import time
import unicodedata

from .models import EvidenceRequest, EvidenceResponse


def cache_key(request: EvidenceRequest) -> str:
    claim = " ".join(unicodedata.normalize("NFKC", request.claim).casefold().split())
    raw = "\x1f".join((claim, str(request.publishedAt or ""), request.language, request.sourceUrl or ""))
    return hashlib.sha256(raw.encode()).hexdigest()


@dataclass
class _Entry:
    expires_at: float
    value: EvidenceResponse


class TtlCache:
    def __init__(self, ttl_seconds: int, clock=time.monotonic, max_entries: int = 1_000) -> None:
        self._ttl = ttl_seconds
        self._clock = clock
        self._max_entries = max_entries
        self._items: dict[str, _Entry] = {}

    def get(self, key: str) -> EvidenceResponse | None:
        entry = self._items.get(key)
        if entry is None:
            return None
        if entry.expires_at <= self._clock():
            self._items.pop(key, None)
            return None
        return deepcopy(entry.value)

    def put(self, key: str, value: EvidenceResponse) -> None:
        now = self._clock()
        expired = [item_key for item_key, entry in self._items.items() if entry.expires_at <= now]
        for item_key in expired:
            self._items.pop(item_key, None)
        while len(self._items) >= self._max_entries:
            self._items.pop(next(iter(self._items)))
        self._items[key] = _Entry(now + self._ttl, deepcopy(value))
