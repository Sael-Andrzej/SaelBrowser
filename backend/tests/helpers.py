from __future__ import annotations

import asyncio

from app.models import EvidenceItem, EvidenceRequest, Provenance, SourceType, Stance
from app.providers import EvidenceProvider


def item(
    suffix: str = "one", *, stance: Stance = Stance.UNKNOWN,
    snippet: str = "Potential evidence snippet", domain: str = "evidence.example",
) -> EvidenceItem:
    return EvidenceItem(
        id=suffix, claim="Inflacja w Polsce wyniosła 3,1%.", snippet=snippet,
        url=f"https://{domain}/{suffix}", domain=domain, publisher="Evidence Publisher",
        sourceType=SourceType.FACT_CHECK, stance=stance,
        provenance=Provenance.GOOGLE_FACT_CHECK, primarySourceId="primary-1",
        provider="fake", providerConfidence=0.75,
    )


class FakeProvider(EvidenceProvider):
    name = "fake"
    available = True

    def __init__(self, results=None, error: Exception | None = None, delay: float = 0.0) -> None:
        self.results = list(results or [])
        self.error = error
        self.delay = delay
        self.calls = 0

    async def search(self, request: EvidenceRequest) -> list[EvidenceItem]:
        self.calls += 1
        if self.delay:
            await asyncio.sleep(self.delay)
        if self.error:
            raise self.error
        return self.results
