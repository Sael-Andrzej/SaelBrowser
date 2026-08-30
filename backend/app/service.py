from __future__ import annotations

import asyncio
import logging
import time

from .cache import TtlCache, cache_key
from .models import EvidenceRequest, EvidenceResponse
from .providers import EvidenceProvider

logger = logging.getLogger("sael.evidence")


class EvidenceService:
    def __init__(self, providers: list[EvidenceProvider], cache: TtlCache, timeout: float, max_results: int) -> None:
        self.providers = providers
        self.cache = cache
        self.timeout = timeout
        self.max_results = max_results
        self._search_lock = asyncio.Lock()

    async def search(self, request: EvidenceRequest) -> EvidenceResponse:
        key = cache_key(request)
        cached = self.cache.get(key)
        if cached is not None:
            cached.cacheHit = True
            logger.info("evidence_request cache=hit results=%d", len(cached.evidence))
            return cached
        async with self._search_lock:
            cached = self.cache.get(key)
            if cached is not None:
                cached.cacheHit = True
                logger.info("evidence_request cache=hit results=%d", len(cached.evidence))
                return cached
            return await self._search_uncached(request, key)

    async def _search_uncached(self, request: EvidenceRequest, key: str) -> EvidenceResponse:
        started = time.monotonic()
        warnings: list[str] = []
        evidence = []
        for provider in self.providers:
            if not provider.available:
                warnings.append(f"{provider.name}: unavailable")
                continue
            try:
                results = await asyncio.wait_for(provider.search(request), timeout=self.timeout)
                evidence.extend(results[: self.max_results])
                logger.info("provider=%s results=%d", provider.name, len(results))
            except TimeoutError:
                warnings.append(f"{provider.name}: timeout")
                logger.warning("provider=%s error=timeout", provider.name)
            except Exception as error:
                warnings.append(f"{provider.name}: error")
                logger.warning("provider=%s error=%s", provider.name, type(error).__name__)
        response = EvidenceResponse(
            query=request.claim, evidence=evidence[: self.max_results], warnings=warnings, cacheHit=False
        )
        self.cache.put(key, response)
        logger.info("evidence_request cache=miss results=%d duration_ms=%d", len(response.evidence),
                    int((time.monotonic() - started) * 1000))
        return response
